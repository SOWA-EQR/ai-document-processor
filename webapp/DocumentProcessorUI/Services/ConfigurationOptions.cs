namespace DocumentProcessorUI.Services;

public class AzureStorageOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string BronzeContainerName { get; set; } = "bronze";
    public string GoldContainerName { get; set; } = "gold";
}

public class CosmosDbOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = "DocumentProcessor";
    public string ContainerName { get; set; } = "ProcessedDocuments";
}

public class DocumentProcessingOptions
{
    public string FunctionAppBaseUrl { get; set; } = string.Empty;
    public string ClientEndpointUrl { get; set; } = string.Empty;
    public int PollingIntervalSeconds { get; set; } = 5;
    public int MaxWaitTimeMinutes { get; set; } = 10;
}