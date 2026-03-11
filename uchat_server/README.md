# uchat_server - Server Application

This is the server application for the uchat messaging system.

## Building

Build command:
```
dotnet build uchat.sln -c Release -m
```

## Running

Run the server binary directly:
```bash
dotnet uchat_server/bin/Release/net8.0/uchat_server.dll 8080
```

The server application takes one command-line argument:
- Network port (1-65535)

If no arguments are provided, the server displays usage information and exits.

## Features

- Concurrent server supporting multiple clients
- SQLite database for persistent chat history
- User authentication with password hashing
- Real-time message broadcasting
- Message editing and deletion support
- Process ID display at startup
- Runs as a daemon service

## Database

The server automatically creates a SQLite database file (`uchat.db`) in the application directory on first run. The database contains:
- Users table: Stores user accounts with hashed passwords
- Messages table: Stores all chat messages with timestamps

