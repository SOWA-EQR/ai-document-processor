namespace DocumentProcessorUI.Models;

public class FileContentWrapper
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    
    public long Length => Content.Length;
    
    public Stream OpenReadStream()
    {
        return new MemoryStream(Content);
    }
}