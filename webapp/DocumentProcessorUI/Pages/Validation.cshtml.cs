using DocumentProcessorUI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DocumentProcessorUI.Pages;

public class ValidationModel : PageModel
{
    private readonly ICosmosDbService _cosmosDbService;
    private readonly ILogger<ValidationModel> _logger;

    public ValidationModel(ICosmosDbService cosmosDbService, ILogger<ValidationModel> logger)
    {
        _cosmosDbService = cosmosDbService;
        _logger = logger;
    }

    public IEnumerable<ProcessedDocument> ProcessedDocuments { get; set; } = new List<ProcessedDocument>();
    
    [BindProperty(SupportsGet = true)]
    public string? DocumentType { get; set; }
    
    [BindProperty(SupportsGet = true)]
    public DateTime? DateFrom { get; set; }
    
    [BindProperty(SupportsGet = true)]
    public DateTime? DateTo { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            // Load processed documents with filters
            ProcessedDocuments = await _cosmosDbService.GetProcessedDocumentsAsync(DocumentType, pageSize: 50);
            
            // Apply date filters if specified
            if (DateFrom.HasValue || DateTo.HasValue)
            {
                ProcessedDocuments = ProcessedDocuments.Where(d =>
                {
                    if (DateFrom.HasValue && d.processed_date < DateFrom.Value)
                        return false;
                    if (DateTo.HasValue && d.processed_date > DateTo.Value.AddDays(1))
                        return false;
                    return true;
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading processed documents");
            ProcessedDocuments = new List<ProcessedDocument>();
        }
    }
}