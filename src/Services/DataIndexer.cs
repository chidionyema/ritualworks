using System;
using Microsoft.Extensions.Logging;
using haworks.Db;
using Nest;
using System.Threading.Tasks;

namespace haworks.Services
{
    public class DataIndexer
    {
        private readonly IElasticClient _client;
        private readonly ILogger<DataIndexer> _logger;

        public DataIndexer(IElasticClient client, ILogger<DataIndexer> logger)
        {
            _client = client;
            _logger = logger;
        }

        public async Task IndexProductAsync(Product product)
        {
            try
            {
                var response = await _client.IndexDocumentAsync(product);
                if (!response.IsValid)
                {
                    _logger.LogError("Failed to index product: {0}", response.OriginalException.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error indexing product: {0}", ex.Message);
            }
        }
    }

}

