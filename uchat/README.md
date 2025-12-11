# uchat - UWP Client Application

This is the UWP client application for the uchat messaging system.

## Building

The project requires .NET 8.0 and Windows SDK.

Build command:
```
dotnet build uchat.sln -c Release -m
```

## Running

Run the client binary directly:
```bash
dotnet uchat/bin/Release/net8.0-windows10.0.19041.0/uchat.dll
```

The client application UI allows you to enter:
- Server IP address (default: 127.0.0.1)
- Server port (default: 8080)

## Features

- Windows 7/XP/Vista style translucent, watery interface
- WhatsApp-like chat interface
- User authentication (login/register)
- Real-time messaging
- Message editing and deletion
- Automatic reconnection on connection loss
- Chat history persistence

