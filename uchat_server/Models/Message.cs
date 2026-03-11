namespace uchat_server.Models;

public class Message
{
    public int Id { get; set; }
    public int SenderId { get; set; }
    public string SenderUsername { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public bool IsDeleted { get; set; }
    public int? BlobId { get; set; }
    public int? ReplyToId { get; set; }
    public string? ReplyToSender { get; set; }
    public string? ReplyToContent { get; set; }
    public bool IsRead { get; set; }
}

