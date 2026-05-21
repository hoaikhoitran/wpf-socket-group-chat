namespace ChatClient;

public sealed class MessagePacket
{
    public string Type { get; set; } = "";
    public string Sender { get; set; } = "";
    public string Content { get; set; } = "";
    public string FileName { get; set; } = "";
    public long FileSize { get; set; }
    public string FileId { get; set; } = "";
    public string MessageId { get; set; } = "";
}
