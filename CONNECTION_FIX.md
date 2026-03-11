# Connection Fix

## Issue Fixed
The client was hardcoded to connect to port **8080**, but the server was running on port **1337**.

## Changes Made

1. **Updated default port** in `uchat/MainPage.xaml.cs`:
   - Changed from `DEFAULT_SERVER_PORT = 8080` to `DEFAULT_SERVER_PORT = 1337`

2. **Improved error handling** in `uchat/Services/NetworkClient.cs`:
   - Added `LastConnectionError` property to capture connection errors
   - Better SocketException handling with specific error codes
   - Increased connection timeouts from 5s to 10s

3. **Enhanced error messages** in `uchat/MainPage.xaml.cs`:
   - Shows specific connection error messages
   - Displays the server address and port in error messages
   - Better user feedback for connection issues

## Testing

To test the connection:
1. Start the server: `dotnet uchat_server/bin/Release/net8.0/uchat_server.dll 1337`
2. Start the client: `dotnet uchat/bin/Release/net8.0-windows10.0.22000.0/uchat.dll`
3. Enter username and password
4. Click Login or Register

The client will now connect to `127.0.0.1:1337` by default.

## If Connection Still Fails

If you still see connection errors, check:
1. **Firewall**: Windows Firewall might be blocking the connection
2. **Server running**: Make sure the server is actually running and listening
3. **Port match**: Verify the server port matches the client's default (1337)
4. **Error message**: The improved error messages will show the specific issue

