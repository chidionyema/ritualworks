﻿using Microsoft.AspNetCore.Mvc;
using haworks.Services;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
namespace haworks.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SearchController : ControllerBase
    {
        private readonly SearchService _searchService;

        public SearchController(SearchService searchService)
        {
            _searchService = searchService;
        }

        [HttpGet("products")]
        [Authorize]
        public async Task<IActionResult> SearchProducts([FromQuery] string query)
        {
            var results = await _searchService.SearchProductsAsync(query);
            return Ok(results);
        }
    }

}

