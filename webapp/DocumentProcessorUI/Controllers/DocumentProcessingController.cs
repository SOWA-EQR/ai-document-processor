using DocumentProcessorUI.Hubs;
using DocumentProcessorUI.Services;
using DocumentProcessorUI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace DocumentProcessorUI.Controllers;

[ApiController]
[Route("api")]
public class DocumentProcessingController : ControllerBase
{
    private readonly IDocumentProcessingService _documentProcessingService;
    private readonly ICosmosDbService _cosmosDbService;
    private readonly IBlobStorageService _blobStorageService;
    private readonly IHubContext<DocumentProcessingHub, IDocumentProcessingHubClient> _hubContext;
    private readonly ILogger<DocumentProcessingController> _logger;

    public DocumentProcessingController(
        IDocumentProcessingService documentProcessingService,
        ICosmosDbService cosmosDbService,
        IBlobStorageService blobStorageService,
        IHubContext<DocumentProcessingHub, IDocumentProcessingHubClient> hubContext,
        ILogger<DocumentProcessingController> logger)
    {
        _documentProcessingService = documentProcessingService;
        _cosmosDbService = cosmosDbService;
        _blobStorageService = blobStorageService;
        _hubContext = hubContext;
        _logger = logger;
    }

    [HttpGet("test")]
    public IActionResult Test()
    {
        return Ok(new { message = "Controller is working!", timestamp = DateTime.UtcNow });
    }

    [HttpGet("documents")]
    public async Task<IActionResult> GetDocuments([FromQuery] string? documentType = null, [FromQuery] int pageSize = 50)
    {
        try
        {
            var documents = await _cosmosDbService.GetProcessedDocumentsAsync(documentType, pageSize);
            return Ok(documents);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving documents");
            return StatusCode(500, new { error = "Internal server error", message = ex.Message });
        }
    }

