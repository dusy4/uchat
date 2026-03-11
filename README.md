# uchat - Messaging Application

A client-server messaging application built with C# following the Track C# challenge requirements.

**WATCH THE PRESENTATION + DEMO**

[![Watch the video](https://img.youtube.com/vi/tkkL7y9ud24/maxresdefault.jpg)](https://www.youtube.com/watch?v=tkkL7y9ud24&t=4778s)

## Project Structure

- `uchat_server/` - Server application (console)
- `uchat/` - Client application (UWP with Windows 7/XP/Vista style design)

## Features

### Server
- Concurrent server architecture
- SQLite database for persistent chat history
- User authentication (login/register)
- Password hashing (SHA256)
- Real-time message broadcasting
- Message editing and deletion
- Process ID display
- Daemon-like operation

### Client
- Windows UWP application
- Windows 7/XP/Vista style translucent, watery interface
- WhatsApp-like chat design
- User authentication
- Real-time messaging
- Message editing (own messages only)
- Message deletion (own messages only)
- Automatic reconnection on connection loss
- Chat history loading
- Connection status indicator

## Architecture

The application follows a 3-tier architecture:
1. **Presentation Layer**: UWP client UI
2. **Business Logic Layer**: Protocol handling, authentication
3. **Data Layer**: SQLite database

## Security

- Password hashing using SHA256
- Secure socket communication
- User authentication required

## Requirements Met

✅ Server takes port as argument, displays usage if missing
✅ Server displays process ID at startup
✅ Server runs as daemon
✅ Concurrent server supporting multiple clients
✅ SQLite database for persistent chat history
✅ Client takes server IP and port as arguments
✅ Basic authentication (username/password)
✅ Message editing and deletion
✅ Reconnection handling
✅ Proper resource management
✅ C# naming conventions
✅ Builds in less than 30 seconds

## Design

The client features a translucent, watery interface inspired by Windows 7/XP/Vista design:
- Glass-like panels with transparency
- Watery blue color scheme
- Smooth gradients
- WhatsApp-like chat bubbles
- Modern yet nostalgic aesthetic

## Getting Started

### Prerequisites
* [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* [Windows App SDK Runtime](https://aka.ms/windowsappsdk/1.6/1.6.2/windowsappruntimeinstall-x64.exe) (for the client)

### 1. Running the Server
Next are steps after project was built.
The server requires a **port** as a command-line argument.

```powershell
# Navigate to the server build directory
cd uchat_server/bin/Release/net8.0/

# Run with a specific port (e.g., 1337)
.\uchat_server.exe 1337
```
### 2. Running the Client
```powershell
# Navigate to the client build directory
cd uchat/bin/x64/Release/net8.0-windows10.0.22000.0

# Run connecting to localhost
.\uchat.exe 127.0.0.1 1337

# OR run connecting to a remote server 
.\uchat.exe 192.168.0.105 1337
```