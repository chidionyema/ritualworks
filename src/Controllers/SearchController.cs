﻿using Microsoft.AspNetCore.Mvc;
using RitualWorks.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
namespace RitualWorks.Controllers
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

        [HttpGet("rituals")]
        [Authorize]
        public async Task<IActionResult> SearchRituals([FromQuery] string query)
        {
            var results = await _searchService.SearchRitualsAsync(query);
            return Ok(results);
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

