using DocumentProcessorUI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DocumentProcessorUI.Pages;

public class IndexModel : PageModel
{
    private readonly IDocumentProcessingService _documentProcessingService;
    private readonly ICosmosDbService _cosmosDbService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IDocumentProcessingService documentProcessingService, 
        ICosmosDbService cosmosDbService,
        ILogger<IndexModel> logger)
    {
        _documentProcessingService = documentProcessingService;
        _cosmosDbService = cosmosDbService;
        _logger = logger;
    }

    public IEnumerable<ProcessedDocument> RecentDocuments { get; set; } = new List<ProcessedDocument>();

    public async Task OnGetAsync()
    {
        try
        {
            // Load recent processed documents
            RecentDocuments = await _cosmosDbService.GetProcessedDocumentsAsync(pageSize: 5);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading recent documents");
            RecentDocuments = new List<ProcessedDocument>();
        }
    }
}