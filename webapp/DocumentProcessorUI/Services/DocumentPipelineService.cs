using Microsoft.AspNetCore.SignalR;
using DocumentProcessorUI.Hubs;
using DocumentProcessorUI.Models;
using System.Text.Json;

namespace DocumentProcessorUI.Services
{
    public interface IDocumentPipelineService
    {
        Task<string> StartPipelineAsync(string blobName, string processingId);
        Task<PipelineStatus> GetPipelineStatusAsync(string instanceId);
        Task<PipelineResult> GetPipelineResultAsync(string instanceId);
        Task<bool> MonitorPipelineProgressAsync(string instanceId, string processingId, IProgress<ProcessingProgress> progress);
    }

    public class DocumentPipelineService : IDocumentPipelineService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IHubContext<DocumentProcessingHub> _hubContext;
        private readonly ICosmosDbService _cosmosDb;
        private readonly IBlobStorageService _blobStorage;
        private readonly ILogger<DocumentPipelineService> _logger;

        public DocumentPipelineService(
            HttpClient httpClient,
            IConfiguration configuration,
            IHubContext<DocumentProcessingHub> hubContext,
            ICosmosDbService cosmosDb,
            IBlobStorageService blobStorage,
            ILogger<DocumentPipelineService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _hubContext = hubContext;
            _cosmosDb = cosmosDb;
            _blobStorage = blobStorage;
            _logger = logger;

            var functionAppUrl = _configuration.GetValue<string>("AzureFunction:BaseUrl");
            var functionKey = _configuration.GetValue<string>("AzureFunction:DefaultFunctionKey");
            if (!string.IsNullOrEmpty(functionAppUrl))
            {
                _httpClient.BaseAddress = new Uri(functionAppUrl);
                // Add function key as default header if available
                if (!string.IsNullOrEmpty(functionKey))
                {
                    _httpClient.DefaultRequestHeaders.Add("x-functions-key", functionKey);
                }
            }
        }

        public async Task<string> StartPipelineAsync(string blobName, string processingId)
        {
            try
            {
                _logger.LogInformation($"Starting pipeline for blob: {blobName}, processingId: {processingId}");

                // Prepare the request payload for the Azure Function
                var requestPayload = new
                {
                    blobs = new[]
                    {
                        new
                        {
                            name = blobName,
                            url = $"https://{_configuration.GetValue<string>("AzureStorage:AccountName", "stzyxhglssdeciq")}.blob.core.windows.net/bronze/{blobName}",
                            container = "bronze",
                            size = 0 // Will be determined by the function
                        }
                    }
                };

                var json = JsonSerializer.Serialize(requestPayload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                // Call the Azure Function pipeline starter
                var functionKey = _configuration.GetValue<string>("AzureFunction:DefaultFunctionKey");
                var endpoint = !string.IsNullOrEmpty(functionKey) 
                    ? $"/api/client?code={functionKey}"
                    : "/api/client";
                var response = await _httpClient.PostAsync(endpoint, content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation($"Pipeline response: {responseContent}");
                    
                    // Try to deserialize as client endpoint response first
                    try
                    {
                        var clientResponse = JsonSerializer.Deserialize<ClientEndpointResponse>(responseContent);
                        if (clientResponse?.results != null && clientResponse.results.Length > 0)
                        {
                            var instanceId = clientResponse.results[0].id ?? string.Empty;
                            _logger.LogInformation($"Pipeline started successfully via client endpoint. Instance ID: {instanceId}");
                            
                            // Start monitoring in background
                            _ = Task.Run(() => MonitorPipelineProgressAsync(instanceId, processingId, 
                                new Progress<ProcessingProgress>()));
                            
                            return instanceId;
                        }
                    }
                    catch (JsonException)
                    {
                        // Fall back to Durable Functions response format
                        var result = JsonSerializer.Deserialize<AzureFunctionResponse>(responseContent);
                        _logger.LogInformation($"Pipeline started successfully via orchestrator endpoint. Instance ID: {result?.id}");
                        
                        // Start monitoring in background
                        _ = Task.Run(() => MonitorPipelineProgressAsync(result?.id ?? string.Empty, processingId, 
                            new Progress<ProcessingProgress>()));
                        
                        return result?.id ?? string.Empty;
                    }
                    
                    return string.Empty;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Failed to start pipeline. Status: {response.StatusCode}, Content: {errorContent}");
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        _logger.LogWarning("Azure Function endpoint not found. This indicates the Python functions are not deployed yet.");
                        throw new HttpRequestException($"Pipeline start failed: NotFound - The Azure Function endpoints are not available yet. Please ensure the Python functions are properly deployed to the Function App.");
                    }
                    
                    throw new HttpRequestException($"Pipeline start failed: {response.StatusCode} - {errorContent}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error starting pipeline for blob: {blobName}");
                throw;
            }
        }

        public async Task<PipelineStatus> GetPipelineStatusAsync(string instanceId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"/runtime/webhooks/durabletask/instances/{instanceId}");
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var status = JsonSerializer.Deserialize<DurableFunctionStatusResponse>(content);
                    
                    return new PipelineStatus
                    {
                        InstanceId = instanceId,
                        RuntimeStatus = status?.runtimeStatus ?? "Unknown",
                        IsCompleted = status?.runtimeStatus == "Completed",
                        IsFailed = status?.runtimeStatus == "Failed",
                        CreatedTime = status?.createdTime ?? DateTime.UtcNow,
                        LastUpdatedTime = status?.lastUpdatedTime ?? DateTime.UtcNow,
                        Output = status?.output
                    };
                }
                
