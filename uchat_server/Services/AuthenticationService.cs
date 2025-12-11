using System.Security.Cryptography;
using System.Text;
using uchat_server.Database;
using uchat_server.Models;

namespace uchat_server.Services;

public class AuthenticationService
{
    private readonly DatabaseContext _db;

    public AuthenticationService(DatabaseContext db)
    {
        _db = db;
    }

    public string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(password);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    public bool VerifyPassword(string password, string hash)
    {
        var passwordHash = HashPassword(password);
        return passwordHash == hash;
    }

    public bool ChangePassword(User user, string oldPassword, string newPassword)
    {
        if (!VerifyPassword(oldPassword, user.PasswordHash))
        {
            return false;
        }

        var newHash = HashPassword(newPassword);

        var success = _db.UpdateUserPassword(user.Id, newHash);

        if (success)
        {
            user.PasswordHash = newHash;
        }

        return success;
    }

    public User? Authenticate(string username, string password)
    {
        var user = _db.GetUserByUsername(username);
        if (user == null) return null;
        
        if (VerifyPassword(password, user.PasswordHash))
        {
            return user;
        }
        
        return null;
    }

    public User? Register(string username, string password)
    {
        if (_db.GetUserByUsername(username) != null)
        {
            return null; 
        }

        var passwordHash = HashPassword(password);
        var userId = _db.CreateUser(username, passwordHash);
        
        return new User
        {
            Id = userId,
            Username = username,
            PasswordHash = passwordHash,
            CreatedAt = DateTime.UtcNow
        };
    }
}

