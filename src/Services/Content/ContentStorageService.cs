using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
using haworks.Contracts;
using haworks.Dto;
using haworks.PolicyFactory;
using Polly;

namespace haworks.Services
{
    public class ContentStorageService : IContentStorageService
    {
        private readonly IMinioClient _minioClient;
        private readonly ILogger<ContentStorageService> _logger;
        private readonly IAsyncPolicy _resiliencePolicy;

        public ContentStorageService(
            IMinioClient minioClient,
            ILogger<ContentStorageService> logger,
            IConfiguration config)
        {
            _minioClient = minioClient ?? throw new ArgumentNullException(nameof(minioClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _resiliencePolicy = ResiliencePolicyFactory.CreateVaultPolicy(config, logger);
        }

        public async Task<ContentUploadResult> UploadAsync(
            Stream fileStream,
            string bucketName,
            string objectName,
            string contentType,
            IDictionary<string, string> metadata,
            CancellationToken cancellationToken = default) // Added CancellationToken
        {
            ContentUploadResult? result = null;
            await _resiliencePolicy.ExecuteAsync(
                async (context, ct) => // Correct lambda parameters
                {
                    var putObjectArgs = new PutObjectArgs()
                        .WithBucket(bucketName)
                        .WithObject(objectName)
                        .WithStreamData(fileStream)
                        .WithObjectSize(fileStream.Length)
                        .WithContentType(contentType)
                        .WithHeaders(metadata);

                    await _minioClient.PutObjectAsync(putObjectArgs, ct); // Use ct here

                    result = new ContentUploadResult(
                        bucketName,
                        objectName,
                        contentType,
                        fileStream.Length,
                        VersionId: string.Empty,
                        StorageDetails: string.Empty,
                        Path: string.Empty
                    );
                },
                new Context(), // Pass Polly context
                cancellationToken
            );
            
            return result ?? throw new InvalidOperationException("Upload failed");
        }

        public async Task<string> GetPresignedUrlAsync(
            string bucketName,
            string objectName,
            TimeSpan expiry,
            bool requireAuth = true,
            CancellationToken cancellationToken = default) // Added CancellationToken
        {
            string? url = null;
            await _resiliencePolicy.ExecuteAsync(
                async (context, ct) => // Correct lambda parameters
                {
                    var args = new PresignedGetObjectArgs()
                        .WithBucket(bucketName)
                        .WithObject(objectName)
                        .WithExpiry((int)expiry.TotalSeconds);

                    url = await _minioClient.PresignedGetObjectAsync(args);
                },
                new Context(),
                cancellationToken
            );
            
            return url ?? throw new InvalidOperationException("Failed to generate URL");
        }

        public async Task<Stream> DownloadAsync(
            string bucketName,
            string objectName,
            CancellationToken cancellationToken = default) // Added CancellationToken
        {
            Stream? resultStream = null;
            await _resiliencePolicy.ExecuteAsync(
                async (context, ct) => // Correct lambda parameters
                {
                    var memoryStream = new MemoryStream();
                    var args = new GetObjectArgs()
                        .WithBucket(bucketName)
                        .WithObject(objectName)
                        .WithCallbackStream(async stream =>
                        {
                            await stream.CopyToAsync(memoryStream, ct); // Use ct here
                        });

                    await _minioClient.GetObjectAsync(args, ct);
                    memoryStream.Position = 0;
                    resultStream = memoryStream;
                },
                new Context(),
                cancellationToken
            );
            
            return resultStream ?? throw new InvalidOperationException("Download failed");
        }

        public async Task DeleteAsync(
            string bucketName,
            string objectName,
            CancellationToken cancellationToken = default) // Added CancellationToken
        {
            await _resiliencePolicy.ExecuteAsync(
                async (context, ct) => // Correct lambda parameters
                {
                    await _minioClient.RemoveObjectAsync(
                        new RemoveObjectArgs()
                            .WithBucket(bucketName)
                            .WithObject(objectName),
                        ct // Use ct here
                    );
                },
                new Context(),
                cancellationToken
            );
        }

        public async Task EnsureBucketExistsAsync(
            string bucketName,
            CancellationToken cancellationToken = default) // Added CancellationToken
        {
            await _resiliencePolicy.ExecuteAsync(
                async (context, ct) => // Correct lambda parameters
                {
                    bool exists = await _minioClient.BucketExistsAsync(
                        new BucketExistsArgs().WithBucket(bucketName),
                        ct // Use ct here
                    );
                    
                    if (!exists)
                    {
                        await _minioClient.MakeBucketAsync(
                            new MakeBucketArgs().WithBucket(bucketName),
                            ct // Use ct here
                        );
                        await SetBucketPolicyAsync(bucketName, ct);
                    }
                },
                new Context(),
                cancellationToken
            );
        }

        private async Task SetBucketPolicyAsync(
            string bucketName,
            CancellationToken ct) // Already has CancellationToken
        {
            var policy = $@"{{
                ""Version"": ""2012-10-17"",
                ""Statement"": [
                    {{
                        ""Effect"": ""Deny"",
                        ""Principal"": ""*"",
                        ""Action"": ""s3:*"",
                        ""Resource"": ""arn:aws:s3:::{bucketName}/*"",
                        ""Condition"": {{
                            ""Bool"": {{ ""aws:SecureTransport"": ""false"" }}
                        }}
                    }}
                ]
            }}";

            await _minioClient.SetPolicyAsync(
                new SetPolicyArgs()
                    .WithBucket(bucketName)
                    .WithPolicy(policy),
                ct // Use ct here
            );
        }
    }
}