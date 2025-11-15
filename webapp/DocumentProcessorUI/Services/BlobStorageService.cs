using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using DocumentProcessorUI.Models;

namespace DocumentProcessorUI.Services;

public interface IBlobStorageService
{
    Task<string> UploadDocumentAsync(IFormFile file, string processingId);
    Task<string> UploadDocumentAsync(FileContentWrapper file, string processingId);
    Task<bool> CheckDocumentExistsAsync(string containerName, string blobName);
    Task<Stream> DownloadDocumentAsync(string containerName, string blobName);
    Task<BlobProperties> GetBlobPropertiesAsync(string containerName, string blobName);
}

public class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly AzureStorageOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BlobStorageService> _logger;
    private readonly string _localStoragePath;

    public BlobStorageService(IOptions<AzureStorageOptions> options, IConfiguration configuration, ILogger<BlobStorageService> logger)
    {
        _options = options.Value;
        _configuration = configuration;
        _blobServiceClient = new BlobServiceClient(_options.ConnectionString);
        _logger = logger;
        
        // Set up local storage path
        _localStoragePath = _configuration.GetValue<string>("LocalPersistence:UploadsPath") ?? "./Data/Uploads";
        Directory.CreateDirectory(_localStoragePath);
    }

    public async Task<string> UploadDocumentAsync(IFormFile file, string processingId)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_options.BronzeContainerName);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

            // Generate unique blob name with processing ID
            var fileExtension = Path.GetExtension(file.FileName);
            var blobName = $"{processingId}_{file.FileName.Replace(" ", "_")}";

            var blobClient = containerClient.GetBlobClient(blobName);

            // Set metadata for tracking
            var metadata = new Dictionary<string, string>
            {
                ["ProcessingId"] = processingId,
                ["OriginalFileName"] = file.FileName,
                ["UploadedAt"] = DateTime.UtcNow.ToString("O"),
                ["ContentType"] = file.ContentType
            };

            using var stream = file.OpenReadStream();
            await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = file.ContentType });
            await blobClient.SetMetadataAsync(metadata);

            _logger.LogInformation($"Document uploaded successfully: {blobName}");
            return blobName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error uploading document for processing ID: {processingId}");
            throw;
        }
    }

    public async Task<string> UploadDocumentAsync(FileContentWrapper file, string processingId)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_options.BronzeContainerName);
            await containerClient.CreateIfNotExistsAsync();

            // Generate unique blob name
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var fileName = Path.GetFileNameWithoutExtension(file.FileName);
            var extension = Path.GetExtension(file.FileName);
            var blobName = $"{fileName}_{timestamp}_{processingId}{extension}";

            var blobClient = containerClient.GetBlobClient(blobName);

            // Set metadata for tracking
            var metadata = new Dictionary<string, string>
            {
                ["ProcessingId"] = processingId,
                ["OriginalFileName"] = file.FileName,
                ["UploadedAt"] = DateTime.UtcNow.ToString("O"),
                ["ContentType"] = file.ContentType
            };

            using var stream = file.OpenReadStream();
            await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = file.ContentType });
            await blobClient.SetMetadataAsync(metadata);

            _logger.LogInformation($"Document uploaded successfully: {blobName}");
            return blobName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error uploading document for processing ID: {processingId}");
            throw;
        }
    }

    public async Task<bool> CheckDocumentExistsAsync(string containerName, string blobName)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);
            
            var response = await blobClient.ExistsAsync();
            return response.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error checking document existence: {containerName}/{blobName}");
            return false;
        }
    }

    public async Task<Stream> DownloadDocumentAsync(string containerName, string blobName)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);
            
            var response = await blobClient.DownloadStreamingAsync();
            return response.Value.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error downloading document: {containerName}/{blobName}");
            throw;
        }
    }

    public async Task<BlobProperties> GetBlobPropertiesAsync(string containerName, string blobName)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);
            
            var response = await blobClient.GetPropertiesAsync();
            return response.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting blob properties: {containerName}/{blobName}");
            throw;
        }
    }

    private async Task SaveFileLocallyAsync(IFormFile file, string blobName, string processingId)
    {
        try
        {
            var localFilePath = Path.Combine(_localStoragePath, blobName);
            using var fileStream = new FileStream(localFilePath, FileMode.Create);
            using var uploadStream = file.OpenReadStream();
            await uploadStream.CopyToAsync(fileStream);

            // Save metadata as well
            var metadataPath = Path.ChangeExtension(localFilePath, ".metadata.json");
            var metadata = new
            {
                ProcessingId = processingId,
                OriginalFileName = file.FileName,
                UploadedAt = DateTime.UtcNow.ToString("O"),
                ContentType = file.ContentType,
                FileSize = file.Length
            };
            await File.WriteAllTextAsync(metadataPath, System.Text.Json.JsonSerializer.Serialize(metadata));
            
            _logger.LogInformation($"File saved locally: {localFilePath}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to save file locally: {blobName}");
        }
    }

    private async Task SaveFileLocallyAsync(FileContentWrapper file, string blobName, string processingId)
    {
        try
        {
            var localFilePath = Path.Combine(_localStoragePath, blobName);
            await File.WriteAllBytesAsync(localFilePath, file.Content);

            // Save metadata as well
            var metadataPath = Path.ChangeExtension(localFilePath, ".metadata.json");
            var metadata = new
            {
                ProcessingId = processingId,
                OriginalFileName = file.FileName,
                UploadedAt = DateTime.UtcNow.ToString("O"),
                ContentType = file.ContentType,
                FileSize = file.Content.Length
            };
            await File.WriteAllTextAsync(metadataPath, System.Text.Json.JsonSerializer.Serialize(metadata));
            
            _logger.LogInformation($"File saved locally: {localFilePath}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to save file locally: {blobName}");
        }
    }
}