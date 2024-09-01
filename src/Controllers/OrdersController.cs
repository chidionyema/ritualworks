using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RitualWorks.Db;
using RitualWorks.Services;

namespace RitualWorks.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrdersController : ControllerBase
    {
        private readonly OrderService _orderService;
        private readonly ILogger<OrdersController> _logger;

        public OrdersController(OrderService orderService, ILogger<OrdersController> logger)
        {
            _orderService = orderService;
            _logger = logger;
        }

        [HttpGet("{orderId}")]
        [Authorize]
        public async Task<IActionResult> GetOrderById(Guid orderId)
        {
            var order = await _orderService.GetOrderByIdAsync(orderId);

            if (order == null)
            {
                return NotFound();
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (order.UserId != userId)
            {
                return Unauthorized();
            }

            return Ok(order);
        }

        [HttpGet("download-link/{orderId}")]
        [Authorize]
        public async Task<IActionResult> GetDownloadLink(Guid orderId)
        {
            var order = await _orderService.GetOrderByIdAsync(orderId);

            if (order == null)
            {
                return NotFound();
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (order.UserId != userId)
            {
                return Unauthorized();
            }

            if (order.Status != OrderStatus.Completed)
            {
                return BadRequest("Order is not completed.");
            }

            try
            {
                var downloadLinks = await _orderService.GenerateDownloadLinksAsync(orderId);
                return Ok(new { downloadUrls = downloadLinks });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating download link.");
                return StatusCode(500, "Error generating download link.");
            }
        }
    }
}