using Microsoft.AspNetCore.SignalR;

namespace DocumentProcessorUI.Hubs;

public class DocumentProcessingHub : Hub<IDocumentProcessingHubClient>
{
    public async Task JoinProcessingGroup(string processingId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"processing_{processingId}");
    }

    public async Task LeaveProcessingGroup(string processingId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"processing_{processingId}");
    }
}

public interface IDocumentProcessingHubClient
{
    Task ReceiveProgressUpdate(string processingId, int percentage, string status, string message);
    Task ReceiveProcessingComplete(string processingId, object result);
    Task ReceiveProcessingError(string processingId, string error);
}