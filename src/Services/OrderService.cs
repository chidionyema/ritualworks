using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using System;
using System.Collections.Generic;
using System.Linq; // Ensure this is included
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using haworks.Contracts;
using haworks.Controllers;
using haworks.Db;
using haworks.Models;
using Minio;
using Minio.DataModel;

namespace haworks.Services
{
    public class OrderService
    {
        private readonly IOrderRepository _orderRepository;
        private readonly IProductRepository _productRepository;
        private readonly MinioClient _minioClient;
        private readonly MinioSettings _minioSettings;

        public OrderService(IOrderRepository orderRepository, IProductRepository productRepository, MinioClient minioClient, MinioSettings minioSettings)
        {
            _orderRepository = orderRepository;
            _productRepository = productRepository;
            _minioClient = minioClient;
            _minioSettings = minioSettings ?? throw new ArgumentNullException(nameof(minioSettings));
        }

        public async Task<string> CreateOrderAsync(Guid userId, List<CheckoutItem> items)
        {
            var order = new Order
            {
                UserId = userId.ToString(),
                OrderDate = DateTime.UtcNow,
                TotalAmount = items.Sum(item => item.Price * item.Quantity),
                Status = OrderStatus.Pending,
                OrderItems = items.Select(item => new OrderItem
                {
                    ProductId = item.ProductId,
                    Quantity = (int)item.Quantity,
                    Price = item.Price
                }).ToList()
            };

            await _orderRepository.CreateOrderAsync(order);
            return order.Id.ToString();
        }

        public async Task<Order?> GetOrderByIdAsync(Guid orderId)
        {
            return await _orderRepository.GetOrderByIdAsync(orderId);
        }

        public async Task<List<string>> GenerateDownloadLinksAsync(Guid orderId)
        {
            var order = await _orderRepository.GetOrderByIdAsync(orderId);
            if (order == null)
            {
                throw new ArgumentException("Order not found.");
            }

            var downloadLinks = new List<string>();
            foreach (var orderItem in order.OrderItems)
            {
                var product = await _productRepository.GetProductByIdAsync(orderItem.ProductId) 
                              ?? throw new InvalidOperationException("Product not found.");

                // Get digital content (assets) for the product
                var digitalContents = product.Contents?
                    .Where(content => content.ContentType == ContentType.Asset)
                    .ToList() ?? new List<Content>();

                foreach (var content in digitalContents)
                {
                    var objectName = content.BlobName;

                  /*  // Check if the object exists in the bucket
                    var objectStat = await _minioClient.StatObjectAsync(
                        new StatObjectArgs()
                            .WithBucket(_minioSettings.BucketName)
                            .WithObject(objectName)
                    );

                    // Generate a pre-signed URL for the object
                    var downloadLink = await _minioClient.PresignedGetObjectAsync(
                        new PresignedGetObjectArgs()
                            .WithBucket(_minioSettings.BucketName)
                            .WithObject(objectName)
                            .WithExpiry(60 * 60) // 1 hour expiry
                    );
*/
                   // downloadLinks.Add(downloadLink);
                }
            }

            return downloadLinks;
        }
    }
}
