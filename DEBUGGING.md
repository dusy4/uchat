# Debugging Connection Issues

## Added Debug Logging

Both client and server now have extensive debug logging to help identify connection issues.

### Server Logging
The server will log:
- When clients connect
- Message lengths received
- Raw JSON messages
- Parsed message types
- Login/Register attempts
- Success/failure of operations
- Errors with stack traces

### Client Logging
The client will log (to Debug output):
- Connection attempts
- Message sending/receiving
- Message lengths
- Parsed message types
- Errors with stack traces

## How to Debug

1. **Start the server** and watch the console output:
   ```bash
   dotnet uchat_server/bin/Release/net8.0/uchat_server.dll 1337
   ```

2. **Start the client** and check Visual Studio Output window (Debug) or use DebugView:
   ```bash
   dotnet uchat/bin/Release/net8.0-windows10.0.22000.0/uchat.dll
   ```

3. **Try to register/login** and watch both consoles:
   - Server should show: "Client connected", "Received message length", "Handling register request"
   - Client should show: "Sending message type: Register", "Received message length"

## Common Issues to Check

1. **Server not receiving messages**: Check if "Client connected" appears in server console
2. **Invalid message length**: Check the message length value - should be reasonable (50-500 bytes for auth)
3. **JSON parsing errors**: Check the raw JSON in server console
4. **Message type mismatch**: Verify the message Type is being parsed correctly

## What to Look For

When you click Register, you should see in the **server console**:
```
Client connected from 127.0.0.1:xxxxx
Received message length: [some number]
Received message: {"Type":4,"Parameters":{"username":"...","password":"..."}}
Parsed message type: Register
Handling register request
Register attempt for user: [username]
Registration successful for user: [username]
Sending message type: RegisterResponse, length: [number]
```

If you don't see these messages, the connection or message sending is failing.

