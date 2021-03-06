using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace SqsPoller
{
    internal class SqsPollerHostedService: BackgroundService
    {
        private readonly AmazonSQSClient _amazonSqsClient;
        private readonly SqsPollerConfig _config;
        private readonly IConsumerResolver _consumerResolver;
        private readonly ILogger<SqsPollerHostedService> _logger;

        public SqsPollerHostedService(
            AmazonSQSClient amazonSqsClient, 
            SqsPollerConfig config, 
            IConsumerResolver consumerResolver,
            ILogger<SqsPollerHostedService> logger)
        {
            _amazonSqsClient = amazonSqsClient;
            _config = config;
            _consumerResolver = consumerResolver;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var queueUrl = !string.IsNullOrEmpty(_config.QueueUrl)
                ? _config.QueueUrl
                : (await _amazonSqsClient.GetQueueUrlAsync(_config.QueueName, stoppingToken)).QueueUrl;
            while (!stoppingToken.IsCancellationRequested)
            {
                await Handle(queueUrl, stoppingToken);
            }
        }

        private async Task Handle(string queueUrl, CancellationToken cancellationToken)
        {
            using var correlationIdScope = _logger.BeginScope(
                new Dictionary<string, object> {["correlation_id"] = Guid.NewGuid()});
            _logger.LogTrace("Start polling messages from a queue. correlation_id: {correlation_id}");
            try
            {
                var receiveMessageResult = await _amazonSqsClient
                    .ReceiveMessageAsync(new ReceiveMessageRequest
                    {
                        WaitTimeSeconds = _config.WaitTimeSeconds,
                        MaxNumberOfMessages = _config.MaxNumberOfMessages,
                        MessageAttributeNames = _config.MessageAttributeNames,
                        QueueUrl = queueUrl
                    }, cancellationToken);
                
                var messagesCount = receiveMessageResult.Messages.Count;
                _logger.LogTrace("{count} messages received", messagesCount);
                foreach (var message in receiveMessageResult.Messages)
                {
                    try
                    {
                        var messageType = message.MessageAttributes
                            .FirstOrDefault(pair => pair.Key == "MessageType")
                            .Value?.StringValue;

                        if (messageType != null)
                        {
                            _logger.LogTrace("Message Type is {message_type}", messageType);
                            await _consumerResolver.Resolve(message.Body, messageType, cancellationToken);
                        }
                        else
                        {
                            var body = JsonConvert.DeserializeObject<MessageBody>(message.Body);
                            messageType = body.MessageAttributes
                                .FirstOrDefault(pair => pair.Key == "MessageType").Value.Value;
                            _logger.LogTrace("Message Type is {message_type}", messageType);
                            await _consumerResolver.Resolve(body.Message, messageType, cancellationToken);
                        }

                        _logger.LogTrace("Deleting the message {message_id}", message.ReceiptHandle);
                        await _amazonSqsClient.DeleteMessageAsync(new DeleteMessageRequest
                        {
                            QueueUrl = queueUrl,
                            ReceiptHandle = message.ReceiptHandle
                        }, cancellationToken);

                        _logger.LogTrace(
                            "The message {message_id} has been deleted successfully",
                            message.ReceiptHandle);

                        if (cancellationToken.IsCancellationRequested)
                            return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            "Failed to handle message {message_id}. {@ex}", message.ReceiptHandle, ex);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to receive messages from the queue");
            }
        }
    }
}