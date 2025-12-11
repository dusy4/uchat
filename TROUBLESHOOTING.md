# Troubleshooting Connection Issues

## Current Status

I've added extensive debug logging to both client and server. When you run the applications, you'll see detailed information about what's happening.

## Steps to Debug

### 1. Start Server First
```bash
dotnet uchat_server/bin/Release/net8.0/uchat_server.dll 1337
```

You should see:
```
Server started on port 0.0.0.0:1337
Process ID: [number]
```

### 2. Start Client
```bash
dotnet uchat/bin/Release/net8.0-windows10.0.22000.0/uchat.dll
```

### 3. Try to Register

When you click "Create Account", watch the **server console**. You should see:
```
Client connected from 127.0.0.1:xxxxx
Received message length: [number]
Received message: {"Type":4,"Parameters":{"username":"...","password":"..."}}
Parsed message type: Register
Handling register request
Register attempt for user: [username]
Registration successful for user: [username]
Sending message type: RegisterResponse, length: [number]
Message sent successfully
```

### 4. Check Client Debug Output

In Visual Studio, open the **Output** window and select **Debug**. You should see:
```
Attempting to connect to 127.0.0.1:1337
Connected successfully to 127.0.0.1:1337
ReceiveMessagesAsync started
Sending message type: Register, length: [number]
Message JSON: {"Type":4,"Parameters":{"username":"...","password":"..."}}
Message sent successfully
Received message length: [number]
Received message: {"Type":11,"Parameters":{"success":"true","username":"..."}}
Parsed message type: RegisterResponse
```

## What Each Message Means

- **"Client connected"** - Server received TCP connection ✓
- **"Received message length"** - Server received length prefix ✓
- **"Received message"** - Server received JSON message ✓
- **"Parsed message type"** - Server successfully parsed message ✓
- **"Handling register request"** - Server processing your request ✓
- **"Sending message type: RegisterResponse"** - Server sending response ✓

## If You Don't See These Messages

1. **No "Client connected"**: 
   - Connection is failing
   - Check firewall settings
   - Verify server is actually running

2. **"Invalid message length"**: 
   - Protocol mismatch
   - Check if both client and server are using latest code

3. **"Error parsing message"**: 
   - JSON format issue
   - Check the raw JSON in server console

4. **No response received**: 
   - Server sent response but client didn't receive it
   - Check client debug output for "Received message length"

## Quick Test

Run this to see if the connection works at all:
1. Start server
2. Start client  
3. Click Register
4. **Immediately check server console** - you should see connection messages

If server shows nothing, the connection isn't being established.
If server shows connection but no messages, the message sending is failing.
If server shows messages but client hangs, the response isn't being received.

