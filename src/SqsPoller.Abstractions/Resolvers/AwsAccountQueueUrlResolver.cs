﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace SqsPoller.Abstractions.Resolvers
{
    public class AwsAccountQueueUrlResolver
    {
        private readonly IAmazonSQS _amazonSqsClient;

        private string BaseUrl { get; set; }

        public AwsAccountQueueUrlResolver(IAmazonSQS amazonSqsClient)
        {
            _amazonSqsClient = amazonSqsClient;
        }

        public async Task<string> Resolve(string queueName, string queueOwnerAwsAccountId = default, CancellationToken cancellationToken = default)
        {
            // Prefered way of making SQS queue URLs - no Amazon SQS Requests fees for GetQueueUrl/CreateQueue requests
            if (!string.IsNullOrEmpty(BaseUrl))
            {
                var uri = new Uri(new Uri(BaseUrl), queueName);
                return uri.AbsoluteUri;
            }

            var request = new GetQueueUrlRequest()
            {
                QueueName = queueName,
                QueueOwnerAWSAccountId = queueOwnerAwsAccountId
            };
            
            var queueUrlResponse = await _amazonSqsClient.GetQueueUrlAsync(request, cancellationToken);
            BaseUrl = RemoveFromEnd(queueUrlResponse.QueueUrl, queueName);
            return queueUrlResponse.QueueUrl;
        }
        
        private string RemoveFromEnd(string queueUrl, string queueName)
        {
            return queueUrl.EndsWith(queueName)
                ? queueUrl.Substring(0, queueUrl.Length - queueName.Length)
                : queueUrl;
        }
    }
}