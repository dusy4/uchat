# Running from Binary

Both applications run **ONLY** from binary DLL files using the `dotnet` command. No scripts, no .exe files required.

## Build

Build the entire solution with one command:
```bash
dotnet build uchat.sln -c Release -m
```

This creates the binary DLL files:
- `uchat_server/bin/Release/net8.0/uchat_server.dll`
- `uchat/bin/Release/net8.0-windows10.0.19041.0/uchat.dll`

## Run Server

Run the server binary directly:
```bash
dotnet uchat_server/bin/Release/net8.0/uchat_server.dll 8080
```

Or navigate to the directory first:
```bash
cd uchat_server/bin/Release/net8.0
dotnet uchat_server.dll 8080
```

## Run Client

Run the client binary directly:
```bash
dotnet uchat/bin/Release/net8.0-windows10.0.19041.0/uchat.dll
```

Or navigate to the directory first:
```bash
cd uchat/bin/Release/net8.0-windows10.0.19041.0
dotnet uchat.dll
```

## Dependencies

All required libraries are included in the project via NuGet packages:
- **Server**: Microsoft.Data.Sqlite, System.Text.Json
- **Client**: Microsoft.WindowsAppSDK, Microsoft.Data.Sqlite, System.Text.Json

No external library installation required. All dependencies are restored during the build process.

## Binary Names

- Server binary: `uchat_server.dll`
- Client binary: `uchat.dll`

**Important:** Run the `.dll` files directly using `dotnet`. Ignore any `.exe` files that may be created during build - those are just .NET host executables. The actual application code is in the DLL binary.

## Dependencies Included

All required libraries are automatically included in the build output:
- **Server**: Microsoft.Data.Sqlite (with native SQLite libraries), System.Text.Json
- **Client**: Microsoft.WindowsAppSDK, Microsoft.Data.Sqlite, System.Text.Json

All dependency DLLs are placed in the same directory as the main binary during build. No manual library installation required.