    [HttpPost("process-document")]
    public async Task<IActionResult> ProcessDocument([FromForm] IFormFile file, [FromForm] string processingId, [FromForm] string documentType)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded");
            }

            if (file.ContentType != "application/pdf")
            {
                return BadRequest("Only PDF files are supported");
            }

            // Create progress reporter that sends updates via SignalR
            var progress = new Progress<ProcessingProgress>(async (progressUpdate) =>
            {
                await _hubContext.Clients.Group($"processing_{processingId}")
                    .ReceiveProgressUpdate(processingId, progressUpdate.Percentage, progressUpdate.Status, progressUpdate.Message);
            });

            // Copy file content to memory before background processing
            byte[] fileContent;
            using (var memoryStream = new MemoryStream())
            {
                await file.CopyToAsync(memoryStream);
                fileContent = memoryStream.ToArray();
            }

            // Create a wrapper for the file content
            var fileWrapper = new FileContentWrapper
            {
                Content = fileContent,
                FileName = file.FileName,
                ContentType = file.ContentType
            };

            // Start processing in background
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await _documentProcessingService.ProcessDocumentAsync(fileWrapper, processingId, progress);
                    
                    if (result.IsSuccess)
                    {
                        await _hubContext.Clients.Group($"processing_{processingId}")
                            .ReceiveProcessingComplete(processingId, result);
                    }
                    else
                    {
                        await _hubContext.Clients.Group($"processing_{processingId}")
                            .ReceiveProcessingError(processingId, result.ErrorMessage ?? "Unknown error");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error in background processing for {processingId}");
                    await _hubContext.Clients.Group($"processing_{processingId}")
                        .ReceiveProcessingError(processingId, ex.Message);
                }
            });

            return Ok(new { processingId, status = "Started", message = "Processing initiated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error initiating document processing");
            return StatusCode(500, new { error = "Internal server error", message = ex.Message });
        }
    }

    [HttpGet("documents/{documentId}")]
    public async Task<IActionResult> GetDocument(string documentId)
    {
        try
        {
            var document = await _cosmosDbService.GetProcessedDocumentAsync(documentId);
            
            if (document == null)
            {
                return NotFound();
            }

            return Ok(document);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving document: {documentId}");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("documents/{documentId}/validation-form")]
    public async Task<IActionResult> GetValidationForm(string documentId)
    {
        try
        {
            var document = await _cosmosDbService.GetProcessedDocumentAsync(documentId);
            
            if (document == null)
            {
                return NotFound();
            }

            // Generate validation form HTML based on document type
            var html = GenerateValidationFormHtml(document);
            return Content(html, "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error generating validation form: {documentId}");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPost("documents/{documentId}/validate")]
    public async Task<IActionResult> ValidateDocument(string documentId, [FromBody] object validationData)
    {
        try
        {
            var success = await _documentProcessingService.ValidateExtractedDataAsync(documentId, validationData);
            
            if (success)
            {
                return Ok(new { message = "Validation saved successfully" });
            }
            else
            {
                return BadRequest(new { error = "Validation failed" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error validating document: {documentId}");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("documents/{documentId}/export")]
    public async Task<IActionResult> ExportDocument(string documentId)
    {
        try
        {
            var document = await _cosmosDbService.GetProcessedDocumentAsync(documentId);
            
            if (document == null)
            {
                return NotFound();
            }

            // Generate JSON export
            var json = System.Text.Json.JsonSerializer.Serialize(document, new System.Text.Json.JsonSerializerOptions 
            { 
                WriteIndented = true 
            });

            var fileName = $"{document.document_name}_extracted_data.json";
            return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error exporting document: {documentId}");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("processing/{processingId}/status")]
    public async Task<IActionResult> GetProcessingStatus(string processingId)
    {
        try
        {
            var result = await _documentProcessingService.GetProcessingResultAsync(processingId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting processing status: {processingId}");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    private string GenerateValidationFormHtml(ProcessedDocument document)
    {
        if (document.document_type == "lease")
        {
            return GenerateLeaseValidationForm(document);
        }
        else if (document.document_type == "application")
        {
            return GenerateApplicationValidationForm(document);
        }
        else
        {
            return GenerateGenericValidationForm(document);
        }
    }

    private string GenerateLeaseValidationForm(ProcessedDocument document)
    {
        var extractedData = document.extracted_data;
        
        return $@"
        <form id=""validationForm"">
            <div class=""row"">
                <div class=""col-md-6"">
                    <h6>Property Information</h6>
                    <div class=""mb-3"">
                        <label class=""form-label"">Property Address</label>
                        <input type=""text"" class=""form-control"" name=""property_address"" value=""{GetJsonValue(extractedData, "property_address")}"">
                    </div>
                    <div class=""mb-3"">
                        <label class=""form-label"">Unit Number</label>
                        <input type=""text"" class=""form-control"" name=""unit_number"" value=""{GetJsonValue(extractedData, "unit_number")}"">
                    </div>
                    <div class=""mb-3"">
                        <label class=""form-label"">Property Type</label>
                        <select class=""form-select"" name=""property_type"">
                            <option value=""apartment"" {(GetJsonValue(extractedData, "property_type") == "apartment" ? "selected" : "")}>Apartment</option>
                            <option value=""house"" {(GetJsonValue(extractedData, "property_type") == "house" ? "selected" : "")}>House</option>
                            <option value=""condo"" {(GetJsonValue(extractedData, "property_type") == "condo" ? "selected" : "")}>Condo</option>
                        </select>
                    </div>
                </div>
                <div class=""col-md-6"">
                    <h6>Tenant Information</h6>
                    <div class=""mb-3"">
                        <label class=""form-label"">Tenant Name</label>
                        <input type=""text"" class=""form-control"" name=""tenant_name"" value=""{GetJsonValue(extractedData, "tenant_name")}"">
                    </div>
                    <div class=""mb-3"">
                        <label class=""form-label"">Tenant Contact</label>
                        <input type=""text"" class=""form-control"" name=""tenant_contact"" value=""{GetJsonValue(extractedData, "tenant_contact")}"">
                    </div>
                </div>
            </div>
            <div class=""row"">
                <div class=""col-md-6"">
                    <h6>Financial Terms</h6>
                    <div class=""mb-3"">
                        <label class=""form-label"">Monthly Rent</label>
                        <input type=""text"" class=""form-control"" name=""monthly_rent"" value=""{GetJsonValue(extractedData, "monthly_rent")}"">
                    </div>
                    <div class=""mb-3"">
                        <label class=""form-label"">Security Deposit</label>
                        <input type=""text"" class=""form-control"" name=""security_deposit"" value=""{GetJsonValue(extractedData, "security_deposit")}"">
                    </div>
                </div>
                <div class=""col-md-6"">
                    <h6>Lease Terms</h6>
                    <div class=""mb-3"">
                        <label class=""form-label"">Lease Start Date</label>
                        <input type=""date"" class=""form-control"" name=""lease_start_date"" value=""{GetJsonValue(extractedData, "lease_start_date")}"">
                    </div>
                    <div class=""mb-3"">
                        <label class=""form-label"">Lease End Date</label>
                        <input type=""date"" class=""form-control"" name=""lease_end_date"" value=""{GetJsonValue(extractedData, "lease_end_date")}"">
                    </div>
                </div>
            </div>
            <div class=""mb-3"">
                <label class=""form-label"">Special Terms</label>
                <textarea class=""form-control"" name=""special_terms"" rows=""3"">{GetJsonValue(extractedData, "special_terms")}</textarea>
            </div>
        </form>";
    }

    private string GenerateApplicationValidationForm(ProcessedDocument document)
    {
        var extractedData = document.extracted_data;
        
        return $@"
        <form id=""validationForm"">
            <div class=""row"">
                <div class=""col-md-6"">
                    <h6>Applicant Information</h6>
                    <div class=""mb-3"">
                        <label class=""form-label"">Applicant Name</label>
                        <input type=""text"" class=""form-control"" name=""applicant_name"" value=""{GetJsonValue(extractedData, "applicant_name")}"">
                    </div>
                    <div class=""mb-3"">
                        <label class=""form-label"">Phone Number</label>
                        <input type=""text"" class=""form-control"" name=""applicant_phone"" value=""{GetJsonValue(extractedData, "applicant_phone")}"">
                    </div>
                    <div class=""mb-3"">
                        <label class=""form-label"">Email</label>
                        <input type=""email"" class=""form-control"" name=""applicant_email"" value=""{GetJsonValue(extractedData, "applicant_email")}"">
                    </div>
                </div>
                <div class=""col-md-6"">
                    <h6>Employment Information</h6>
                    <div class=""mb-3"">
                        <label class=""form-label"">Employer Name</label>
                        <input type=""text"" class=""form-control"" name=""employer_name"" value=""{GetJsonValue(extractedData, "employer_name")}"">
                    </div>
                    <div class=""mb-3"">
                        <label class=""form-label"">Job Title</label>
                        <input type=""text"" class=""form-control"" name=""job_title"" value=""{GetJsonValue(extractedData, "job_title")}"">
                    </div>
                    <div class=""mb-3"">
                        <label class=""form-label"">Monthly Income</label>
                        <input type=""text"" class=""form-control"" name=""monthly_income"" value=""{GetJsonValue(extractedData, "monthly_income")}"">
                    </div>
                </div>
            </div>
            <div class=""mb-3"">
                <label class=""form-label"">Current Address</label>
                <textarea class=""form-control"" name=""current_address"" rows=""2"">{GetJsonValue(extractedData, "current_address")}</textarea>
            </div>
        </form>";
    }

    private string GenerateGenericValidationForm(ProcessedDocument document)
    {
        return @"
        <form id=""validationForm"">
            <div class=""alert alert-info"">
                <i class=""fas fa-info-circle""></i>
                This document type doesn't have a specific validation form. You can review the extracted data and confirm it's correct.
            </div>
            <div class=""mb-3"">
                <div class=""form-check"">
                    <input class=""form-check-input"" type=""checkbox"" name=""data_validated"" id=""dataValidated"" required>
                    <label class=""form-check-label"" for=""dataValidated"">
                        I have reviewed the extracted data and confirm it is accurate
                    </label>
                </div>
            </div>
            <div class=""mb-3"">
                <label class=""form-label"">Comments (Optional)</label>
                <textarea class=""form-control"" name=""validation_comments"" rows=""3"" placeholder=""Add any comments about the validation...""></textarea>
            </div>
        </form>";
    }

    private string GetJsonValue(System.Text.Json.JsonElement element, string propertyName)
    {
        try
        {
            if (element.TryGetProperty(propertyName, out var value))
            {
                return value.GetString() ?? "";
            }
            return "";
        }
        catch
        {
            return "";
        }
    }
}