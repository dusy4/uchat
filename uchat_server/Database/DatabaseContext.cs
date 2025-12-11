using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using uchat_server.Database;
using uchat_server.Models;

namespace uchat_server.Database;

public class DatabaseContext : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _connectionString;
   
    public DatabaseContext()
    {
        var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uchat.db");

        _connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate;Pooling=True;";

        _connection = new SqliteConnection(_connectionString);
        _connection.Open();

        using (var cmd = new SqliteCommand("PRAGMA journal_mode = WAL;", _connection))
        {
            cmd.ExecuteNonQuery();
        }

        using (var cmd = new SqliteCommand("PRAGMA auto_vacuum = FULL;", _connection))
        {
            cmd.ExecuteNonQuery();
        }

        using (var cmd = new SqliteCommand("PRAGMA synchronous = NORMAL;", _connection))
        {
            cmd.ExecuteNonQuery();
        }

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        var createUsersTable = @"
            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT UNIQUE NOT NULL,
                PasswordHash TEXT NOT NULL,
                Bio TEXT DEFAULT '',
                AvatarColor TEXT DEFAULT '#0088CC',
                AvatarData TEXT,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                IsDeleted INTEGER NOT NULL DEFAULT 0
            );";

        var createFileBlobsTable = @"
            CREATE TABLE IF NOT EXISTS FileBlobs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FileName TEXT,
                Data TEXT NOT NULL
            );";

        var createMessagesTable = @"
            CREATE TABLE IF NOT EXISTS Messages (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SenderId INTEGER NOT NULL,
                SenderUsername TEXT NOT NULL,
                Content TEXT NOT NULL,
                SentAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                EditedAt DATETIME,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                BlobId INTEGER, 
                ReplyToId INTEGER,
                FOREIGN KEY (SenderId) REFERENCES Users(Id)
            );";
        var createDirectMessagesTable = @"
            CREATE TABLE IF NOT EXISTS DirectMessages (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SenderId INTEGER NOT NULL,
                RecipientId INTEGER NOT NULL,
                Content TEXT NOT NULL,
                SentAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                EditedAt DATETIME,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                BlobId INTEGER,
                ReplyToId INTEGER,
                FOREIGN KEY (SenderId) REFERENCES Users(Id),
                FOREIGN KEY (RecipientId) REFERENCES Users(Id)
            );";

        var createScheduledMessagesTable = @"
            CREATE TABLE IF NOT EXISTS ScheduledMessages (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SenderId INTEGER NOT NULL,
                SenderUsername TEXT NOT NULL,
                TargetUsername TEXT,
                Content TEXT NOT NULL,
                ScheduledAt DATETIME NOT NULL,
                IsPrivate INTEGER NOT NULL DEFAULT 0,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (SenderId) REFERENCES Users(Id)
            );";

        var createGroupsTable = @"
            CREATE TABLE IF NOT EXISTS Groups (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                CreatorId INTEGER NOT NULL,
                AvatarData TEXT,  -- <--- ОСЬ ЦЬОГО НЕ БУЛО!
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (CreatorId) REFERENCES Users(Id)
            );";

        var createGroupMembersTable = @"
            CREATE TABLE IF NOT EXISTS GroupMembers (
                GroupId INTEGER,
                UserId INTEGER,
                PRIMARY KEY (GroupId, UserId),
                FOREIGN KEY(GroupId) REFERENCES Groups(Id),
                FOREIGN KEY(UserId) REFERENCES Users(Id)
            );";

        var createGroupMessagesTable = @"
            CREATE TABLE IF NOT EXISTS GroupMessages (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GroupId INTEGER NOT NULL,
                SenderId INTEGER NOT NULL,
                Content TEXT NOT NULL,
                SentAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                BlobId INTEGER,
                ReplyToId INTEGER,
                FOREIGN KEY(GroupId) REFERENCES Groups(Id),
                FOREIGN KEY(SenderId) REFERENCES Users(Id)
            );";

        var createChatSettingsTable = @"
    CREATE TABLE IF NOT EXISTS ChatSettings (
        UserId INTEGER,
        PartnerId INTEGER,
        LastClearedMessageId INTEGER DEFAULT 0,
        PRIMARY KEY (UserId, PartnerId),
        FOREIGN KEY(UserId) REFERENCES Users(Id),
        FOREIGN KEY(PartnerId) REFERENCES Users(Id)
    );";
        var createIndices = @"
            CREATE INDEX IF NOT EXISTS idx_messages_sentat ON Messages(SentAt);
            CREATE INDEX IF NOT EXISTS idx_direct_sender_recipient ON DirectMessages(SenderId, RecipientId);
            CREATE INDEX IF NOT EXISTS idx_direct_sentat ON DirectMessages(SentAt);
            CREATE INDEX IF NOT EXISTS idx_group_messages_groupid ON GroupMessages(GroupId);
            CREATE INDEX IF NOT EXISTS idx_group_messages_sentat ON GroupMessages(SentAt);
            CREATE INDEX IF NOT EXISTS idx_group_members_userid ON GroupMembers(UserId);
        ";
        using (var cmd = new SqliteCommand(createUsersTable, _connection)) cmd.ExecuteNonQuery();
        using (var cmd = new SqliteCommand(createFileBlobsTable, _connection)) cmd.ExecuteNonQuery();
        using (var cmd = new SqliteCommand(createMessagesTable, _connection)) cmd.ExecuteNonQuery();
        using (var cmd = new SqliteCommand(createDirectMessagesTable, _connection)) cmd.ExecuteNonQuery();
        using (var cmd = new SqliteCommand(createScheduledMessagesTable, _connection)) cmd.ExecuteNonQuery();
        using (var cmd = new SqliteCommand(createGroupsTable, _connection)) cmd.ExecuteNonQuery(); using (var cmd = new SqliteCommand(createGroupMembersTable, _connection)) cmd.ExecuteNonQuery();
        using (var cmd = new SqliteCommand(createGroupMessagesTable, _connection)) cmd.ExecuteNonQuery();
        using (var cmd = new SqliteCommand(createChatSettingsTable, _connection)) cmd.ExecuteNonQuery();
        using (var cmd = new SqliteCommand(createIndices, _connection)) { cmd.ExecuteNonQuery(); }
        var createIndexSender = @"
        CREATE INDEX IF NOT EXISTS idx_dm_sender_recipient 
        ON DirectMessages(SenderId, RecipientId);";

        var createIndexRecipient = @"
        CREATE INDEX IF NOT EXISTS idx_dm_recipient_sender 
        ON DirectMessages(RecipientId, SenderId);";

        var createIndexTime = @"
        CREATE INDEX IF NOT EXISTS idx_dm_sentat 
        ON DirectMessages(SentAt);";

        var createIndexReply = @"
        CREATE INDEX IF NOT EXISTS idx_dm_replyto 
        ON DirectMessages(ReplyToId);";

        using (var cmd = new SqliteCommand(createIndexSender, _connection)) cmd.ExecuteNonQuery();
        using (var cmd = new SqliteCommand(createIndexRecipient, _connection)) cmd.ExecuteNonQuery();
        using (var cmd = new SqliteCommand(createIndexTime, _connection)) cmd.ExecuteNonQuery();
        using (var cmd = new SqliteCommand(createIndexReply, _connection)) cmd.ExecuteNonQuery();

        using (var cmd = new SqliteCommand("ANALYZE;", _connection))
        {
            cmd.ExecuteNonQuery();
        }

        try
        {
            using (var cmd = new SqliteCommand("ALTER TABLE Users ADD COLUMN DisplayName TEXT;", _connection))
            {
                cmd.ExecuteNonQuery();
            }
            using (var cmd = new SqliteCommand("UPDATE Users SET DisplayName = Username WHERE DisplayName IS NULL;", _connection))
            {
                cmd.ExecuteNonQuery();
            }
        }
        catch { }
        try
        {
            using (var cmd = new SqliteCommand("ALTER TABLE DirectMessages ADD COLUMN IsRead INTEGER NOT NULL DEFAULT 0;", _connection))
                cmd.ExecuteNonQuery();
        } catch { }

        try
        {
            using (var cmd = new SqliteCommand("ALTER TABLE Messages ADD COLUMN IsRead INTEGER NOT NULL DEFAULT 0;", _connection))
                cmd.ExecuteNonQuery();
        }
        catch { }

        try
        {
            using (var cmd = new SqliteCommand("ALTER TABLE GroupMessages ADD COLUMN IsRead INTEGER NOT NULL DEFAULT 0;", _connection))
                cmd.ExecuteNonQuery();
        }
        catch { }
    }

    public bool UpdateUserProfile(int userId, string newDisplayName, string newBio, string newColor, string? avatarBase64)
    {
        var query = "UPDATE Users SET DisplayName = @name, Bio = @bio, AvatarColor = @color, AvatarData = @avatar WHERE Id = @id";
        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@name", newDisplayName);
        command.Parameters.AddWithValue("@bio", newBio);
        command.Parameters.AddWithValue("@color", newColor);
        command.Parameters.AddWithValue("@avatar", avatarBase64 ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@id", userId);

        return command.ExecuteNonQuery() > 0;
    }

    public List<User> GetAllUsers()
    {
        var users = new List<User>();
        var query = @"
        SELECT Id, Username, CreatedAt, Bio, AvatarColor, AvatarData, DisplayName 
        FROM Users 
        WHERE IsDeleted = 0
        ORDER BY Username ASC 
        LIMIT 100;";

        using var command = new SqliteCommand(query, _connection);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var u = new User
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                CreatedAt = reader.GetDateTime(2),
                Bio = reader.IsDBNull(3) ? "" : reader.GetString(3),
                AvatarColor = reader.IsDBNull(4) ? "#0088CC" : reader.GetString(4),
                AvatarData = reader.IsDBNull(5) ? null : reader.GetString(5)
            };
            u.DisplayName = !reader.IsDBNull(6) ? reader.GetString(6) : u.Username;
            users.Add(u);
        }
        return users;
    }

    public User? GetUserByUsername(string username)
    {
        var query = "SELECT Id, Username, PasswordHash, CreatedAt, Bio, AvatarColor, AvatarData, DisplayName FROM Users WHERE Username = @username;";
        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@username", username);

        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            var u = new User
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                PasswordHash = reader.GetString(2),
                CreatedAt = reader.GetDateTime(3),
                Bio = reader.IsDBNull(4) ? "" : reader.GetString(4),
                AvatarColor = reader.IsDBNull(5) ? "#0088CC" : reader.GetString(5),
                AvatarData = reader.IsDBNull(6) ? null : reader.GetString(6)
            };
            u.DisplayName = !reader.IsDBNull(7) ? reader.GetString(7) : u.Username;
            return u;
        }
        return null;
    }

    public int CreateUser(string username, string passwordHash)
    {
        var query = "INSERT INTO Users (Username, DisplayName, PasswordHash, CreatedAt, Bio, AvatarColor) VALUES (@username, @username, @passwordHash, @createdAt, '', '#0088CC') RETURNING Id;";
        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@username", username);
        command.Parameters.AddWithValue("@passwordHash", passwordHash);
        command.Parameters.AddWithValue("@createdAt", DateTime.UtcNow);
        var result = command.ExecuteScalar();
        return Convert.ToInt32(result);
    }

    public int CreateMessage(int senderId, string senderUsername, string content, int? blobId = null, int? replyToId = null)
    {   
        var query = "INSERT INTO Messages (SenderId, SenderUsername, Content, SentAt, BlobId, ReplyToId) VALUES (@senderId, @senderUsername, @content, @sentAt, @blobId, @replyToId) RETURNING Id;";

        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@senderId", senderId);
        command.Parameters.AddWithValue("@senderUsername", senderUsername);
        command.Parameters.AddWithValue("@content", content);
        command.Parameters.AddWithValue("@sentAt", DateTime.UtcNow);
        command.Parameters.AddWithValue("@blobId", blobId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@replyToId", replyToId ?? (object)DBNull.Value);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public List<Message> GetMessages(int limit = 100, int? beforeId = null)
    {
        var messages = new List<Message>();
        var query = @"
        SELECT 
            m.Id, m.SenderId, m.SenderUsername, m.Content, m.SentAt, m.EditedAt, m.IsDeleted, m.BlobId, 
            m.ReplyToId, 
            m2.SenderUsername AS ReplySender, 
            m2.Content AS ReplyContent,
            m.IsRead
        FROM Messages m
        LEFT JOIN Messages m2 ON m.ReplyToId = m2.Id
        WHERE m.IsDeleted = 0 
        AND (@bid IS NULL OR m.Id < @bid)
        ORDER BY m.SentAt DESC 
        LIMIT @limit;";
        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@limit", limit);
        command.Parameters.AddWithValue("@bid", beforeId ?? (object)DBNull.Value);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var msg = new Message
            {
                Id = reader.GetInt32(0),
                SenderId = reader.GetInt32(1),
                SenderUsername = reader.GetString(2),
                Content = reader.GetString(3),
                SentAt = reader.GetDateTime(4).ToLocalTime(),
                EditedAt = reader.IsDBNull(5) ? null : reader.GetDateTime(5).ToLocalTime(),
                IsDeleted = reader.GetInt32(6) != 0,
                BlobId = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                ReplyToId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                IsRead = reader.GetInt32(11) != 0
            };
            if (msg.ReplyToId != null)
            {
                msg.ReplyToSender = reader.IsDBNull(9) ? "Deleted" : reader.GetString(9);
                msg.ReplyToContent = reader.IsDBNull(10) ? "Message deleted" : reader.GetString(10);
            }

            messages.Add(msg);
        }
        return messages.OrderBy(m => m.SentAt).ToList();
    }

    public bool UpdateMessage(int messageId, string newContent)
    {
        var query = "UPDATE Messages SET Content = @content, EditedAt = @editedAt WHERE Id = @messageId;";
        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@content", newContent);
        command.Parameters.AddWithValue("@editedAt", DateTime.UtcNow);
        command.Parameters.AddWithValue("@messageId", messageId);
        return command.ExecuteNonQuery() > 0;
    }

    public Message? GetMessageById(int messageId)
    {
        var query = "SELECT Id, SenderId, SenderUsername, Content, SentAt, EditedAt, IsDeleted FROM Messages WHERE Id = @messageId;";
        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@messageId", messageId);
        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return new Message
            {
                Id = reader.GetInt32(0),
                SenderId = reader.GetInt32(1),
                SenderUsername = reader.GetString(2),
                Content = reader.GetString(3),
                SentAt = reader.GetDateTime(4).ToLocalTime(),
                EditedAt = reader.IsDBNull(5) ? null : reader.GetDateTime(5).ToLocalTime(),
                IsDeleted = reader.GetInt32(6) != 0
            };
        }
        return null;
    }
    public bool UpdateUserPassword(int userId, string newPasswordHash)
    {
        var query = "UPDATE Users SET PasswordHash = @hash WHERE Id = @id";
        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@hash", newPasswordHash);
        command.Parameters.AddWithValue("@id", userId);
        return command.ExecuteNonQuery() > 0;
    }

    public bool DeleteMessage(int messageId)
    {
        try
        {
            var query = "DELETE FROM Messages WHERE Id = @id;";
            using (var command = new SqliteCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@id", messageId);
                int rows = command.ExecuteNonQuery();
                if (rows > 0)
                {
                    try
                    {
                        var cleanBlobs = "DELETE FROM FileBlobs WHERE Id NOT IN (SELECT BlobId FROM Messages WHERE BlobId IS NOT NULL) AND Id NOT IN (SELECT BlobId FROM DirectMessages WHERE BlobId IS NOT NULL);";
                        using (var blobCmd = new SqliteCommand(cleanBlobs, _connection)) blobCmd.ExecuteNonQuery();
                    }
                    catch { }
                    using (var vacuumCmd = new SqliteCommand("VACUUM;", _connection))
                    {
                        vacuumCmd.ExecuteNonQuery();
                    }
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB ERROR] Deletion error: {ex.Message}");
            return false;
        }
    }

    public int? GetPrivateMessageRecipientId(int messageId)
    {
        var query = "SELECT RecipientId FROM DirectMessages WHERE Id = @id";
        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@id", messageId);
        var result = command.ExecuteScalar();
        return result != null ? Convert.ToInt32(result) : null;
    }

    public int? GetPrivateMessageSenderId(int messageId)
    {
        var query = "SELECT SenderId FROM DirectMessages WHERE Id = @id";
        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@id", messageId);
        var result = command.ExecuteScalar();
        return result != null ? Convert.ToInt32(result) : null;
    }

    public string? GetUsernameById(int userId)
    {
        var query = "SELECT Username FROM Users WHERE Id = @id";
        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@id", userId);
        var result = command.ExecuteScalar();
        return result?.ToString();
    }

    public bool UpdatePrivateMessage(int messageId, string newContent)
    {
        try
        {
            var updateQuery = "UPDATE DirectMessages SET Content = @content, EditedAt = @editedAt WHERE Id = @messageId;";

            using (var updateCmd = new SqliteCommand(updateQuery, _connection))
            {
                updateCmd.Parameters.AddWithValue("@content", newContent);
                updateCmd.Parameters.AddWithValue("@editedAt", DateTime.UtcNow);
                updateCmd.Parameters.AddWithValue("@messageId", messageId);

                return updateCmd.ExecuteNonQuery() > 0;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating private message: {ex.Message}");
            return false;
        }
    }

    public bool DeletePrivateMessage(int messageId)
    {
        try
        {
            var query = "DELETE FROM DirectMessages WHERE Id = @id;";

            using (var command = new SqliteCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@id", messageId);
                int rows = command.ExecuteNonQuery();

                if (rows > 0)
                {
                    try
                    {
                        var cleanBlobs = "DELETE FROM FileBlobs WHERE Id NOT IN (SELECT BlobId FROM Messages WHERE BlobId IS NOT NULL) AND Id NOT IN (SELECT BlobId FROM DirectMessages WHERE BlobId IS NOT NULL);";
                        using (var blobCmd = new SqliteCommand(cleanBlobs, _connection)) blobCmd.ExecuteNonQuery();
                    }
                    catch { }
                    using (var vacuumCmd = new SqliteCommand("VACUUM;", _connection))
                    {
                        vacuumCmd.ExecuteNonQuery();
                    }
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB ERROR] DM deletion error: {ex.Message}");
            return false;
        }
    }
    public int CreatePrivateMessage(int senderId, int recipientId, string content, int? blobId = null, int? replyToId = null)
    {
        var query = "INSERT INTO DirectMessages (SenderId, RecipientId, Content, SentAt, BlobId, ReplyToId) VALUES (@senderId, @recipientId, @content, @sentAt, @blobId, @replyToId) RETURNING Id;";
        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@senderId", senderId);
        command.Parameters.AddWithValue("@recipientId", recipientId);
        command.Parameters.AddWithValue("@content", content);
        command.Parameters.AddWithValue("@sentAt", DateTime.UtcNow);
        command.Parameters.AddWithValue("@blobId", blobId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@replyToId", replyToId ?? (object)DBNull.Value);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public List<Message> GetPrivateMessages(int userId1, int userId2, int limit = 100, int? beforeId = null)
    {
        int minId = GetLastClearedMessageId(userId1, userId2); 

        var messages = new List<Message>();
        var query = @"
    SELECT * FROM (
        SELECT 
            m.Id, m.SenderId, m.Content, m.SentAt, m.BlobId, m.ReplyToId, m.EditedAt,
            u.Username AS ReplySender, m2.Content AS ReplyContent,
            m.IsRead
        FROM DirectMessages m
        LEFT JOIN DirectMessages m2 ON m.ReplyToId = m2.Id
        LEFT JOIN Users u ON m2.SenderId = u.Id
        WHERE m.SenderId = @u1 AND m.RecipientId = @u2 AND m.IsDeleted = 0
        AND m.Id > @minId  -- <--- ФІЛЬТР (Тільки новіші за горизонт)
        AND (@bid IS NULL OR m.Id < @bid)
        ORDER BY m.SentAt DESC LIMIT @limit
    )
    UNION ALL
    SELECT * FROM (
        SELECT 
            m.Id, m.SenderId, m.Content, m.SentAt, m.BlobId, m.ReplyToId, m.EditedAt,
            u.Username AS ReplySender, m2.Content AS ReplyContent,
            m.IsRead
        FROM DirectMessages m
        LEFT JOIN DirectMessages m2 ON m.ReplyToId = m2.Id
        LEFT JOIN Users u ON m2.SenderId = u.Id
        WHERE m.SenderId = @u2 AND m.RecipientId = @u1 AND m.IsDeleted = 0
        AND m.Id > @minId -- <--- ФІЛЬТР
        AND (@bid IS NULL OR m.Id < @bid)
        ORDER BY m.SentAt DESC LIMIT @limit
    )
    ORDER BY SentAt DESC LIMIT @limit;";

        try
        {
            using var command = new SqliteCommand(query, _connection);
            command.Parameters.AddWithValue("@u1", userId1);
            command.Parameters.AddWithValue("@u2", userId2);
            command.Parameters.AddWithValue("@limit", limit);
            command.Parameters.AddWithValue("@bid", beforeId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@minId", minId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var msg = new Message
                {
                    Id = reader.GetInt32(0),
                    SenderId = reader.GetInt32(1),
                    Content = reader.GetString(2),
                    SentAt = reader.GetDateTime(3).ToLocalTime(),
                    BlobId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    ReplyToId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    EditedAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6).ToLocalTime(),
                    IsRead = reader.GetInt32(9) != 0
                };
                if (msg.ReplyToId != null)
                {
                    msg.ReplyToSender = reader.IsDBNull(7) ? "User" : reader.GetString(7);
                    msg.ReplyToContent = reader.IsDBNull(8) ? "Deleted" : reader.GetString(8);
                }
                messages.Add(msg);
            }
        }
        catch (Exception ex) { Console.WriteLine($"DB Error: {ex.Message}"); }
        return messages.OrderBy(m => m.SentAt).ToList();
    }

    public int CreateScheduledMessage(int senderId, string senderUsername, string? targetUsername, string content, DateTime scheduledAt, bool isPrivate)
    {
        var query = "INSERT INTO ScheduledMessages (SenderId, SenderUsername, TargetUsername, Content, ScheduledAt, IsPrivate) VALUES (@senderId, @senderUsername, @targetUsername, @content, @scheduledAt, @isPrivate); SELECT last_insert_rowid();";
        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@senderId", senderId);
        command.Parameters.AddWithValue("@senderUsername", senderUsername);
        command.Parameters.AddWithValue("@targetUsername", targetUsername ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@content", content);
        command.Parameters.AddWithValue("@scheduledAt", scheduledAt.ToString("O"));
        command.Parameters.AddWithValue("@isPrivate", isPrivate ? 1 : 0);
        var result = command.ExecuteScalar();
        return Convert.ToInt32(result);
    }

    public List<ScheduledMessage> GetScheduledMessages(int senderId, string? targetUsername = null)
    {
        var messages = new List<ScheduledMessage>();
        string query;
        SqliteCommand command;

        if (targetUsername != null)
        {
            query = "SELECT Id, SenderId, SenderUsername, TargetUsername, Content, ScheduledAt, IsPrivate, CreatedAt FROM ScheduledMessages WHERE SenderId = @senderId AND TargetUsername = @targetUsername ORDER BY ScheduledAt ASC;";
            command = new SqliteCommand(query, _connection);
            command.Parameters.AddWithValue("@senderId", senderId);
            command.Parameters.AddWithValue("@targetUsername", targetUsername);
        }
        else
        {
            query = "SELECT Id, SenderId, SenderUsername, TargetUsername, Content, ScheduledAt, IsPrivate, CreatedAt FROM ScheduledMessages WHERE SenderId = @senderId AND IsPrivate = 0 ORDER BY ScheduledAt ASC;";
            command = new SqliteCommand(query, _connection);
            command.Parameters.AddWithValue("@senderId", senderId);
        }

        using (command)
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                messages.Add(new ScheduledMessage
                {
                    Id = reader.GetInt32(0),
                    SenderId = reader.GetInt32(1),
                    SenderUsername = reader.GetString(2),
                    TargetUsername = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Content = reader.GetString(4),
                    ScheduledAt = DateTime.Parse(reader.GetString(5)),
                    IsPrivate = reader.GetInt32(6) == 1,
                    CreatedAt = reader.GetDateTime(7)
                });
            }
        }
        return messages;
    }

    public List<ScheduledMessage> GetDueScheduledMessages()
    {
        var messages = new List<ScheduledMessage>();
        var now = DateTime.UtcNow;
        var query = "SELECT Id, SenderId, SenderUsername, TargetUsername, Content, ScheduledAt, IsPrivate, CreatedAt FROM ScheduledMessages WHERE ScheduledAt <= @now ORDER BY ScheduledAt ASC;";
        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@now", now.ToString("O"));
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            messages.Add(new ScheduledMessage
            {
                Id = reader.GetInt32(0),
                SenderId = reader.GetInt32(1),
                SenderUsername = reader.GetString(2),
                TargetUsername = reader.IsDBNull(3) ? null : reader.GetString(3),
                Content = reader.GetString(4),
                ScheduledAt = DateTime.Parse(reader.GetString(5)),
                IsPrivate = reader.GetInt32(6) == 1,
                CreatedAt = reader.GetDateTime(7)
            });
        }
        return messages;
    }

    public bool UpdateScheduledMessage(int messageId, string content, DateTime scheduledAt)
    {
        var query = "UPDATE ScheduledMessages SET Content = @content, ScheduledAt = @scheduledAt WHERE Id = @id;";
        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@content", content);
        command.Parameters.AddWithValue("@scheduledAt", scheduledAt.ToString("O"));
        command.Parameters.AddWithValue("@id", messageId);
        return command.ExecuteNonQuery() > 0;
    }

    public bool DeleteScheduledMessage(int messageId)
    {
        var query = "DELETE FROM ScheduledMessages WHERE Id = @id;";
        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@id", messageId);
        return command.ExecuteNonQuery() > 0;
    }

    public ScheduledMessage? GetScheduledMessageById(int messageId)
    {
        var query = "SELECT Id, SenderId, SenderUsername, TargetUsername, Content, ScheduledAt, IsPrivate, CreatedAt FROM ScheduledMessages WHERE Id = @id;";
        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@id", messageId);
        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return new ScheduledMessage
            {
                Id = reader.GetInt32(0),
                SenderId = reader.GetInt32(1),
                SenderUsername = reader.GetString(2),
                TargetUsername = reader.IsDBNull(3) ? null : reader.GetString(3),
                Content = reader.GetString(4),
                ScheduledAt = DateTime.Parse(reader.GetString(5)),
                IsPrivate = reader.GetInt32(6) == 1,
                CreatedAt = reader.GetDateTime(7)
            };
        }
        return null;
    }

    public string? SoftDeleteUser(int userId)
    {
        using var transaction = _connection.BeginTransaction();
        try
        {
            string deletedUsername = $"deleted_{Guid.NewGuid().ToString().Substring(0, 8)}";
            var updateQuery = @"
            UPDATE Users 
            SET Username = @delName, DisplayName = 'Deleted Account', PasswordHash = '', Bio = '', AvatarData = NULL, AvatarColor = '#808080', IsDeleted = 1
            WHERE Id = @uid";

            using (var cmd = new SqliteCommand(updateQuery, _connection, transaction))
            {
                cmd.Parameters.AddWithValue("@delName", deletedUsername);
                cmd.Parameters.AddWithValue("@uid", userId);
                cmd.ExecuteNonQuery();
            }
            var partners = new List<int>();
            var findPartnersQuery = @"
            SELECT DISTINCT CASE WHEN SenderId = @uid THEN RecipientId ELSE SenderId END
            FROM DirectMessages 
            WHERE SenderId = @uid OR RecipientId = @uid";

            using (var cmd = new SqliteCommand(findPartnersQuery, _connection, transaction))
            {
                cmd.Parameters.AddWithValue("@uid", userId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) partners.Add(reader.GetInt32(0));
            }
            foreach (var partnerId in partners)
            {
                var setHorizonQuery = @"
                INSERT INTO ChatSettings (UserId, PartnerId, LastClearedMessageId)
                VALUES (@uid, @pid, 2147483647) -- int.MaxValue
                ON CONFLICT(UserId, PartnerId) 
                DO UPDATE SET LastClearedMessageId = 2147483647;";

                using (var cmd = new SqliteCommand(setHorizonQuery, _connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@uid", userId);
                    cmd.Parameters.AddWithValue("@pid", partnerId);
                    cmd.ExecuteNonQuery();
                }
            }
            using (var cmd = new SqliteCommand("DELETE FROM GroupMembers WHERE UserId = @uid", _connection, transaction))
            {
                cmd.Parameters.AddWithValue("@uid", userId);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = new SqliteCommand("DELETE FROM ScheduledMessages WHERE SenderId = @uid", _connection, transaction))
            {
                cmd.Parameters.AddWithValue("@uid", userId);
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
            foreach (var partnerId in partners)
            {
                TryCleanupGarbageMessages(userId, partnerId);
            }

            return deletedUsername;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error soft deleting user: {ex.Message}");
            transaction.Rollback();
            return null;
        }
    }

    public int SaveFileBlob(string base64Data)
    {
        var query = "INSERT INTO FileBlobs (Data) VALUES (@data) RETURNING Id;";
        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@data", base64Data);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public string? GetFileBlob(int blobId)
    {
        var query = "SELECT Data FROM FileBlobs WHERE Id = @id;";
        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@id", blobId);
        var result = command.ExecuteScalar();
        return result?.ToString();
    }

    public int CreateGroup(string name, int creatorId, List<int> memberIds)
    {
        using var transaction = _connection.BeginTransaction();
        try
        {
            var cmdGroup = new SqliteCommand("INSERT INTO Groups (Name, CreatorId) VALUES (@name, @creatorId) RETURNING Id;", _connection, transaction);
            cmdGroup.Parameters.AddWithValue("@name", name);
            cmdGroup.Parameters.AddWithValue("@creatorId", creatorId);
            var result = cmdGroup.ExecuteScalar();
            if (result == null) throw new Exception("Failed to create group");
            int groupId = Convert.ToInt32(result);
            if (!memberIds.Contains(creatorId)) memberIds.Add(creatorId);

            foreach (var userId in memberIds)
            {
                var cmdMember = new SqliteCommand("INSERT INTO GroupMembers (GroupId, UserId) VALUES (@gid, @uid);", _connection, transaction);
                cmdMember.Parameters.AddWithValue("@gid", groupId);
                cmdMember.Parameters.AddWithValue("@uid", userId);
                cmdMember.ExecuteNonQuery();
            }

            transaction.Commit();
            return groupId;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating group: {ex.Message}");
            transaction.Rollback();
            return -1;
        }
    }

    public List<GroupModel> GetUserGroups(int userId)
    {
        var groups = new List<GroupModel>();
        var query = @"
            SELECT g.Id, g.Name, g.CreatorId 
            FROM Groups g 
            JOIN GroupMembers gm ON g.Id = gm.GroupId 
            WHERE gm.UserId = @uid";

        using var cmd = new SqliteCommand(query, _connection);
        cmd.Parameters.AddWithValue("@uid", userId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            groups.Add(new GroupModel
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                CreatorId = reader.GetInt32(2)
            });
        }
        return groups;
    }

    public int CreateGroupMessage(int groupId, int senderId, string content, int? blobId = null, int? replyToId = null)
    {
        var query = "INSERT INTO GroupMessages (GroupId, SenderId, Content, SentAt, BlobId, ReplyToId) VALUES (@gid, @sid, @content, @time, @blobId, @replyToId) RETURNING Id;";
        using var cmd = new SqliteCommand(query, _connection);
        cmd.Parameters.AddWithValue("@gid", groupId);
        cmd.Parameters.AddWithValue("@sid", senderId);
        cmd.Parameters.AddWithValue("@content", content);
        cmd.Parameters.AddWithValue("@time", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("@blobId", blobId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@replyToId", replyToId ?? (object)DBNull.Value);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public List<int> GetGroupMemberIds(int groupId)
    {
        var ids = new List<int>();
        var query = "SELECT UserId FROM GroupMembers WHERE GroupId = @gid";

        using var cmd = new SqliteCommand(query, _connection);
        cmd.Parameters.AddWithValue("@gid", groupId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(reader.GetInt32(0));
        }
        return ids;
    }

    public List<Message> GetGroupMessages(int groupId, int limit = 20, int? beforeId = null)
    {
        var messages = new List<Message>();
        var query = @"
        SELECT 
            m.Id, m.SenderId, u.Username, m.Content, m.SentAt, m.BlobId, 
            m.ReplyToId,                  -- Index 6
            u2.Username AS ReplySender,   -- Index 7
            m2.Content AS ReplyContent,    -- Index 8
            m.IsRead
        FROM GroupMessages m
        JOIN Users u ON m.SenderId = u.Id
        LEFT JOIN GroupMessages m2 ON m.ReplyToId = m2.Id
        LEFT JOIN Users u2 ON m2.SenderId = u2.Id
        WHERE m.GroupId = @gid 
        AND (@bid IS NULL OR m.Id < @bid)
        ORDER BY m.SentAt DESC 
        LIMIT @limit;";
        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@gid", groupId);
        command.Parameters.AddWithValue("@limit", limit);
        command.Parameters.AddWithValue("@bid", beforeId ?? (object)DBNull.Value);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var msg = new Message
            {
                Id = reader.GetInt32(0),
                SenderId = reader.GetInt32(1),
                SenderUsername = reader.GetString(2),
                Content = reader.GetString(3),
                SentAt = reader.GetDateTime(4).ToLocalTime(),
                BlobId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                ReplyToId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                IsRead = reader.GetInt32(9) != 0
            };
            if (msg.ReplyToId != null)
            {
                msg.ReplyToSender = reader.IsDBNull(7) ? "User" : reader.GetString(7);
                msg.ReplyToContent = reader.IsDBNull(8) ? "Deleted message" : reader.GetString(8);
            }

            messages.Add(msg);
        }
        return messages.OrderBy(m => m.SentAt).ToList();
    }

    public GroupDetailsModel? GetGroupDetails(int groupId)
    {

        var query = "SELECT Id, Name, CreatorId, AvatarData FROM Groups WHERE Id = @id";
        var group = new GroupDetailsModel();

        using (var cmd = new SqliteCommand(query, _connection))
        {
            cmd.Parameters.AddWithValue("@id", groupId);
            using (var reader = cmd.ExecuteReader())
            {
                if (!reader.Read()) return null;

                group.Id = reader.GetInt32(0);
                group.Name = reader.GetString(1);
                group.CreatorId = reader.GetInt32(2);
                group.AvatarData = reader.IsDBNull(3) ? null : reader.GetString(3);
            }
        }
        var membersQuery = @"
        SELECT u.Id, u.Username, u.AvatarColor, u.AvatarData 
        FROM GroupMembers gm
        JOIN Users u ON gm.UserId = u.Id
        WHERE gm.GroupId = @gid";

        using (var cmd = new SqliteCommand(membersQuery, _connection))
        {
            cmd.Parameters.AddWithValue("@gid", groupId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                group.Members.Add(new UserItemModel
                {
                    Id = reader.GetInt32(0),
                    Username = reader.GetString(1),
                    AvatarColor = reader.IsDBNull(2) ? "#0088CC" : reader.GetString(2),
                    AvatarData = reader.IsDBNull(3) ? null : reader.GetString(3)
                });
            }
        }
        return group;
    }

    public bool AddMemberToGroup(int groupId, string username)
    {
        var user = GetUserByUsername(username);
        if (user == null) return false;
        var checkQuery = "SELECT COUNT(*) FROM GroupMembers WHERE GroupId = @gid AND UserId = @uid";
        using (var checkCmd = new SqliteCommand(checkQuery, _connection))
        {
            checkCmd.Parameters.AddWithValue("@gid", groupId);
            checkCmd.Parameters.AddWithValue("@uid", user.Id);
            if (Convert.ToInt32(checkCmd.ExecuteScalar()) > 0) return false; 
        }
        var query = "INSERT INTO GroupMembers (GroupId, UserId) VALUES (@gid, @uid)";
        using var cmd = new SqliteCommand(query, _connection);
        cmd.Parameters.AddWithValue("@gid", groupId);
        cmd.Parameters.AddWithValue("@uid", user.Id);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool RemoveMemberFromGroup(int groupId, int userId)
    {
        var query = "DELETE FROM GroupMembers WHERE GroupId = @gid AND UserId = @uid";
        using var cmd = new SqliteCommand(query, _connection);
        cmd.Parameters.AddWithValue("@gid", groupId);
        cmd.Parameters.AddWithValue("@uid", userId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool DeleteGroup(int groupId)
    {
        using var transaction = _connection.BeginTransaction();
        try
        {
            using (var cmd = new SqliteCommand("DELETE FROM GroupMembers WHERE GroupId = @gid", _connection, transaction))
            {
                cmd.Parameters.AddWithValue("@gid", groupId);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = new SqliteCommand("DELETE FROM GroupMessages WHERE GroupId = @gid", _connection, transaction))
            {
                cmd.Parameters.AddWithValue("@gid", groupId);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = new SqliteCommand("DELETE FROM Groups WHERE Id = @gid", _connection, transaction))
            {
                cmd.Parameters.AddWithValue("@gid", groupId);
                int rows = cmd.ExecuteNonQuery();
                if (rows == 0) { transaction.Rollback(); return false; }
            }
            var cleanBlobsQuery = @"
            DELETE FROM FileBlobs 
            WHERE Id NOT IN (SELECT BlobId FROM Messages WHERE BlobId IS NOT NULL) 
            AND Id NOT IN (SELECT BlobId FROM DirectMessages WHERE BlobId IS NOT NULL)
            AND Id NOT IN (SELECT BlobId FROM GroupMessages WHERE BlobId IS NOT NULL);";

            using (var gcCmd = new SqliteCommand(cleanBlobsQuery, _connection, transaction))
            {
                gcCmd.ExecuteNonQuery();
            }

            transaction.Commit();
            try
            {
                using var vacuumCmd = new SqliteCommand("VACUUM;", _connection);
                vacuumCmd.ExecuteNonQuery();
            }
            catch { }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DeleteGroup Error: {ex.Message}");
            transaction.Rollback();
            return false;
        }
    }

    public bool DeleteGroupMessage(int messageId, int userId)
    {
        try
        {
            var checkQuery = "SELECT SenderId FROM GroupMessages WHERE Id = @id";
            using (var checkCmd = new SqliteCommand(checkQuery, _connection))
            {
                checkCmd.Parameters.AddWithValue("@id", messageId);
                var result = checkCmd.ExecuteScalar();
                if (result == null) return false; 
                if (Convert.ToInt32(result) != userId) return false; 
            }
            var query = "DELETE FROM GroupMessages WHERE Id = @id";
            using (var command = new SqliteCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@id", messageId);
                int rows = command.ExecuteNonQuery();
                if (rows > 0)
                {
                    var cleanBlobs = @"
                    DELETE FROM FileBlobs 
                    WHERE Id NOT IN (SELECT BlobId FROM Messages WHERE BlobId IS NOT NULL) 
                    AND Id NOT IN (SELECT BlobId FROM DirectMessages WHERE BlobId IS NOT NULL)
                    AND Id NOT IN (SELECT BlobId FROM GroupMessages WHERE BlobId IS NOT NULL);";

                    try
                    {
                        using (var blobCmd = new SqliteCommand(cleanBlobs, _connection)) blobCmd.ExecuteNonQuery();
                    }
                    catch { }

                    return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DeleteGroupMessage Error: {ex.Message}");
            return false;
        }
    }

    public bool UpdateGroupMessage(int messageId, int userId, string newContent)
    {
        try
        {
            var checkQuery = "SELECT SenderId FROM GroupMessages WHERE Id = @id";
            using (var checkCmd = new SqliteCommand(checkQuery, _connection))
            {
                checkCmd.Parameters.AddWithValue("@id", messageId);
                var result = checkCmd.ExecuteScalar();
                if (result == null) return false;
                if (Convert.ToInt32(result) != userId) return false; 
            }
            var query = "UPDATE GroupMessages SET Content = @content WHERE Id = @id";
            using (var command = new SqliteCommand(query, _connection))
            {
                command.Parameters.AddWithValue("@content", newContent);
                command.Parameters.AddWithValue("@id", messageId);
                return command.ExecuteNonQuery() > 0;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"UpdateGroupMessage Error: {ex.Message}");
            return false;
        }
    }

    public List<int> GetGroupsCreatedBy(int creatorId)
    {
        var ids = new List<int>();
        var query = "SELECT Id FROM Groups WHERE CreatorId = @uid";
        using var cmd = new SqliteCommand(query, _connection);
        cmd.Parameters.AddWithValue("@uid", creatorId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(reader.GetInt32(0));
        }
        return ids;
    }
    public int GetGroupCreatorId(int groupId)
    {
        try
        {
            using var cmd = new SqliteCommand("SELECT CreatorId FROM Groups WHERE Id = @id", _connection);
            cmd.Parameters.AddWithValue("@id", groupId);
            var result = cmd.ExecuteScalar();

            if (result != null && result != DBNull.Value)
            {
                return Convert.ToInt32(result);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DB Error GetGroupCreatorId: {ex.Message}");
        }
        return 0;
    }
    public bool UpdateGroupProfile(int groupId, string newName, string? newAvatar)
    {
        try
        {
            var query = "UPDATE Groups SET Name = @name, AvatarData = @avatar WHERE Id = @id";

            using (var cmd = new SqliteCommand(query, _connection))
            {
                cmd.Parameters.AddWithValue("@name", newName);
                cmd.Parameters.AddWithValue("@avatar", (object?)newAvatar ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@id", groupId);

                return cmd.ExecuteNonQuery() > 0;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating group: {ex.Message}");
            return false;
        }
    }
    public List<GroupDetailsModel> GetGroupsForUser(int userId)
    {
        var groups = new List<GroupDetailsModel>();

        var query = @"
            SELECT g.Id, g.Name, g.CreatorId, g.AvatarData
            FROM Groups g
            JOIN GroupMembers gm ON g.Id = gm.GroupId
            WHERE gm.UserId = @uid
            ORDER BY (SELECT MAX(SentAt) FROM GroupMessages WHERE GroupId = g.Id) DESC";

        try
        {
            using (var cmd = new SqliteCommand(query, _connection))
            {
                cmd.Parameters.AddWithValue("@uid", userId);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        groups.Add(new GroupDetailsModel
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1),
                            CreatorId = reader.GetInt32(2),
                            AvatarData = reader.IsDBNull(3) ? null : reader.GetString(3)
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting groups: {ex.Message}");
        }

        return groups;
    }

    public List<Message> SearchMessagesInChat(int userId, string queryText, string? targetUsername, int? groupId)
    {
        var messages = new List<Message>();
        string sql = "";
        if (string.IsNullOrWhiteSpace(queryText)) return messages;
        string searchPattern = $"%{queryText}%";
        if (targetUsername == null && groupId == null)
        {
            sql = @"
            SELECT m.Id, m.SenderId, m.SenderUsername, m.Content, m.SentAt, m.BlobId 
            FROM Messages m
            WHERE m.IsDeleted = 0 AND m.Content LIKE @query
            ORDER BY m.SentAt DESC LIMIT 50";
        }
        else if (groupId != null)
        {
            sql = @"
            SELECT m.Id, m.SenderId, u.Username as SenderUsername, m.Content, m.SentAt, m.BlobId 
            FROM GroupMessages m
            JOIN Users u ON m.SenderId = u.Id
            WHERE m.GroupId = @gid AND m.Content LIKE @query
            ORDER BY m.SentAt DESC LIMIT 50";
        }
        else if (targetUsername != null)
        {
            var targetUser = GetUserByUsername(targetUsername);
            if (targetUser == null) return messages;
            int targetId = targetUser.Id;

            sql = @"
            SELECT m.Id, m.SenderId, m.Content, m.SentAt, m.BlobId 
            FROM DirectMessages m
            WHERE ((m.SenderId = @uid AND m.RecipientId = @tid) OR (m.SenderId = @tid AND m.RecipientId = @uid))
            AND m.IsDeleted = 0 AND m.Content LIKE @query
            ORDER BY m.SentAt DESC LIMIT 50";
        }
        using var cmd = new SqliteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@query", searchPattern);
        if (groupId != null) cmd.Parameters.AddWithValue("@gid", groupId);
        if (targetUsername != null)
        {
            var targetUser = GetUserByUsername(targetUsername);
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@tid", targetUser?.Id ?? 0);
        }
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var msg = new Message
            {
                Id = reader.GetInt32(0),
                SenderId = reader.GetInt32(1),
                Content = reader.GetString(reader.GetOrdinal("Content")),
                SentAt = reader.GetDateTime(reader.GetOrdinal("SentAt")).ToLocalTime(),
                BlobId = reader.IsDBNull(reader.GetOrdinal("BlobId")) ? null : reader.GetInt32(reader.GetOrdinal("BlobId"))
            };
            try { msg.SenderUsername = reader.GetString(reader.GetOrdinal("SenderUsername")); } catch { }

            messages.Add(msg);
        }

        return messages;
    }

    public List<Message> GetMessagesAroundId(int messageId, int limit, int? userId1 = null, int? userId2 = null, int? groupId = null)
    {
        var messages = new List<Message>();
        int halfLimit = limit / 2;
        string baseSelect = @"
        SELECT m.Id, m.SenderId, m.SenderUsername, m.Content, m.SentAt, m.EditedAt, m.IsDeleted, m.BlobId, 
               m.ReplyToId, m2.SenderUsername AS ReplySender, m2.Content AS ReplyContent
    ";
        string table, joins, whereCondition;

        if (groupId != null)
        {
            table = "FROM GroupMessages m";
            joins = "JOIN Users u ON m.SenderId = u.Id LEFT JOIN GroupMessages m2 ON m.ReplyToId = m2.Id LEFT JOIN Users u2 ON m2.SenderId = u2.Id";
            whereCondition = "WHERE m.GroupId = @gid AND m.IsDeleted = 0";
        }
        else if (userId1 != null && userId2 != null)
        {
            table = "FROM DirectMessages m";
            joins = "LEFT JOIN DirectMessages m2 ON m.ReplyToId = m2.Id LEFT JOIN Users u ON m2.SenderId = u.Id";
            whereCondition = "WHERE ((m.SenderId = @u1 AND m.RecipientId = @u2) OR (m.SenderId = @u2 AND m.RecipientId = @u1)) AND m.IsDeleted = 0";
        }
        else
        {
            table = "FROM Messages m";
            joins = "LEFT JOIN Messages m2 ON m.ReplyToId = m2.Id";
            whereCondition = "WHERE m.IsDeleted = 0";
        }
        var query = $@"
        SELECT * FROM (
            {baseSelect} {table} {joins} 
            {whereCondition} AND m.Id < @mid
            ORDER BY m.SentAt DESC LIMIT @half
        )
        UNION ALL
        SELECT * FROM (
            {baseSelect} {table} {joins} 
            {whereCondition} AND m.Id >= @mid
            ORDER BY m.SentAt ASC LIMIT @half
        )
        ORDER BY SentAt ASC";

        using var cmd = new SqliteCommand(query, _connection);
        cmd.Parameters.AddWithValue("@mid", messageId);
        cmd.Parameters.AddWithValue("@half", halfLimit);

        if (groupId != null) cmd.Parameters.AddWithValue("@gid", groupId);
        if (userId1 != null) cmd.Parameters.AddWithValue("@u1", userId1);
        if (userId2 != null) cmd.Parameters.AddWithValue("@u2", userId2);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var msg = new Message
            {
                Id = reader.GetInt32(0),
                SenderId = reader.GetInt32(1),
                Content = reader.GetString(reader.GetOrdinal("Content")),
                SentAt = reader.GetDateTime(reader.GetOrdinal("SentAt")).ToLocalTime(),
                BlobId = reader.IsDBNull(reader.GetOrdinal("BlobId")) ? null : reader.GetInt32(reader.GetOrdinal("BlobId"))
            };
            try { msg.SenderUsername = reader.GetString(2); } catch { }

            messages.Add(msg);
        }

        return messages;
    }

    public List<User> GetContactList(int userId)
    {
        var query = @"
        SELECT DISTINCT u.Id, u.Username, u.DisplayName, u.Bio, u.AvatarColor, u.AvatarData, u.CreatedAt
        FROM Users u
        JOIN DirectMessages m ON (m.SenderId = u.Id OR m.RecipientId = u.Id)
        LEFT JOIN ChatSettings cs ON (cs.UserId = @uid AND cs.PartnerId = u.Id)
        WHERE (m.SenderId = @uid OR m.RecipientId = @uid)
          AND u.Id != @uid
          AND m.IsDeleted = 0
          AND m.Id > COALESCE(cs.LastClearedMessageId, 0) -- <--- Показувати тільки якщо є щось новіше за горизонт
        ORDER BY m.SentAt DESC";

        var users = new List<User>();
        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@uid", userId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {   
            users.Add(new User
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                DisplayName = reader.IsDBNull(2) ? reader.GetString(1) : reader.GetString(2),
                Bio = reader.IsDBNull(3) ? "" : reader.GetString(3),
                AvatarColor = reader.IsDBNull(4) ? "#0088CC" : reader.GetString(4),
                AvatarData = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedAt = reader.GetDateTime(6)
            });
        }
        return users;
    }
    public List<User> SearchUsers(string queryText)
    {
        var query = @"
    SELECT Id, Username, DisplayName, Bio, AvatarColor, NULL as AvatarData, CreatedAt
    FROM Users
    WHERE (Username LIKE @q OR DisplayName LIKE @q)
      AND IsDeleted = 0 
    LIMIT 20";

        var users = new List<User>();
        using var command = new SqliteCommand(query, _connection);
        command.Parameters.AddWithValue("@q", $"%{queryText}%");

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            users.Add(new User
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                DisplayName = reader.IsDBNull(2) ? reader.GetString(1) : reader.GetString(2),
                Bio = reader.IsDBNull(3) ? "" : reader.GetString(3),
                AvatarColor = reader.IsDBNull(4) ? "#0088CC" : reader.GetString(4),
                AvatarData = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedAt = reader.GetDateTime(6)
            });
        }
        return users;
    }

    public void SetPrivateChatCleared(int userId, int partnerId, int lastMessageId)
    {
        var query = @"
        INSERT INTO ChatSettings (UserId, PartnerId, LastClearedMessageId)
        VALUES (@uid, @pid, @mid)
        ON CONFLICT(UserId, PartnerId) 
        DO UPDATE SET LastClearedMessageId = @mid;";

        using var cmd = new SqliteCommand(query, _connection);
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@pid", partnerId);
        cmd.Parameters.AddWithValue("@mid", lastMessageId);
        cmd.ExecuteNonQuery();
    }

    public int GetLastClearedMessageId(int userId, int partnerId)
    {
        var query = "SELECT LastClearedMessageId FROM ChatSettings WHERE UserId = @uid AND PartnerId = @pid";
        using var cmd = new SqliteCommand(query, _connection);
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@pid", partnerId);
        var res = cmd.ExecuteScalar();
        return res != null ? Convert.ToInt32(res) : 0;
    }

    public void DeleteAllPrivateMessages(int userId1, int userId2)
    {
        var query = @"DELETE FROM DirectMessages 
                  WHERE (SenderId = @u1 AND RecipientId = @u2) 
                     OR (SenderId = @u2 AND RecipientId = @u1)";
        using var cmd = new SqliteCommand(query, _connection);
        cmd.Parameters.AddWithValue("@u1", userId1);
        cmd.Parameters.AddWithValue("@u2", userId2);

        if (cmd.ExecuteNonQuery() > 0)
        {
            CleanUpOrphanedBlobs();
            CleanUpOrphanedUsers();
        }
    }

    public void TryCleanupGarbageMessages(int userId1, int userId2)
    {
        try
        {
            int horizon1 = GetLastClearedMessageId(userId1, userId2);
            int horizon2 = GetLastClearedMessageId(userId2, userId1);
            int safeDeleteLimit = Math.Min(horizon1, horizon2);

            if (safeDeleteLimit > 0)
            {
                var query = @"
                DELETE FROM DirectMessages 
                WHERE ((SenderId = @u1 AND RecipientId = @u2) OR (SenderId = @u2 AND RecipientId = @u1))
                  AND Id <= @limit";

                using var cmd = new SqliteCommand(query, _connection);
                cmd.Parameters.AddWithValue("@u1", userId1);
                cmd.Parameters.AddWithValue("@u2", userId2);
                cmd.Parameters.AddWithValue("@limit", safeDeleteLimit);

                int rows = cmd.ExecuteNonQuery();
                if (rows > 0)
                {
                    Console.WriteLine($"[GC] Cleaned up {rows} orphaned messages...");
                    CleanUpOrphanedBlobs();
                    CleanUpOrphanedUsers();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GC Error] {ex.Message}");
        }
    }

    public void CleanUpOrphanedUsers()
    {
        try
        {
            var findOrphansQuery = @"
            SELECT Id FROM Users 
            WHERE Username LIKE 'deleted_%'
              AND Id NOT IN (SELECT DISTINCT SenderId FROM DirectMessages)
              AND Id NOT IN (SELECT DISTINCT RecipientId FROM DirectMessages)
              AND Id NOT IN (SELECT DISTINCT UserId FROM GroupMembers)
              AND Id NOT IN (SELECT DISTINCT CreatorId FROM Groups)";

            var orphanIds = new List<int>();
            using (var cmd = new SqliteCommand(findOrphansQuery, _connection))
            {
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    orphanIds.Add(reader.GetInt32(0));
                }
            }

            if (orphanIds.Count == 0) return;

            Console.WriteLine($"[System] Found {orphanIds.Count} orphaned users to delete. Cleaning up dependencies...");
            var idsStr = string.Join(",", orphanIds);

            using var transaction = _connection.BeginTransaction();
            try
            {
                using (var cmd = new SqliteCommand($"DELETE FROM ChatSettings WHERE UserId IN ({idsStr}) OR PartnerId IN ({idsStr})", _connection, transaction))
                {
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = new SqliteCommand($"DELETE FROM Messages WHERE SenderId IN ({idsStr})", _connection, transaction))
                {
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = new SqliteCommand($"DELETE FROM GroupMessages WHERE SenderId IN ({idsStr})", _connection, transaction))
                {
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = new SqliteCommand($"DELETE FROM Users WHERE Id IN ({idsStr})", _connection, transaction))
                {
                    int deleted = cmd.ExecuteNonQuery();
                    Console.WriteLine($"[System] Successfully deleted {deleted} users from DB.");
                }
                CleanUpOrphanedBlobs(transaction);
                transaction.Commit();
                using (var vacuumCmd = new SqliteCommand("VACUUM;", _connection))
                {
                    vacuumCmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                Console.WriteLine($"[Cleanup Error] Failed to delete orphans: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Cleanup Error] {ex.Message}");
        }
    }

    public void CleanUpOrphanedBlobs(SqliteTransaction? transaction = null)
    {
        try
        {
            var query = @"
            DELETE FROM FileBlobs 
            WHERE Id NOT IN (SELECT BlobId FROM Messages WHERE BlobId IS NOT NULL) 
              AND Id NOT IN (SELECT BlobId FROM DirectMessages WHERE BlobId IS NOT NULL)
              AND Id NOT IN (SELECT BlobId FROM GroupMessages WHERE BlobId IS NOT NULL)";

            using var cmd = new SqliteCommand(query, _connection, transaction);
            int rows = cmd.ExecuteNonQuery();

            if (rows > 0)
            {
                Console.WriteLine($"[System] Cleaned up {rows} unused file blobs.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Blob Cleanup Error] {ex.Message}");
        }
    }

    public void MarkGlobalMessagesAsRead(int readerId)
    {
        var query = "UPDATE Messages SET IsRead = 1 WHERE SenderId != @uid AND IsRead = 0";
        using var cmd = new SqliteCommand(query, _connection);
        cmd.Parameters.AddWithValue("@uid", readerId);
        cmd.ExecuteNonQuery();
    }

    public void MarkPrivateMessagesAsRead(int readerId, int senderId)
    {
        var query = "UPDATE DirectMessages SET IsRead = 1 WHERE RecipientId = @rid AND SenderId = @sid AND IsRead = 0";

        using var cmd = new SqliteCommand(query, _connection);
        cmd.Parameters.AddWithValue("@rid", readerId);
        cmd.Parameters.AddWithValue("@sid", senderId);

        cmd.ExecuteNonQuery();
    }

    public void MarkGroupMessagesAsRead(int groupId, int readerId)
    {
        var query = "UPDATE GroupMessages SET IsRead = 1 WHERE GroupId = @gid AND SenderId != @uid AND IsRead = 0";
        using var cmd = new SqliteCommand(query, _connection);
        cmd.Parameters.AddWithValue("@gid", groupId);
        cmd.Parameters.AddWithValue("@uid", readerId);
        cmd.ExecuteNonQuery();
    }

    public bool IsGroupNameTaken(string name)
    {
        var query = "SELECT COUNT(*) FROM Groups WHERE Name = @name COLLATE NOCASE";
        using var cmd = new SqliteCommand(query, _connection);
        cmd.Parameters.AddWithValue("@name", name);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
