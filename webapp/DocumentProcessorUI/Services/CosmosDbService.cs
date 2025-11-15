using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DocumentProcessorUI.Services;

public interface ICosmosDbService
{
    Task<ProcessedDocument?> GetProcessedDocumentAsync(string documentId);
    Task<ProcessedDocument?> GetProcessedDocumentByNameAsync(string documentName);
    Task<IEnumerable<ProcessedDocument>> GetProcessedDocumentsAsync(string? documentType = null, int pageSize = 50);
    Task<bool> IsDocumentProcessedAsync(string documentName);
}

public class CosmosDbService : ICosmosDbService
{
    private readonly CosmosClient _cosmosClient;
    private readonly Container _container;
    private readonly ILogger<CosmosDbService> _logger;

    public CosmosDbService(IOptions<CosmosDbOptions> options, ILogger<CosmosDbService> logger)
    {
        var cosmosOptions = options.Value;
        _cosmosClient = new CosmosClient(cosmosOptions.Endpoint, cosmosOptions.Key);
        _container = _cosmosClient.GetContainer(cosmosOptions.DatabaseName, cosmosOptions.ContainerName);
        _logger = logger;
    }

    public async Task<ProcessedDocument?> GetProcessedDocumentAsync(string documentId)
    {
        try
        {
            var response = await _container.ReadItemAsync<ProcessedDocument>(documentId, new PartitionKey(documentId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving document: {documentId}");
            throw;
        }
    }

    public async Task<ProcessedDocument?> GetProcessedDocumentByNameAsync(string documentName)
    {
        try
        {
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.document_name = @documentName ORDER BY c.processed_date DESC")
                .WithParameter("@documentName", documentName);

            using var iterator = _container.GetItemQueryIterator<ProcessedDocument>(query);
            
            if (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                return response.FirstOrDefault();
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving document by name: {documentName}");
            throw;
        }
    }

    public async Task<IEnumerable<ProcessedDocument>> GetProcessedDocumentsAsync(string? documentType = null, int pageSize = 50)
    {
        try
        {
            var queryText = documentType != null 
                ? "SELECT * FROM c WHERE c.document_type = @documentType ORDER BY c.processed_date DESC" 
                : "SELECT * FROM c ORDER BY c.processed_date DESC";

            var query = new QueryDefinition(queryText);
            if (documentType != null)
            {
                query = query.WithParameter("@documentType", documentType);
            }

            var results = new List<ProcessedDocument>();
            using var iterator = _container.GetItemQueryIterator<ProcessedDocument>(query, requestOptions: new QueryRequestOptions { MaxItemCount = pageSize });

            while (iterator.HasMoreResults && results.Count < pageSize)
            {
                var response = await iterator.ReadNextAsync();
                results.AddRange(response);
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving processed documents");
            throw;
        }
    }

    public async Task<bool> IsDocumentProcessedAsync(string documentName)
    {
        try
        {
            var document = await GetProcessedDocumentByNameAsync(documentName);
            return document != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error checking if document is processed: {documentName}");
            return false;
        }
    }
}

// Data model for processed documents
public class ProcessedDocument
{
    public string id { get; set; } = string.Empty;
    public string document_name { get; set; } = string.Empty;
    public string document_type { get; set; } = string.Empty;
    public string container_name { get; set; } = string.Empty;
    public JsonElement extracted_data { get; set; }
    public DateTime processed_date { get; set; }
    public int file_size { get; set; }
    public string processing_version { get; set; } = string.Empty;
    public JsonElement metadata { get; set; }
    public string? searchable_text { get; set; }
}