                return new PipelineStatus
                {
                    InstanceId = instanceId,
                    RuntimeStatus = "Unknown",
                    IsCompleted = false,
                    IsFailed = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting pipeline status for instance: {instanceId}");
                return new PipelineStatus
                {
                    InstanceId = instanceId,
                    RuntimeStatus = "Error",
                    IsCompleted = false,
                    IsFailed = true
                };
            }
        }

        public async Task<PipelineResult> GetPipelineResultAsync(string instanceId)
        {
            try
            {
                var status = await GetPipelineStatusAsync(instanceId);
                
                if (status.IsCompleted)
                {
                    // Extract results from the pipeline output
                    var result = new PipelineResult
                    {
                        InstanceId = instanceId,
                        IsSuccess = true,
                        CompletedAt = status.LastUpdatedTime,
                        Steps = new List<PipelineStep>()
                    };

                    if (status.Output != null)
                    {
                        var outputJson = JsonSerializer.Serialize(status.Output);
                        var pipelineOutput = JsonSerializer.Deserialize<PipelineOutput[]>(outputJson);
                        
                        if (pipelineOutput?.Length > 0)
                        {
                            var firstResult = pipelineOutput[0];
                            result.ExtractedData = firstResult.extracted_data;
                            result.CosmosResult = firstResult.cosmos_result;
                            result.GoldBlobResult = firstResult.task_result;
                            
                            // Add pipeline steps
                            result.Steps.Add(new PipelineStep { Name = "Document Intelligence", Status = "Completed", CompletedAt = DateTime.UtcNow });
                            result.Steps.Add(new PipelineStep { Name = "AI Processing", Status = "Completed", CompletedAt = DateTime.UtcNow });
                            result.Steps.Add(new PipelineStep { Name = "Cosmos DB Storage", Status = "Completed", CompletedAt = DateTime.UtcNow });
                            result.Steps.Add(new PipelineStep { Name = "Gold Container", Status = "Completed", CompletedAt = DateTime.UtcNow });
                        }
                    }
                    
                    return result;
                }
                
                return new PipelineResult
                {
                    InstanceId = instanceId,
                    IsSuccess = false,
                    ErrorMessage = status.IsFailed ? "Pipeline execution failed" : "Pipeline not yet completed"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting pipeline result for instance: {instanceId}");
                return new PipelineResult
                {
                    InstanceId = instanceId,
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<bool> MonitorPipelineProgressAsync(string instanceId, string processingId, IProgress<ProcessingProgress> progress)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                var timeout = TimeSpan.FromMinutes(_configuration.GetValue<int>("DocumentProcessing:MaxWaitTimeMinutes", 10));
                var pollingInterval = TimeSpan.FromSeconds(_configuration.GetValue<int>("DocumentProcessing:PollingIntervalSeconds", 5));

                while (DateTime.UtcNow - startTime < timeout)
                {
                    var status = await GetPipelineStatusAsync(instanceId);
                    
                    // Calculate progress based on pipeline stage
                    var progressInfo = CalculateProgress(status.RuntimeStatus);
                    progress.Report(progressInfo);
                    
                    // Send real-time update via SignalR
                    await _hubContext.Clients.All.SendAsync("ProcessingUpdate", new
                    {
                        DocumentId = processingId,
                        InstanceId = instanceId,
                        Status = status.RuntimeStatus,
                        Message = GetStatusMessage(status.RuntimeStatus),
                        Progress = progressInfo.Percentage,
                        Timestamp = DateTime.UtcNow
                    });
                    
                    if (status.IsCompleted)
                    {
                        progress.Report(new ProcessingProgress 
                        { 
                            Percentage = 100, 
                            Status = "Completed", 
                            Message = "Document processing pipeline completed successfully!" 
                        });
                        
                        await _hubContext.Clients.All.SendAsync("ProcessingComplete", new
                        {
                            DocumentId = processingId,
                            InstanceId = instanceId,
                            Success = true,
                            CompletedAt = DateTime.UtcNow
                        });
                        
                        return true;
                    }
                    
                    if (status.IsFailed)
                    {
                        progress.Report(new ProcessingProgress 
                        { 
                            Percentage = 0, 
                            Status = "Failed", 
                            Message = "Document processing pipeline failed." 
                        });
                        
                        await _hubContext.Clients.All.SendAsync("ProcessingFailed", new
                        {
                            DocumentId = processingId,
                            InstanceId = instanceId,
                            Error = "Pipeline execution failed",
                            FailedAt = DateTime.UtcNow
                        });
                        
                        return false;
                    }
                    
                    await Task.Delay(pollingInterval);
                }
                
                // Timeout
                progress.Report(new ProcessingProgress 
                { 
                    Percentage = 0, 
                    Status = "Timeout", 
                    Message = "Document processing pipeline timed out." 
                });
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error monitoring pipeline progress for instance: {instanceId}");
                return false;
            }
        }

        private ProcessingProgress CalculateProgress(string runtimeStatus)
        {
            return runtimeStatus switch
            {
                "Running" => new ProcessingProgress { Percentage = 30, Status = "Processing", Message = "Document Intelligence extraction in progress..." },
                "Pending" => new ProcessingProgress { Percentage = 10, Status = "Pending", Message = "Pipeline starting..." },
                "Completed" => new ProcessingProgress { Percentage = 100, Status = "Completed", Message = "Processing completed successfully!" },
                "Failed" => new ProcessingProgress { Percentage = 0, Status = "Failed", Message = "Processing failed." },
                _ => new ProcessingProgress { Percentage = 20, Status = "Processing", Message = "Processing document..." }
            };
        }

        private string GetStatusMessage(string runtimeStatus)
        {
            return runtimeStatus switch
            {
                "Running" => "Processing document through AI pipeline...",
                "Pending" => "Starting document processing pipeline...",
                "Completed" => "Document successfully processed and stored in Gold container!",
                "Failed" => "Document processing failed.",
                _ => "Processing document..."
            };
        }
    }

    // Model classes for pipeline integration
    public class PipelineStatus
    {
        public string InstanceId { get; set; } = string.Empty;
        public string RuntimeStatus { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public bool IsFailed { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime LastUpdatedTime { get; set; }
        public object? Output { get; set; }
    }

    public class PipelineResult
    {
        public string InstanceId { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? ExtractedData { get; set; }
        public object? CosmosResult { get; set; }
        public object? GoldBlobResult { get; set; }
        public List<PipelineStep> Steps { get; set; } = new();
    }

    public class PipelineStep
    {
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime? CompletedAt { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class AzureFunctionResponse
    {
        public string id { get; set; } = string.Empty;
        public string statusQueryGetUri { get; set; } = string.Empty;
        public string sendEventPostUri { get; set; } = string.Empty;
        public string terminatePostUri { get; set; } = string.Empty;
    }

    public class DurableFunctionStatusResponse
    {
        public string name { get; set; } = string.Empty;
        public string instanceId { get; set; } = string.Empty;
        public string runtimeStatus { get; set; } = string.Empty;
        public object? input { get; set; }
        public object? output { get; set; }
        public DateTime createdTime { get; set; }
        public DateTime lastUpdatedTime { get; set; }
    }

    public class PipelineOutput
    {
        public object blob { get; set; } = new();
        public string extracted_data { get; set; } = string.Empty;
        public object cosmos_result { get; set; } = new();
        public object task_result { get; set; } = new();
    }

    public class ClientEndpointResponse
    {
        public string status { get; set; } = string.Empty;
        public string message { get; set; } = string.Empty;
        public int processed_count { get; set; }
        public ClientResult[] results { get; set; } = Array.Empty<ClientResult>();
        public string timestamp { get; set; } = string.Empty;
    }

    public class ClientResult
    {
        public string name { get; set; } = string.Empty;
        public string status { get; set; } = string.Empty;
        public string id { get; set; } = string.Empty;
        public string message { get; set; } = string.Empty;
    }
}