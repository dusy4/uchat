using System;

namespace uchat_server.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    public string Bio { get; set; } = string.Empty;
    public string AvatarColor { get; set; } = "#0088CC";

    public string? AvatarData { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
}