# Database Storage Information

## Message Storage Location

Messages are stored in a **SQLite database** file named `uchat.db`.

### Database File Location

The database file is created in the **server's working directory** (where the server executable is run from):

```
[Server executable directory]/uchat.db
```

For example:
- If you run: `dotnet uchat_server/bin/Release/net8.0/uchat_server.dll 1337`
- The database will be at: `uchat_server/bin/Release/net8.0/uchat.db`

### Database Structure

#### Messages Table
```sql
CREATE TABLE Messages (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SenderId INTEGER NOT NULL,
    SenderUsername TEXT NOT NULL,
    Content TEXT NOT NULL,
    SentAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    EditedAt DATETIME,
    IsDeleted INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (SenderId) REFERENCES Users(Id)
);
```

**Fields:**
- `Id`: Unique message identifier (auto-increment)
- `SenderId`: Foreign key to Users table
- `SenderUsername`: Username of the sender (denormalized for quick access)
- `Content`: The actual message text
- `SentAt`: Timestamp when message was sent
- `EditedAt`: Timestamp when message was last edited (NULL if never edited)
- `IsDeleted`: Soft delete flag (0 = active, 1 = deleted)

#### Users Table
```sql
CREATE TABLE Users (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Username TEXT UNIQUE NOT NULL,
    PasswordHash TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);
```

### Database Operations

**Creating Messages:**
- `CreateMessage(senderId, senderUsername, content)` - Inserts a new message

**Retrieving Messages:**
- `GetMessages(limit)` - Gets the last N messages (default 100), ordered by SentAt

**Updating Messages:**
- `UpdateMessage(messageId, newContent)` - Updates message content and sets EditedAt

**Deleting Messages:**
- `DeleteMessage(messageId)` - Soft deletes a message (sets IsDeleted = 1)

### Persistence

- ✅ Messages persist across server restarts
- ✅ Messages persist across client disconnections
- ✅ Database is automatically created on first server run
- ✅ No manual configuration required (single-click deployment)

### Accessing the Database

You can view the database using any SQLite browser tool:
- **DB Browser for SQLite** (free, cross-platform)
- **SQLiteStudio** (free, cross-platform)
- **VS Code SQLite extension**

Simply open the `uchat.db` file from the server's directory.

