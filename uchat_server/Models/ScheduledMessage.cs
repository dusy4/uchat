namespace uchat_server.Models;

public class ScheduledMessage
{
    public int Id { get; set; }
    public int SenderId { get; set; }
    public string SenderUsername { get; set; } = string.Empty;
    public string? TargetUsername { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime ScheduledAt { get; set; }
    public bool IsPrivate { get; set; }
    public DateTime CreatedAt { get; set; }
}

