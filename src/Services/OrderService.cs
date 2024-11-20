using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using System;
using System.Collections.Generic;
using System.Linq; // Ensure this is included
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using haworks.Contracts;
using haworks.Db;
using haworks.Controllers;
using haworks.Settings;

namespace haworks.Services
{
    public class OrderService
    {
        private readonly IOrderRepository _orderRepository;
        private readonly IProductRepository _productRepository;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly BlobSettings _blobSettings;

        public OrderService(IOrderRepository orderRepository, IProductRepository productRepository, BlobServiceClient blobServiceClient, IOptions<BlobSettings> blobSettings)
        {
            _orderRepository = orderRepository;
            _productRepository = productRepository;
            _blobServiceClient = blobServiceClient;
            _blobSettings = blobSettings.Value;
        }

        public async Task<string> CreateOrderAsync(Guid userId, List<CheckoutItem> items)
        {
            var order = new Order
            {
                UserId = userId.ToString(),
                OrderDate = DateTime.UtcNow,
                TotalAmount = items.Sum(item => item.Price * item.Quantity), // Ensure System.Linq is included
                Status = OrderStatus.Pending,
                OrderItems = items.Select(item => new OrderItem
                {
                    ProductId = item.ProductId,
                    Quantity = (int)item.Quantity, // Cast from long to int if needed
                    Price = item.Price
                }).ToList() // Ensure System.Linq is included
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
                var product = await _productRepository.GetProductByIdAsync(orderItem.ProductId) ?? throw new InvalidOperationException("Product not found.");
                var blobContainerClient = _blobServiceClient.GetBlobContainerClient(_blobSettings.ContainerName);
                var blobClient = blobContainerClient.GetBlobClient(product.BlobName);

                if (!await blobClient.ExistsAsync())
                {
                    throw new InvalidOperationException("Digital asset not found.");
                }

                var downloadLink = blobClient.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddHours(1)).ToString();
                downloadLinks.Add(downloadLink);
            }

            return downloadLinks;
        }
    }
}
