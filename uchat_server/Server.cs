using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using uchat_server.Database;
using uchat_server.Protocol;
using uchat_server.Services;

namespace uchat_server;

public class Server
{
    private readonly TcpListener _listener;
    private readonly DatabaseContext _db;
    private readonly AuthenticationService _authService;
    private readonly List<ClientHandler> _clients = new();
    private readonly object _clientsLock = new();
    private readonly ConcurrentDictionary<string, string> _gifCache = new();
    private readonly ConcurrentDictionary<string, string> _stickerCache = new();

    public Server(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _db = new DatabaseContext();
        _authService = new AuthenticationService(_db);
    }
    
    public string? GetCachedGif(string filename) => _gifCache.TryGetValue(filename, out var data) ? data : null;
    public string? GetCachedSticker(string key) => _stickerCache.TryGetValue(key, out var data) ? data : null;
    public void CacheGif(string filename, string base64Data) => _gifCache[filename] = base64Data;
    public void CacheSticker(string key, string base64Data) => _stickerCache[key] = base64Data;
    
    private async Task PreloadMediaCacheAsync()
    {
        Console.WriteLine("[Cache] Preloading GIFs and stickers into memory...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        var gifsPath = GetGifsPath();
        if (Directory.Exists(gifsPath))
        {
            var gifFiles = Directory.GetFiles(gifsPath, "*.gif", SearchOption.TopDirectoryOnly);
            int loadedGifs = 0;
            foreach (var gifFile in gifFiles)
            {
                try
                {
                    var filename = Path.GetFileName(gifFile);
                    var bytes = await File.ReadAllBytesAsync(gifFile);
                    var base64 = Convert.ToBase64String(bytes);
                    _gifCache[filename] = base64;
                    loadedGifs++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Cache] Failed to preload GIF {gifFile}: {ex.Message}");
                }
            }
            Console.WriteLine($"[Cache] Preloaded {loadedGifs} GIFs");
        }

        var stickersPath = GetStickersPath();
        if (Directory.Exists(stickersPath))
        {
            int loadedStickers = 0;
            foreach (var packDir in Directory.GetDirectories(stickersPath))
            {
                var packName = new DirectoryInfo(packDir).Name;
                var stickerFiles = Directory.GetFiles(packDir)
                    .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".webp"));
                    
                foreach (var stickerFile in stickerFiles)
                {
                    try
                    {
                        var fileName = Path.GetFileName(stickerFile);
                        var key = $"{packName}|{fileName}";
                        var bytes = await File.ReadAllBytesAsync(stickerFile);
                        var base64 = Convert.ToBase64String(bytes);
                        _stickerCache[key] = base64;
                        loadedStickers++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Cache] Failed to preload sticker {stickerFile}: {ex.Message}");
                    }
                }
            }
            Console.WriteLine($"[Cache] Preloaded {loadedStickers} stickers");
        }
        
        Console.WriteLine($"[Cache] Media cache preload complete in {sw.ElapsedMilliseconds}ms");
    }
    
    private string GetGifsPath()
    {
        var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var assemblyDir = Path.GetDirectoryName(assemblyLocation);
        var projectRoot = assemblyDir;
        for (int i = 0; i < 4; i++)
        {
            projectRoot = Path.GetDirectoryName(projectRoot);
            if (projectRoot == null) break;
        }
        if (projectRoot == null || !Directory.Exists(Path.Combine(projectRoot, "Database")))
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            projectRoot = baseDir;
            for (int i = 0; i < 4; i++)
            {
                var parent = Path.GetDirectoryName(projectRoot);
                if (parent == null) break;
                projectRoot = parent;
            }
        }
        return Path.Combine(projectRoot ?? "", "Database", "gifs");
    }
    
    private string GetStickersPath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var pathInBin = Path.Combine(baseDir, "Stickers");
        if (Directory.Exists(pathInBin)) return pathInBin;
        var projectRoot = baseDir;
        for (int i = 0; i < 4; i++)
        {
            projectRoot = Directory.GetParent(projectRoot)?.FullName;
            if (projectRoot == null) break;
            var pathInRoot = Path.Combine(projectRoot, "Stickers");
            if (Directory.Exists(pathInRoot)) return pathInRoot;
        }
        return Path.Combine(baseDir, "Stickers");
    }
    
    public async Task StartAsync()
    {
        await PreloadMediaCacheAsync();
        
        _listener.Start();
        Console.WriteLine($"Server started on port {_listener.LocalEndpoint}");
        Console.WriteLine($"Process ID: {Environment.ProcessId}");

        while (true)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync();
                var handler = new ClientHandler(client, null, null, this);

                lock (_clientsLock)
                {
                    _clients.Add(handler);
                }

                _ = Task.Run(async () =>
                {
                    await handler.HandleClientAsync();
                    lock (_clientsLock)
                    {
                        _clients.Remove(handler);
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accepting client: {ex.Message}");
            }
        }
    }

    public void BroadcastMessage(string content, string senderUsername, int? messageId = null, int? replyToId = null)
    {
        var parameters = new Dictionary<string, string>
        {
            { "sender", senderUsername },
            { "timestamp", DateTime.UtcNow.ToString("O") },
            { "messageId", messageId?.ToString() ?? "" }
        };
        if (replyToId.HasValue)
        {
            parameters.Add("replyToId", replyToId.Value.ToString());
        }
        var message = new ProtocolMessage
        {
            Type = MessageType.MessageReceived,
            Data = content,
            Parameters = parameters
        };

        BroadcastToAll(message);
    }

    public void BroadcastSystemMessage(string content)
    {
        var message = new ProtocolMessage
        {
            Type = MessageType.MessageReceived,
            Data = content,
            Parameters = new Dictionary<string, string>
            {
                { "sender", "System" },
                { "timestamp", DateTime.UtcNow.ToString("O") },
                { "isSystem", "true" }
            }
        };

        BroadcastToAll(message);
    }

    public void BroadcastEdit(int messageId, string newContent)
    {
        var message = new ProtocolMessage
        {
            Type = MessageType.MessageReceived,
            Data = newContent,
            Parameters = new Dictionary<string, string>
            {
                { "action", "edit" },
                { "messageId", messageId.ToString() },
                { "timestamp", DateTime.UtcNow.ToString("O") }
            }
        };

        BroadcastToAll(message);
    }

    public void BroadcastDelete(int messageId)
    {
        var message = new ProtocolMessage
        {
            Type = MessageType.MessageReceived,
            Data = "",
            Parameters = new Dictionary<string, string>
            {
                { "action", "delete" },
                { "messageId", messageId.ToString() }
            }
        };

        BroadcastToAll(message);
    }

    public void BroadcastToAll(ProtocolMessage message)
    {
        byte[] dataToSend;
        try
        {
            dataToSend = message.ToBytes();
        }
        catch
        {
            Console.WriteLine("Error serializing broadcast message");
            return;
        }

        List<ClientHandler> clientsSnapshot;
        lock (_clientsLock)
        {
            clientsSnapshot = _clients.ToList(); 
        }

        foreach (var client in clientsSnapshot)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await client.SendMessageAsync(message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Broadcast error: {ex.Message}");
                }
            });
        }
    }

    public async Task SendPrivateMessageToUserAsync(string targetUsername, ProtocolMessage message)
    {
        ClientHandler? targetClient = null;

        lock (_clientsLock)
        {
            targetClient = _clients.FirstOrDefault(c => c.GetCurrentUser()?.Username == targetUsername);
        }

        if (targetClient != null)
        {
            await targetClient.SendMessageAsync(message);
        }
    }

    public void BroadcastNewUser(ProtocolMessage message)
    {
        BroadcastToAll(message);
    }

    public void BroadcastGifMessage(ProtocolMessage message)
    {
        BroadcastToAll(message);
    }

    public async Task CheckAndSendScheduledMessagesAsync()
    {
        try
        {
            var dueMessages = _db.GetDueScheduledMessages();
            foreach (var scheduledMsg in dueMessages)
            {
                try
                {
                    if (scheduledMsg.IsPrivate && !string.IsNullOrEmpty(scheduledMsg.TargetUsername))
                    {
                        var recipient = _db.GetUserByUsername(scheduledMsg.TargetUsername);
                        if (recipient != null)
                        {
                            var messageId = _db.CreatePrivateMessage(scheduledMsg.SenderId, recipient.Id, scheduledMsg.Content);
                            var message = new ProtocolMessage
                            {
                                Type = MessageType.PrivateMessageReceived,
                                Data = scheduledMsg.Content,
                                Parameters = new Dictionary<string, string>
                                {
                                    { "sender", scheduledMsg.SenderUsername },
                                    { "targetUsername", scheduledMsg.TargetUsername },
                                    { "timestamp", DateTime.UtcNow.ToString("O") },
                                    { "messageId", messageId.ToString() }
                                }
                            };
                            await SendPrivateMessageToUserAsync(scheduledMsg.TargetUsername, message);
                            await SendPrivateMessageToUserAsync(scheduledMsg.SenderUsername, message);
                        }
                    }
                    else
                    {
                        var messageId = _db.CreateMessage(scheduledMsg.SenderId, scheduledMsg.SenderUsername, scheduledMsg.Content);
                        BroadcastMessage(scheduledMsg.Content, scheduledMsg.SenderUsername, messageId);
                    }
                    
                    _db.DeleteScheduledMessage(scheduledMsg.Id);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending scheduled message {scheduledMsg.Id}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking scheduled messages: {ex.Message}");
        }
    }

    public async Task SendToGroupMembersAsync(List<int> memberIds, ProtocolMessage message)
    {
        List<ClientHandler> targets;

        lock (_clientsLock)
        {
            targets = _clients.Where(c =>
                c.GetCurrentUser() != null &&
                memberIds.Contains(c.GetCurrentUser()!.Id)
            ).ToList();
        }

        var tasks = targets.Select(client => Task.Run(async () =>
        {
            try
            {
                await client.SendMessageAsync(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Broadcast Error] Failed to send to {client.GetCurrentUser()?.Username}: {ex.Message}");
            }
        }));

        await Task.WhenAll(tasks);
    }

    public void BroadcastUserProfileUpdate(int userId, string newDisplayName, string realUsername, string? newAvatarBase64, bool isDeleted = false)
    {
        var message = new ProtocolMessage
        {
            Type = MessageType.ProfileUpdated,
            Data = newAvatarBase64 ?? "",
            Parameters = new Dictionary<string, string>
        {
            { "userId", userId.ToString() },
            { "username", newDisplayName },
            { "realUsername", realUsername }, 
            { "isGlobalUpdate", "true" },
            { "isDeleted", isDeleted.ToString().ToLower() }
        }
        };

        BroadcastToAll(message);
    }

    public void Stop()
    {
        _listener.Stop();
        _db.Dispose();
    }
}