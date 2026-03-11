using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using uchat_server.Database;
using uchat_server.Models;
using uchat_server.Protocol;
using uchat_server.Services;

namespace uchat_server;

public class ClientHandler
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly DatabaseContext _db;
    private readonly AuthenticationService _authService;
    private readonly Server _server;
    private User? _currentUser;
    private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
    public User? GetCurrentUser() => _currentUser;

    public ClientHandler(TcpClient client, DatabaseContext db, AuthenticationService authService, Server server)
    {
        _client = client;
        _stream = client.GetStream();
        _db = new DatabaseContext();
        _authService = new AuthenticationService(_db);
        _server = server;

        _client.NoDelay = true;
        _client.ReceiveBufferSize = 65536;
        _client.SendBufferSize = 65536;
        _client.ReceiveTimeout = 0;
        _client.SendTimeout = 0;
        _stream.ReadTimeout = Timeout.Infinite;
        _stream.WriteTimeout = Timeout.Infinite;
    }

    public async Task HandleClientAsync()
    {
        try
        {
            Console.WriteLine($"Client connected from {_client.Client.RemoteEndPoint}");

            while (true)
            {
                if (!_client.Connected || !_stream.CanRead)
                {
                    Console.WriteLine("Client disconnected");
                    break;
                }

                try
                {
                    var lengthBytes = new byte[4];
                    var lengthBytesRead = 0;
                    while (lengthBytesRead < 4)
                    {
                        if (!_client.Connected || !_stream.CanRead) return;
                        var read = await _stream.ReadAsync(lengthBytes, lengthBytesRead, 4 - lengthBytesRead);
                        if (read == 0) return;
                        lengthBytesRead += read;
                    }

                    var messageLength = BitConverter.ToInt32(lengthBytes, 0);

                    if (messageLength <= 0 || messageLength > 100 * 1024 * 1024)
                    {
                        Console.WriteLine($"Invalid message length: {messageLength}");
                        break;
                    }

                    var messageBytes = new byte[messageLength];
                    var totalRead = 0;
                    while (totalRead < messageLength)
                    {
                        if (!_client.Connected || !_stream.CanRead) return;
                        var read = await _stream.ReadAsync(messageBytes, totalRead, messageLength - totalRead);
                        if (read == 0) return;
                        totalRead += read;
                    }

                    var message = ProtocolMessage.FromBytes(messageBytes);
                    await ProcessMessageAsync(message);
                }
                catch (System.IO.IOException)
                {
                    Console.WriteLine("Client connection lost (IOException)");
                    break;
                }
                catch (System.Net.Sockets.SocketException)
                {
                    Console.WriteLine("Client connection lost (SocketException)");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling client: {ex.Message}");
        }
        finally
        {
            try
            {
                if (_currentUser != null)
                {
                    _server.BroadcastSystemMessage($"{_currentUser.Username} left the chat");
                }
            }
            catch { }
            finally
            {
                try
                {
                    _db.Dispose();
                    _stream?.Close();
                    _client?.Close();
                }
                catch { }
            }
        }
    }

    private async Task ProcessMessageAsync(ProtocolMessage message)
    {
        Console.WriteLine($"Processing message type: {message.Type}");
        switch (message.Type)
        {
            case MessageType.Login:
                await HandleLoginAsync(message);
                break;
            case MessageType.Register:
                await HandleRegisterAsync(message);
                break;
            case MessageType.SendMessage:
                await HandleSendMessageAsync(message);
                break;
            case MessageType.EditMessage:
                await HandleEditMessageAsync(message);
                break;
            case MessageType.DeleteMessage:
                await HandleDeleteMessageAsync(message);
                break;
            case MessageType.GetHistory:
                await HandleGetHistoryAsync(message);
                break;
            case MessageType.Heartbeat:
                await SendResponseAsync(MessageType.Heartbeat, "OK", null);
                break;
            case MessageType.GetUsers:
                await HandleGetUsersAsync();
                break;
            case MessageType.SearchUsers:
                await HandleSearchUsersAsync(message);
                break;
            case MessageType.SendPrivateMessage:
                await HandleSendPrivateMessageAsync(message);
                break;
            case MessageType.GetPrivateHistory:
                await HandleGetPrivateHistoryAsync(message);
                break;
            case MessageType.EditPrivateMessage:
                await HandleEditPrivateMessageAsync(message);
                break;
            case MessageType.DeletePrivateMessage:
                await HandleDeletePrivateMessageAsync(message);
                break;
            case MessageType.UpdateProfile:
                await HandleUpdateProfileAsync(message);
                break;
            case MessageType.ScheduleMessage:
                await HandleScheduleMessageAsync(message);
                break;
            case MessageType.GetScheduledMessages:
                await HandleGetScheduledMessagesAsync(message);
                break;
            case MessageType.DeleteAccount:
                await HandleDeleteAccountAsync();
                break;
            case MessageType.UpdateScheduledMessage:
                await HandleUpdateScheduledMessageAsync(message);
                break;
            case MessageType.DeleteScheduledMessage:
                await HandleDeleteScheduledMessageAsync(message);
                break;
            case MessageType.GetGifList:
                await HandleGetGifListAsync();
                break;
            case MessageType.SendGif:
                await HandleSendGifAsync(message);
                break;
            case MessageType.GetGif:
                await HandleGetGifAsync(message);
                break;
            case MessageType.SendVoiceMessage:
                await HandleSendVoiceMessageAsync(message);
                break;
            case MessageType.SendImage:
            case MessageType.SendFile:
                await HandleSendMediaAsync(message);
                break;
            case MessageType.GetFileContent:
                await HandleGetFileContentAsync(message);
                break;
            case MessageType.CreateGroup:
                await HandleCreateGroupAsync(message);
                break;
            case MessageType.SendGroupMessage:
                await HandleSendGroupMessageAsync(message);
                break;
            case MessageType.GetGroups:
                await HandleGetGroupsAsync();
                break;
            case MessageType.GetGroupHistory:
                await HandleGetGroupHistoryAsync(message);
                break;
            case MessageType.GetGroupDetails:
                await HandleGetGroupDetailsAsync(message);
                break;
            case MessageType.AddGroupMember:
                await HandleAddGroupMemberAsync(message);
                break;
            case MessageType.RemoveGroupMember:
                await HandleRemoveGroupMemberAsync(message);
                break;
            case MessageType.DeleteGroup:
                await HandleDeleteGroupAsync(message);
                break;
            case MessageType.DeleteGroupMessage:
                await HandleDeleteGroupMessageAsync(message);
                break;
            case MessageType.EditGroupMessage:
                await HandleEditGroupMessageAsync(message);
                break;
            case MessageType.ChangePassword:
                await HandleChangePasswordAsync(message);
                break;
            case MessageType.UpdateGroupProfile:
                await HandleUpdateGroupProfileAsync(message);
                break;

            case MessageType.GetStickerPacks:
                await HandleGetStickerPacksAsync();
                break;
            case MessageType.GetStickerPackContent:
                await HandleGetStickerPackContentAsync(message);
                break;
            case MessageType.GetSticker:
                await HandleGetStickerAsync(message);
                break;
            case MessageType.SearchHistory:
                await HandleSearchHistoryAsync(message);
                break;
            case MessageType.GetHistoryAroundId:
                await HandleGetHistoryAroundIdAsync(message);
                break;
            case MessageType.DeleteChat:
                await HandleDeleteChatAsync(message);
                break;
            case MessageType.LeaveGroup:
                await HandleLeaveGroupAsync(message);
                break;
            case MessageType.MessagesRead:
                await HandleMessagesReadAsync(message);
                break;
        }
    }
    private async Task HandleUpdateGroupProfileAsync(ProtocolMessage message)
    {
        if (_currentUser == null) return;

        int groupId = int.Parse(message.Parameters["groupId"]);
        string newName = message.Parameters["name"];
        var currentGroup = _db.GetGroupDetails(groupId);
        if (currentGroup == null) return;
        if (currentGroup.CreatorId != _currentUser.Id)
        {
            await SendResponseAsync(MessageType.Error, "Only creator can edit group.", null);
            return;
        }
        if (!currentGroup.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))
        {
            if (newName.Equals("Global Chat", StringComparison.OrdinalIgnoreCase))
            {
                await SendResponseAsync(MessageType.Error, "Name 'Global Chat' is reserved.", null);
                return;
            }
            if (_db.GetUserByUsername(newName) != null)
            {
                await SendResponseAsync(MessageType.Error, $"Name '{newName}' is taken by a user.", null);
                return;
            }
            if (_db.IsGroupNameTaken(newName))
            {
                await SendResponseAsync(MessageType.Error, $"Group named '{newName}' already exists.", null);
                return;
            }
        }
        string? newAvatar = message.Data;
        var creatorId = _db.GetGroupCreatorId(groupId);
        if (creatorId != _currentUser.Id) return;

        if (newAvatar == null)
        {
            currentGroup = _db.GetGroupDetails(groupId);
            newAvatar = currentGroup?.AvatarData;
        }
        else if (newAvatar == "")
        {
            newAvatar = null;
        }

        if (_db.UpdateGroupProfile(groupId, newName, newAvatar))
        {
            var memberIds = _db.GetGroupMemberIds(groupId);

            var payload = new ProtocolMessage
            {
                Type = MessageType.GroupProfileUpdated,
                Data = newAvatar ?? "",
                Parameters = new Dictionary<string, string>
                {
                    { "groupId", groupId.ToString() },
                    { "name", newName }
                }
            };

            await _server.SendToGroupMembersAsync(memberIds, payload);
        }
    }
    private async Task HandleGetUsersAsync()
    {
        if (_currentUser == null) return;
        var globalUsers = _db.GetAllUsers();
        var myContacts = _db.GetContactList(_currentUser.Id);
        var finalUserList = globalUsers
            .Concat(myContacts)
            .GroupBy(u => u.Id)
            .Select(g => g.First())
            .ToList();
        var usersJson = JsonSerializer.Serialize(finalUserList.Select(u => new
        {
            u.Id,
            u.Username,
            DisplayName = u.DisplayName,
            u.Bio,
            u.AvatarColor,
            u.AvatarData, 
            u.CreatedAt,
            IsDeleted = u.IsDeleted
        }));

        await SendResponseAsync(MessageType.UsersList, usersJson, null);
    }

    private async Task HandleLoginAsync(ProtocolMessage message)
    {
        if (message.Parameters == null) return;

        var username = message.Parameters.GetValueOrDefault("username");
        var password = message.Parameters.GetValueOrDefault("password");

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            await SendResponseAsync(MessageType.LoginResponse, "Invalid credentials", new Dictionary<string, string> { { "success", "false" } });
            return;
        }

        var user = _authService.Authenticate(username, password);
        if (user != null)
        {
            Console.WriteLine($"Login successful: {username}");
            _currentUser = user;
            await SendResponseAsync(MessageType.LoginResponse, "Login successful", new Dictionary<string, string>
            {
                { "success", "true" },
                { "userId", user.Id.ToString() },
                { "username", user.Username },
                { "displayName", user.DisplayName ?? user.Username },
                { "bio", user.Bio },
                { "color", user.AvatarColor },
                { "avatar", user.AvatarData ?? "" }
            });
        }
        else
        {
            await SendResponseAsync(MessageType.LoginResponse, "Invalid credentials", new Dictionary<string, string> { { "success", "false" } });
        }
    }

    private async Task HandleChangePasswordAsync(ProtocolMessage message)
    {
        if (_currentUser == null || message.Parameters == null) return;

        var oldPass = message.Parameters.GetValueOrDefault("oldPassword");
        var newPass = message.Parameters.GetValueOrDefault("newPassword");

        if (string.IsNullOrEmpty(oldPass) || string.IsNullOrEmpty(newPass))
        {
            await SendResponseAsync(MessageType.ChangePasswordResponse, "Invalid data", new Dictionary<string, string> { { "success", "false" } });
            return;
        }

        bool success = _authService.ChangePassword(_currentUser, oldPass, newPass);

        if (success)
        {
            await SendResponseAsync(MessageType.ChangePasswordResponse, "Password changed", new Dictionary<string, string> { { "success", "true" } });
        }
        else
        {
            await SendResponseAsync(MessageType.ChangePasswordResponse, "Incorrect old password", new Dictionary<string, string> { { "success", "false" } });
        }
    }


    private async Task HandleUpdateProfileAsync(ProtocolMessage message)
    {
        if (_currentUser == null || message.Parameters == null) return;

        var newDisplayName = message.Parameters.GetValueOrDefault("username");
        var newBio = message.Parameters.GetValueOrDefault("bio") ?? "";
        var newColor = message.Parameters.GetValueOrDefault("color") ?? "#0088CC";
        var newAvatar = message.Data;

        if (string.IsNullOrEmpty(newDisplayName)) return;

        if (_db.UpdateUserProfile(_currentUser.Id, newDisplayName, newBio, newColor, newAvatar))
        {
            _currentUser.DisplayName = newDisplayName;
            _currentUser.Bio = newBio;
            _currentUser.AvatarColor = newColor;
            _currentUser.AvatarData = newAvatar;

            await SendResponseAsync(MessageType.ProfileUpdated, "Success", new Dictionary<string, string>
            {
                { "username", newDisplayName },
                { "bio", newBio },
                { "color", newColor }
            });

            _server.BroadcastUserProfileUpdate(_currentUser.Id, newDisplayName, _currentUser.Username, newAvatar);
        }
        else
        {
            await SendResponseAsync(MessageType.Error, "Error updating profile", null);
        }
    }

    private async Task HandleRegisterAsync(ProtocolMessage message)
    {
        if (message.Parameters == null) return;
        var username = message.Parameters.GetValueOrDefault("username");
        var password = message.Parameters.GetValueOrDefault("password");
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            await SendResponseAsync(MessageType.RegisterResponse, "Invalid", new Dictionary<string, string> { { "success", "false" } });
            return;
        }
        var user = _authService.Register(username, password);
        if (user != null)
        {
            Console.WriteLine($"Registration successful: {username}");
            _currentUser = user;
            await SendResponseAsync(MessageType.RegisterResponse, "Success", new Dictionary<string, string>
        { { "success", "true" }, { "userId", user.Id.ToString() }, { "username", user.Username } });

            _server.BroadcastSystemMessage($"{user.Username} joined the chat");
            var newUserModel = new
            {
                Id = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName ?? user.Username,
                Bio = user.Bio ?? "",
                AvatarColor = user.AvatarColor ?? "#0088CC",
                AvatarData = user.AvatarData ?? ""
            };

            var json = JsonSerializer.Serialize(newUserModel);

            var broadcastMsg = new ProtocolMessage
            {
                Type = MessageType.NewUserRegistered,
                Data = json
            };
            _server.BroadcastToAll(broadcastMsg);
            var registrationTime = DateTime.UtcNow;

            var systemMsg = new ProtocolMessage
            {
                Type = MessageType.MessageReceived,
                Data = $"{user.Username} registered at {registrationTime:yyyy-MM-dd HH:mm:ss} UTC",
                Parameters = new Dictionary<string, string>
                {
                    { "sender", "System" },
                    { "timestamp", registrationTime.ToString("O") },
                    { "isSystem", "true" },
                    { "isRegistration", "true" }
                }
            };
            _server.BroadcastToAll(systemMsg);
        }
        else
        {
            await SendResponseAsync(MessageType.RegisterResponse, "Username exists", new Dictionary<string, string> { { "success", "false" } });
        }
    }

    private async Task HandleSendMessageAsync(ProtocolMessage message)
    {
        if (_currentUser == null || string.IsNullOrEmpty(message.Data)) return;
        int? replyToId = null;
        if (message.Parameters != null && message.Parameters.ContainsKey("replyToId"))
        {
            if (int.TryParse(message.Parameters["replyToId"], out int rid)) replyToId = rid;
        }
        var messageId = _db.CreateMessage(_currentUser.Id, _currentUser.Username, message.Data, null, replyToId);
        _server.BroadcastMessage(message.Data, _currentUser.Username, messageId, replyToId);
    }

    private async Task HandleEditMessageAsync(ProtocolMessage message)
    {
        try
        {
            if (_currentUser == null || message.Parameters == null) return;
            if (!int.TryParse(message.Parameters.GetValueOrDefault("messageId"), out var messageId)) return;
            var newContent = message.Data ?? "";
            var isPrivate = message.Parameters.ContainsKey("isPrivate");
            if (isPrivate)
            {
                var recipientId = _db.GetPrivateMessageRecipientId(messageId);
                var senderId = _db.GetPrivateMessageSenderId(messageId);
                if (recipientId.HasValue && senderId.HasValue)
                {
                    if (_db.UpdatePrivateMessage(messageId, newContent))
                        await NotifyPrivateChatUpdate(messageId, recipientId.Value, senderId.Value, "edit", newContent);
                }
            }
            else
            {
                if (_db.UpdateMessage(messageId, newContent)) _server.BroadcastEdit(messageId, newContent);
            }
        }
        catch (Exception ex) { Console.WriteLine($"Error edit: {ex.Message}"); }
    }

    private async Task HandleDeleteMessageAsync(ProtocolMessage message)
    {
        try
        {
            if (_currentUser == null || message.Parameters == null) return;
            if (!int.TryParse(message.Parameters.GetValueOrDefault("messageId"), out var messageId)) return;
            var isPrivate = message.Parameters.ContainsKey("isPrivate");
            if (isPrivate)
            {
                var recipientId = _db.GetPrivateMessageRecipientId(messageId);
                var senderId = _db.GetPrivateMessageSenderId(messageId);
                if (recipientId.HasValue && senderId.HasValue)
                {
                    if ((senderId.Value == _currentUser.Id || recipientId.Value == _currentUser.Id) &&
                        _db.DeletePrivateMessage(messageId))
                    {
                        await NotifyPrivateChatUpdate(messageId, recipientId.Value, senderId.Value, "delete", null);
                    }
                }
            }
            else
            {
                if (_db.DeleteMessage(messageId)) _server.BroadcastDelete(messageId);
            }
        }
        catch (Exception ex) { Console.WriteLine($"Error delete: {ex.Message}"); }
    }

    private async Task HandleGetHistoryAsync(ProtocolMessage message = null)
    {
        int? beforeId = null;
        if (message != null && message.Parameters != null &&
            message.Parameters.ContainsKey("beforeId") &&
            int.TryParse(message.Parameters["beforeId"], out int bid))
        {
            beforeId = bid;
        }
        var messages = _db.GetMessages(20, beforeId);
        var historyDtos = messages.Select(m => new
        {
            m.Id,
            m.SenderUsername,
            m.Content,
            m.SentAt,
            m.EditedAt,
            m.IsDeleted,
            m.BlobId,
            m.ReplyToId,
            m.ReplyToSender,
            m.ReplyToContent,
            IsRead = m.IsRead 
        });
        var historyData = JsonSerializer.Serialize(historyDtos);

        await SendResponseAsync(MessageType.HistoryResponse, historyData, null);
    }

    private async Task HandleSendPrivateMessageAsync(ProtocolMessage message)
    {
        if (_currentUser == null || message.Parameters == null) return;
        var targetUsername = message.Parameters.GetValueOrDefault("targetUsername");
        var content = message.Data;
        if (string.IsNullOrEmpty(targetUsername) || string.IsNullOrEmpty(content)) return;
        var targetUser = _db.GetUserByUsername(targetUsername);
        if (targetUser == null) return;
        int? replyToId = null;
        if (message.Parameters != null && message.Parameters.ContainsKey("replyToId"))
        {
            if (int.TryParse(message.Parameters["replyToId"], out int rid)) replyToId = rid;
        }
        var msgId = _db.CreatePrivateMessage(_currentUser.Id, targetUser.Id, content, null, replyToId);
        var payload = new ProtocolMessage
        {
            Type = MessageType.PrivateMessageReceived,
            Data = content,
            Parameters = new Dictionary<string, string> { { "sender", _currentUser.Username }, { "timestamp", DateTime.UtcNow.ToString("O") }, { "messageId", msgId.ToString() } }
        };
        if (replyToId.HasValue) payload.Parameters.Add("replyToId", replyToId.Value.ToString());
        await _server.SendPrivateMessageToUserAsync(targetUsername, payload);
        await _server.SendPrivateMessageToUserAsync(_currentUser.Username, payload);
    }

    private async Task HandleGetPrivateHistoryAsync(ProtocolMessage message)
    {
        if (_currentUser == null || message.Parameters == null) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var otherUsername = message.Parameters.GetValueOrDefault("otherUsername");
        if (string.IsNullOrEmpty(otherUsername)) return;
        var otherUser = _db.GetUserByUsername(otherUsername);
        if (otherUser == null) return;
        int? beforeId = null;
        if (message.Parameters.ContainsKey("beforeId") && int.TryParse(message.Parameters["beforeId"], out int bid))
        {
            Console.WriteLine($"[SERVER] Loading history before ID: {bid}");
            beforeId = bid;
        }
        var history = _db.GetPrivateMessages(_currentUser.Id, otherUser.Id, 20, beforeId);
        var timeDb = sw.Elapsed.TotalMilliseconds;
        var historyDtos = history.Select(h => new
        {
            h.Id,
            h.Content,
            h.SentAt,
            EditedAt = h.EditedAt,
            h.BlobId,
            SenderUsername = (h.SenderId == _currentUser.Id) ? _currentUser.Username : otherUsername,
            IsOwnMessage = (h.SenderId == _currentUser.Id),
            ReplyToId = h.ReplyToId,
            ReplyToSender = h.ReplyToSender,
            IsRead = h.IsRead,
            ReplyToContent = h.ReplyToContent
        });
        var json = JsonSerializer.Serialize(historyDtos);
        var timeJson = sw.Elapsed.TotalMilliseconds;
        await SendResponseAsync(MessageType.PrivateHistoryResponse, json, new Dictionary<string, string> { { "otherUsername", otherUsername } });
        var timeSend = sw.Elapsed.TotalMilliseconds;
    }

    private async Task HandleEditPrivateMessageAsync(ProtocolMessage message)
    {
        if (_currentUser == null || message.Parameters == null) return;
        if (!int.TryParse(message.Parameters.GetValueOrDefault("messageId"), out var messageId)) return;
        var newContent = message.Data ?? "";
        var recipientId = _db.GetPrivateMessageRecipientId(messageId);
        var senderId = _db.GetPrivateMessageSenderId(messageId);
        if (recipientId.HasValue && senderId.HasValue)
        {
            if (_db.UpdatePrivateMessage(messageId, newContent))
                await NotifyPrivateChatUpdate(messageId, recipientId.Value, senderId.Value, "edit", newContent);
        }
    }

    private async Task HandleDeletePrivateMessageAsync(ProtocolMessage message)
    {
        if (_currentUser == null || message.Parameters == null) return;
        if (!int.TryParse(message.Parameters.GetValueOrDefault("messageId"), out var messageId)) return;
        var recipientId = _db.GetPrivateMessageRecipientId(messageId);
        var senderId = _db.GetPrivateMessageSenderId(messageId);
        if (recipientId.HasValue && senderId.HasValue)
        {
            if (_db.DeletePrivateMessage(messageId))
                await NotifyPrivateChatUpdate(messageId, recipientId.Value, senderId.Value, "delete", null);
        }
    }

    private async Task NotifyPrivateChatUpdate(int messageId, int recipientId, int senderId, string action, string? content)
    {
        try
        {
            var recipientName = _db.GetUsernameById(recipientId);
            var senderName = _db.GetUsernameById(senderId);

            var updateMsg = new ProtocolMessage
            {
                Type = MessageType.PrivateMessageReceived,
                Data = content ?? "",
                Parameters = new Dictionary<string, string>
            {
                { "action", action },
                { "messageId", messageId.ToString() },
                { "sender", senderName ?? "System" },
                { "timestamp", DateTime.UtcNow.ToString("O") },
                { "isPrivate", "true" }
            }
            };

            if (recipientName != null) await _server.SendPrivateMessageToUserAsync(recipientName, updateMsg);
            if (senderName != null) await _server.SendPrivateMessageToUserAsync(senderName, updateMsg);
        }
        catch { }
    }

    private async Task HandleScheduleMessageAsync(ProtocolMessage message)
    {
        if (_currentUser == null || message.Parameters == null) return;

        var content = message.Data ?? "";
        var scheduledAtStr = message.Parameters.GetValueOrDefault("scheduledAt");
        var targetUsername = message.Parameters.GetValueOrDefault("targetUsername");

        if (string.IsNullOrEmpty(scheduledAtStr) || !DateTime.TryParse(scheduledAtStr, out var scheduledAt))
        {
            await SendResponseAsync(MessageType.Error, "Invalid scheduled time", null);
            return;
        }

        var isPrivate = !string.IsNullOrEmpty(targetUsername);
        var messageId = _db.CreateScheduledMessage(_currentUser.Id, _currentUser.Username, targetUsername, content, scheduledAt, isPrivate);

        await SendResponseAsync(MessageType.ScheduleMessage, "Scheduled", new Dictionary<string, string>
        {
            { "messageId", messageId.ToString() },
            { "scheduledAt", scheduledAt.ToString("O") }
        });
    }

    private async Task HandleGetScheduledMessagesAsync(ProtocolMessage message)
    {
        if (_currentUser == null) return;

        var targetUsername = message.Parameters?.GetValueOrDefault("targetUsername");
        if (string.IsNullOrEmpty(targetUsername))
        {
            targetUsername = null;
        }
        var scheduledMessages = _db.GetScheduledMessages(_currentUser.Id, targetUsername);

        var messagesJson = JsonSerializer.Serialize(scheduledMessages.Select(m => new
        {
            Id = m.Id,
            Content = m.Content,
            ScheduledAt = m.ScheduledAt,
            SenderUsername = m.SenderUsername,
            TargetUsername = m.TargetUsername,
            IsPrivate = m.IsPrivate
        }));

        await SendResponseAsync(MessageType.ScheduledMessagesList, messagesJson, new Dictionary<string, string>
        {
            { "targetUsername", targetUsername ?? "" }
        });
    }

    private async Task HandleUpdateScheduledMessageAsync(ProtocolMessage message)
    {
        if (_currentUser == null || message.Parameters == null) return;

        if (!int.TryParse(message.Parameters.GetValueOrDefault("messageId"), out var messageId))
        {
            await SendResponseAsync(MessageType.Error, "Invalid message ID", null);
            return;
        }

        var scheduledMsg = _db.GetScheduledMessageById(messageId);
        if (scheduledMsg == null || scheduledMsg.SenderId != _currentUser.Id)
        {
            await SendResponseAsync(MessageType.Error, "Message not found or access denied", null);
            return;
        }

        var content = message.Data ?? "";
        var scheduledAtStr = message.Parameters.GetValueOrDefault("scheduledAt");

        if (string.IsNullOrEmpty(scheduledAtStr) || !DateTime.TryParse(scheduledAtStr, out var scheduledAt))
        {
            await SendResponseAsync(MessageType.Error, "Invalid scheduled time", null);
            return;
        }

        if (_db.UpdateScheduledMessage(messageId, content, scheduledAt))
        {
            await SendResponseAsync(MessageType.UpdateScheduledMessage, "Updated", new Dictionary<string, string>
            {
                { "messageId", messageId.ToString() }
            });
        }
        else
        {
            await SendResponseAsync(MessageType.Error, "Failed to update", null);
        }
    }

    private async Task HandleDeleteScheduledMessageAsync(ProtocolMessage message)
    {
        if (_currentUser == null || message.Parameters == null) return;

        if (!int.TryParse(message.Parameters.GetValueOrDefault("messageId"), out var messageId))
        {
            await SendResponseAsync(MessageType.Error, "Invalid message ID", null);
            return;
        }

        var scheduledMsg = _db.GetScheduledMessageById(messageId);
        if (scheduledMsg == null || scheduledMsg.SenderId != _currentUser.Id)
        {
            await SendResponseAsync(MessageType.Error, "Message not found or access denied", null);
            return;
        }

        if (_db.DeleteScheduledMessage(messageId))
        {
            await SendResponseAsync(MessageType.DeleteScheduledMessage, "Deleted", new Dictionary<string, string>
            {
                { "messageId", messageId.ToString() }
            });
        }
        else
        {
            await SendResponseAsync(MessageType.Error, "Failed to delete", null);
        }
    }

    private async Task HandleDeleteAccountAsync()
    {
        if (_currentUser == null)
        {
            await SendResponseAsync(MessageType.DeleteAccountResponse, "Not logged in", null);
            return;
        }

        try
        {
            var userId = _currentUser.Id;
            var newSystemName = _db.SoftDeleteUser(userId);
            if (newSystemName != null)
            {
                await SendResponseAsync(MessageType.DeleteAccountResponse, "Deleted", new Dictionary<string, string> { { "success", "true" } });
                _server.BroadcastUserProfileUpdate(userId, "Deleted Account", newSystemName, "", true);
                _currentUser = null;
                await Task.Delay(200);
                try { _client.Close(); } catch { }
            }
            else
            {
                await SendResponseAsync(MessageType.DeleteAccountResponse, "DB Error", null);
            }
        }
        catch (Exception ex) { Console.WriteLine(ex.Message); }
    }

    public async Task SendMessageAsync(ProtocolMessage message)
    {
        if (!_client.Connected || !_stream.CanWrite) return;
        await _writeLock.WaitAsync();
        try
        {
            byte[] bytes;
            try
            {
                bytes = message.ToBytes();
            }
            catch
            {
                return;
            }

            var lengthBytes = BitConverter.GetBytes(bytes.Length);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

            await _stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, cts.Token);
            await _stream.WriteAsync(bytes, 0, bytes.Length, cts.Token);
            await _stream.FlushAsync(cts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending message to client: {ex.Message}");
        }
        finally
        {
            _writeLock.Release();
        }
    }
    private async Task SendResponseAsync(MessageType type, string? data, Dictionary<string, string>? parameters)
    {
        var response = new ProtocolMessage { Type = type, Data = data, Parameters = parameters };
        await SendMessageAsync(response);
    }

    private async Task HandleGetGifListAsync()
    {
        Console.WriteLine("HandleGetGifListAsync called");
        try
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

            var gifsPath = Path.Combine(projectRoot ?? "", "Database", "gifs");
            var fullPath = Path.GetFullPath(gifsPath);

            Console.WriteLine($"Looking for GIFs in: {fullPath}");
            Console.WriteLine($"Directory exists: {Directory.Exists(fullPath)}");

            if (!Directory.Exists(fullPath))
            {
                Console.WriteLine($"GIFs directory not found at: {fullPath}");
                await SendResponseAsync(MessageType.GifListResponse, "[]", null);
                return;
            }

            var gifFiles = Directory.GetFiles(fullPath, "*.gif", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(f => f != null)
                .Cast<string>()
                .ToList();

            Console.WriteLine($"Found {gifFiles.Count} GIF files:");
            foreach (var file in gifFiles)
            {
                Console.WriteLine($"  - {file}");
            }

            var gifListJson = JsonSerializer.Serialize(gifFiles);
            Console.WriteLine($"Sending GIF list response: {gifListJson}");
            await SendResponseAsync(MessageType.GifListResponse, gifListJson, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting GIF list: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            await SendResponseAsync(MessageType.GifListResponse, "[]", null);
        }
    }

    private async Task HandleSendGifAsync(ProtocolMessage message)
    {
        if (_currentUser == null || string.IsNullOrEmpty(message.Data)) return;
        var gifFilename = message.Data;
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
        var gifsPath = Path.Combine(projectRoot ?? "", "Database", "gifs");
        var gifPath = Path.Combine(gifsPath, gifFilename);
        if (!File.Exists(gifPath))
        {
            await SendResponseAsync(MessageType.Error, "GIF not found", null);
            return;
        }
        var gifBytes = await File.ReadAllBytesAsync(gifPath);
        var gifBase64 = Convert.ToBase64String(gifBytes);
        int? replyToId = null;
        if (message.Parameters != null && message.Parameters.ContainsKey("replyToId"))
        {
            if (int.TryParse(message.Parameters["replyToId"], out int rid)) replyToId = rid;
        }
        var parameters = new Dictionary<string, string>
    {
        { "sender", _currentUser.Username },
        { "filename", gifFilename },
        { "timestamp", DateTime.UtcNow.ToString("O") }
    };
        if (replyToId.HasValue)
        {
            parameters.Add("replyToId", replyToId.Value.ToString());
        }
        var groupIdStr = message.Parameters?.GetValueOrDefault("groupId");

        if (!string.IsNullOrEmpty(groupIdStr) && int.TryParse(groupIdStr, out int groupId))
        {
            var msgId = _db.CreateGroupMessage(groupId, _currentUser.Id, $"<GIF:{gifFilename}>", null, replyToId);

            parameters.Add("groupId", groupId.ToString());
            parameters.Add("messageId", msgId.ToString());

            var payload = new ProtocolMessage
            {
                Type = MessageType.GroupMessageReceived,
                Data = $"<GIF:{gifFilename}>",
                Parameters = parameters
            };
            var memberIds = _db.GetGroupMemberIds(groupId);
            await _server.SendToGroupMembersAsync(memberIds, payload);
        }
        else if (message.Parameters != null && message.Parameters.ContainsKey("targetUsername"))
        {
            var targetUsername = message.Parameters["targetUsername"];
            var targetUser = _db.GetUserByUsername(targetUsername);
            if (targetUser == null) return;
            var msgId = _db.CreatePrivateMessage(_currentUser.Id, targetUser.Id, $"<GIF:{gifFilename}>", null, replyToId);
            parameters.Add("messageId", msgId.ToString());
            parameters.Add("targetUsername", targetUsername);
            var payload = new ProtocolMessage
            {
                Type = MessageType.GifReceived,
                Data = gifBase64,
                Parameters = parameters
            };
            await _server.SendPrivateMessageToUserAsync(targetUsername, payload);
            await _server.SendPrivateMessageToUserAsync(_currentUser.Username, payload);
        }
        else
        {
            var messageId = _db.CreateMessage(_currentUser.Id, _currentUser.Username, $"<GIF:{gifFilename}>", null, replyToId);
            parameters.Add("messageId", messageId.ToString());
            var payload = new ProtocolMessage
            {
                Type = MessageType.GifReceived,
                Data = gifBase64,
                Parameters = parameters
            };
            _server.BroadcastGifMessage(payload);
        }
    }
    private async Task HandleSendVoiceMessageAsync(ProtocolMessage message)
    {
        if (_currentUser == null || string.IsNullOrEmpty(message.Data)) return;

        var targetUsername = message.Parameters?.GetValueOrDefault("targetUsername");
        var groupIdStr = message.Parameters?.GetValueOrDefault("groupId");
        int? replyToId = null;
        if (message.Parameters != null && message.Parameters.ContainsKey("replyToId"))
        {
            if (int.TryParse(message.Parameters["replyToId"], out int rid)) replyToId = rid;
        }
        var content = $"<VOICE:{message.Data}>";
        var responseParams = new Dictionary<string, string>
    {
        { "sender", _currentUser.Username },
        { "timestamp", DateTime.UtcNow.ToString("O") }
    };
        if (replyToId.HasValue) responseParams.Add("replyToId", replyToId.Value.ToString());

        if (!string.IsNullOrEmpty(groupIdStr) && int.TryParse(groupIdStr, out int groupId))
        {
            var msgId = _db.CreateGroupMessage(groupId, _currentUser.Id, content, null, replyToId);
            responseParams.Add("groupId", groupId.ToString());
            responseParams.Add("messageId", msgId.ToString());

            var payload = new ProtocolMessage
            {
                Type = MessageType.GroupMessageReceived,
                Data = content,
                Parameters = responseParams
            };
            var memberIds = _db.GetGroupMemberIds(groupId);
            await _server.SendToGroupMembersAsync(memberIds, payload);
        }
        else if (!string.IsNullOrEmpty(targetUsername))
        {
            var targetUser = _db.GetUserByUsername(targetUsername);
            if (targetUser == null) return;

            var msgId = _db.CreatePrivateMessage(_currentUser.Id, targetUser.Id, content, null, replyToId);
            responseParams.Add("messageId", msgId.ToString());

            var payload = new ProtocolMessage
            {
                Type = MessageType.VoiceMessageReceived,
                Data = content,
                Parameters = responseParams
            };
            await _server.SendPrivateMessageToUserAsync(targetUsername, payload);
            await _server.SendPrivateMessageToUserAsync(_currentUser.Username, payload);
        }
        else
        {
            var msgId = _db.CreateMessage(_currentUser.Id, _currentUser.Username, content, null, replyToId);
            responseParams.Add("messageId", msgId.ToString());

            var payload = new ProtocolMessage
            {
                Type = MessageType.VoiceMessageReceived,
                Data = content,
                Parameters = responseParams
            };
            _server.BroadcastToAll(payload);
        }
    }
    private async Task HandleGetGifAsync(ProtocolMessage message)
    {
        if (string.IsNullOrEmpty(message.Data)) return;

        var gifFilename = message.Data;
        
        var cachedData = _server.GetCachedGif(gifFilename);
        if (cachedData != null)
        {
            Console.WriteLine($"[GIF CACHE HIT] '{gifFilename}' served from memory");
            await SendResponseAsync(MessageType.GifDataResponse, cachedData, new Dictionary<string, string> { { "filename", gifFilename } });
            return;
        }

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

        var gifsPath = Path.Combine(projectRoot ?? "", "Database", "gifs");
        var gifPath = Path.Combine(gifsPath, gifFilename);

        if (!File.Exists(gifPath))
        {
            Console.WriteLine($"[GIF ERROR] File not found: {gifFilename}");
            await SendResponseAsync(MessageType.Error, "GIF not found", null);
            return;
        }

        var gifBytes = await File.ReadAllBytesAsync(gifPath);
        var gifBase64 = Convert.ToBase64String(gifBytes);
        
        _server.CacheGif(gifFilename, gifBase64);
        
        Console.WriteLine($"[GIF CACHE MISS] '{gifFilename}' loaded from disk and cached. Size: {gifBytes.Length} bytes");
        await SendResponseAsync(MessageType.GifDataResponse, gifBase64, new Dictionary<string, string> { { "filename", gifFilename } });
    }

    private async Task HandleSendMediaAsync(ProtocolMessage message)
    {
        if (_currentUser == null || string.IsNullOrEmpty(message.Data)) return;

        var targetUsername = message.Parameters?.GetValueOrDefault("targetUsername");
        var groupIdStr = message.Parameters?.GetValueOrDefault("groupId");
        var filename = message.Parameters?.GetValueOrDefault("filename") ?? "image.png";
        int? replyToId = null;
        if (message.Parameters != null && message.Parameters.ContainsKey("replyToId"))
        {
            if (int.TryParse(message.Parameters["replyToId"], out int rid)) replyToId = rid;
        }
        var isImage = message.Type == MessageType.SendImage;
        int blobId = _db.SaveFileBlob(message.Data);
        string contentRef;
        if (isImage)
        {
            contentRef = $"<IMG_REF:{blobId}|{filename}>";
        }
        else
        {
            long sizeInBytes = (long)(message.Data.Length * 3.0 / 4.0);
            contentRef = $"<FILE_REF:{blobId}|{filename}|{sizeInBytes}>";
        }
        var responseType = isImage ? MessageType.ImageReceived : MessageType.FileReceived;
        int msgId;
        if (!string.IsNullOrEmpty(groupIdStr) && int.TryParse(groupIdStr, out int groupId))
        {
            msgId = _db.CreateGroupMessage(groupId, _currentUser.Id, contentRef, blobId, replyToId);
            var payload = new ProtocolMessage
            {
                Type = MessageType.GroupMessageReceived,
                Data = contentRef,
                Parameters = new Dictionary<string, string>
            {
                { "groupId", groupId.ToString() },
                { "sender", _currentUser.Username },
                { "timestamp", DateTime.UtcNow.ToString("O") },
                { "messageId", msgId.ToString() },
                { "filename", filename }
            }
            };
            var memberIds = _db.GetGroupMemberIds(groupId);
            await _server.SendToGroupMembersAsync(memberIds, payload);
        }
        else if (!string.IsNullOrEmpty(targetUsername))
        {
            var targetUser = _db.GetUserByUsername(targetUsername);
            if (targetUser == null) return;
            msgId = _db.CreatePrivateMessage(_currentUser.Id, targetUser.Id, contentRef, blobId, replyToId);

            var payload = new ProtocolMessage
            {
                Type = responseType,
                Data = contentRef,
                Parameters = new Dictionary<string, string>
            {
                { "sender", _currentUser.Username },
                { "timestamp", DateTime.UtcNow.ToString("O") },
                { "messageId", msgId.ToString() },
                { "filename", filename },
                { "targetUsername", targetUsername }
            }
            };
            await _server.SendPrivateMessageToUserAsync(targetUsername, payload);
            await _server.SendPrivateMessageToUserAsync(_currentUser.Username, payload);
        }
        else
        {
            msgId = _db.CreateMessage(_currentUser.Id, _currentUser.Username, contentRef, blobId, replyToId);
            var payload = new ProtocolMessage
            {
                Type = responseType,
                Data = contentRef,
                Parameters = new Dictionary<string, string>
            {
                { "sender", _currentUser.Username },
                { "timestamp", DateTime.UtcNow.ToString("O") },
                { "messageId", msgId.ToString() },
                { "filename", filename }
            }
            };
            _server.BroadcastToAll(payload);
        }
    }

    private async Task HandleGetFileContentAsync(ProtocolMessage message)
    {
        if (_currentUser == null || string.IsNullOrEmpty(message.Data)) return;

        if (int.TryParse(message.Data, out int blobId))
        {
            Console.WriteLine($"User {_currentUser.Username} requesting file blob {blobId}");
            string? base64Data = _db.GetFileBlob(blobId);

            if (base64Data != null)
            {
                await SendResponseAsync(MessageType.FileContentResponse, base64Data, new Dictionary<string, string>
            {
                { "blobId", blobId.ToString() }
            });
            }
            else
            {
                await SendResponseAsync(MessageType.Error, "File not found on server", null);
            }
        }
    }

    private async Task HandleCreateGroupAsync(ProtocolMessage message)
    {
        if (_currentUser == null) return;
        var groupName = message.Parameters?.GetValueOrDefault("name");
        var memberIds = JsonSerializer.Deserialize<List<int>>(message.Data ?? "[]");
        if (string.IsNullOrEmpty(groupName)) return;
        if (groupName.Equals("Global Chat", StringComparison.OrdinalIgnoreCase))
        {
            await SendResponseAsync(MessageType.Error, "Name 'Global Chat' is reserved.", null);
            return;
        }
        var existingUser = _db.GetUserByUsername(groupName);
        if (existingUser != null)
        {
            await SendResponseAsync(MessageType.Error, $"Name '{groupName}' is already taken by a user.", null);
            return;
        }
        if (_db.IsGroupNameTaken(groupName))
        {
            await SendResponseAsync(MessageType.Error, $"Group named '{groupName}' already exists.", null);
            return;
        }
        int groupId = _db.CreateGroup(groupName, _currentUser.Id, memberIds ?? new List<int>());
        if (groupId > 0)
        {
            var allMembers = _db.GetGroupMemberIds(groupId);
            var responseMsg = new ProtocolMessage
            {
                Type = MessageType.GroupCreated,
                Data = groupId.ToString(),
                Parameters = new Dictionary<string, string> { { "name", groupName } }
            };
            await _server.SendToGroupMembersAsync(allMembers, responseMsg);
        }
    }

    private async Task HandleSendGroupMessageAsync(ProtocolMessage message)
    {
        if (_currentUser == null) return;

        int groupId = int.Parse(message.Parameters["groupId"]);
        string content = message.Data ?? "";
        int? replyToId = null;
        if (message.Parameters != null && message.Parameters.ContainsKey("replyToId"))
        {
            if (int.TryParse(message.Parameters["replyToId"], out int rid)) replyToId = rid;
        }
        int msgId = _db.CreateGroupMessage(groupId, _currentUser.Id, content, null, replyToId);
        var memberIds = _db.GetGroupMemberIds(groupId);
        var payload = new ProtocolMessage
        {
            Type = MessageType.GroupMessageReceived,
            Data = content,
            Parameters = new Dictionary<string, string>
        {
            { "groupId", groupId.ToString() },
            { "sender", _currentUser.Username },
            { "messageId", msgId.ToString() },
            { "timestamp", DateTime.UtcNow.ToString("O") }
        }
        };
        if (replyToId.HasValue) payload.Parameters.Add("replyToId", replyToId.Value.ToString());
        await _server.SendToGroupMembersAsync(memberIds, payload);
    }

    private async Task HandleGetGroupsAsync()
    {
        if (_currentUser == null) return;

        var groups = _db.GetGroupsForUser(_currentUser.Id);

        var groupList = groups.Select(g => new UserItemModel
        {
            Id = g.Id,
            Username = g.Name,
            IsGroup = true,
            GroupId = g.Id,
            CreatorId = g.CreatorId,
            AvatarData = g.AvatarData,

            AvatarColor = "#4CAF50",
        }).ToList();

        var json = JsonSerializer.Serialize(groupList);

        await SendResponseAsync(MessageType.GroupsList, json, null);
    }

    private async Task HandleGetGroupHistoryAsync(ProtocolMessage message)
    {
        if (_currentUser == null) return;
        if (!int.TryParse(message.Parameters?.GetValueOrDefault("groupId"), out int groupId)) return;
        var memberIds = _db.GetGroupMemberIds(groupId);
        if (!memberIds.Contains(_currentUser.Id))
        {
            await SendResponseAsync(MessageType.Error, "Access denied", null);
            return;
        }
        int? beforeId = null;
        if (message.Parameters.ContainsKey("beforeId") && int.TryParse(message.Parameters["beforeId"], out int bid))
        {
            Console.WriteLine($"[SERVER] Loading history before ID: {bid}");
            beforeId = bid;
        }
        var messages = _db.GetGroupMessages(groupId);
        var historyDtos = messages.Select(m => new
        {
            Id = m.Id,
            SenderUsername = m.SenderUsername,
            Content = m.Content,
            SentAt = m.SentAt,
            EditedAt = m.EditedAt,
            IsOwnMessage = m.SenderUsername == _currentUser.Username,
            BlobId = m.BlobId,
            ReplyToId = m.ReplyToId,
            ReplyToSender = m.ReplyToSender,
            ReplyToContent = m.ReplyToContent,
            IsRead = m.IsRead
        });
        var json = JsonSerializer.Serialize(historyDtos);
        await SendResponseAsync(MessageType.GroupHistoryResponse, json, new Dictionary<string, string> { { "groupId", groupId.ToString() } });
    }

    private async Task HandleGetGroupDetailsAsync(ProtocolMessage message)
    {
        if (!int.TryParse(message.Data, out int groupId)) return;
        var details = _db.GetGroupDetails(groupId);
        if (details != null)
        {
            var json = JsonSerializer.Serialize(details);
            await SendResponseAsync(MessageType.GroupDetailsResponse, json, null);
        }
    }

    private async Task HandleAddGroupMemberAsync(ProtocolMessage message)
    {
        if (_currentUser == null) return;
        if (!int.TryParse(message.Parameters?["groupId"], out int groupId)) return;
        var targetUsername = message.Data;
        var group = _db.GetGroupDetails(groupId);
        if (group?.CreatorId != _currentUser.Id) return;
        if (_db.AddMemberToGroup(groupId, targetUsername))
        {
            await HandleGetGroupDetailsAsync(new ProtocolMessage { Data = groupId.ToString() });
            var targetUser = _db.GetUserByUsername(targetUsername);
            if (targetUser != null)
            {
                var notification = new ProtocolMessage
                {
                    Type = MessageType.GroupCreated,
                    Data = groupId.ToString(),
                    Parameters = new Dictionary<string, string> { { "name", group.Name } }
                };
                await _server.SendToGroupMembersAsync(new List<int> { targetUser.Id }, notification);
            }
        }
    }

    private async Task HandleRemoveGroupMemberAsync(ProtocolMessage message)
    {
        if (_currentUser == null) return;
        if (!int.TryParse(message.Parameters?["groupId"], out int groupId)) return;
        if (!int.TryParse(message.Data, out int targetUserId)) return;
        var group = _db.GetGroupDetails(groupId);
        if (group?.CreatorId != _currentUser.Id) return;
        if (_db.RemoveMemberFromGroup(groupId, targetUserId))
        {
            await HandleGetGetGroupDetailsInternal(groupId);
            var kickNotification = new ProtocolMessage
            {
                Type = MessageType.GroupDeleted,
                Data = groupId.ToString()
            };
            await _server.SendToGroupMembersAsync(new List<int> { targetUserId }, kickNotification);
        }
    }

    private async Task HandleGetGetGroupDetailsInternal(int groupId)
    {
        var details = _db.GetGroupDetails(groupId);
        if (details != null)
        {
            var json = JsonSerializer.Serialize(details);
            var currentMembers = _db.GetGroupMemberIds(groupId);
            var msg = new ProtocolMessage { Type = MessageType.GroupDetailsResponse, Data = json };
            await _server.SendToGroupMembersAsync(currentMembers, msg);
        }
    }

    private async Task HandleDeleteGroupAsync(ProtocolMessage message)
    {
        if (_currentUser == null) return;
        if (!int.TryParse(message.Data, out int groupId)) return;
        var group = _db.GetGroupDetails(groupId);
        if (group == null || group.CreatorId != _currentUser.Id) return;
        var members = _db.GetGroupMemberIds(groupId);
        if (_db.DeleteGroup(groupId))
        {
            var notification = new ProtocolMessage
            {
                Type = MessageType.GroupDeleted,
                Data = groupId.ToString()
            };
            await _server.SendToGroupMembersAsync(members, notification);
        }
    }

    private async Task HandleDeleteGroupMessageAsync(ProtocolMessage message)
    {
        if (_currentUser == null || message.Parameters == null) return;

        if (!int.TryParse(message.Parameters.GetValueOrDefault("messageId"), out var messageId)) return;
        if (!int.TryParse(message.Parameters.GetValueOrDefault("groupId"), out var groupId)) return;
        if (_db.DeleteGroupMessage(messageId, _currentUser.Id))
        {
            var memberIds = _db.GetGroupMemberIds(groupId);
            var payload = new ProtocolMessage
            {
                Type = MessageType.GroupMessageReceived,
                Parameters = new Dictionary<string, string>
            {
                { "action", "delete" },
                { "groupId", groupId.ToString() },
                { "messageId", messageId.ToString() },
                { "sender", _currentUser.Username }
            }
            };
            await _server.SendToGroupMembersAsync(memberIds, payload);
        }
    }

    private async Task HandleEditGroupMessageAsync(ProtocolMessage message)
    {
        if (_currentUser == null || message.Parameters == null) return;
        if (!int.TryParse(message.Parameters.GetValueOrDefault("messageId"), out var messageId)) return;
        if (!int.TryParse(message.Parameters.GetValueOrDefault("groupId"), out var groupId)) return;
        var newContent = message.Data ?? "";
        if (_db.UpdateGroupMessage(messageId, _currentUser.Id, newContent))
        {
            var memberIds = _db.GetGroupMemberIds(groupId);

            var payload = new ProtocolMessage
            {
                Type = MessageType.GroupMessageReceived,
                Data = newContent,
                Parameters = new Dictionary<string, string>
            {
                { "action", "edit" },
                { "groupId", groupId.ToString() },
                { "messageId", messageId.ToString() },
                { "sender", _currentUser.Username }
            }
            };

            await _server.SendToGroupMembersAsync(memberIds, payload);
        }
    }

    private async Task HandleGetStickerPacksAsync()
    {
        var stickersRoot = GetStickersRootPath();
        if (!Directory.Exists(stickersRoot)) Directory.CreateDirectory(stickersRoot);
        var packs = new List<object>();
        foreach (var dir in Directory.GetDirectories(stickersRoot))
        {
            var packName = new DirectoryInfo(dir).Name;
            string coverBase64 = "";
            var coverPath = Path.Combine(dir, "cover.png");
            if (File.Exists(coverPath))
            {
                var bytes = await File.ReadAllBytesAsync(coverPath);
                coverBase64 = Convert.ToBase64String(bytes);
            }
            packs.Add(new { Name = packName, Cover = coverBase64 });
        }
        var json = JsonSerializer.Serialize(packs);
        await SendResponseAsync(MessageType.StickerPacksList, json, null);
    }

    private async Task HandleGetStickerPackContentAsync(ProtocolMessage message)
    {
        var packName = message.Data;
        Console.WriteLine($"[SERVER] Request for pack content: '{packName}'");
        var root = GetStickersRootPath();
        var stickersRoot = Path.Combine(root, packName);
        Console.WriteLine($"[SERVER] Looking in path: {stickersRoot}");
        if (!Directory.Exists(stickersRoot))
        {
            Console.WriteLine($"[SERVER] ERROR: Directory not found!");
            return;
        }
        var files = Directory.GetFiles(stickersRoot).Select(Path.GetFileName).Where(f => f != "cover.png" && (f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".webp"))).ToList();
        Console.WriteLine($"[SERVER] Found {files.Count} files in pack.");
        var json = JsonSerializer.Serialize(files);
        await SendResponseAsync(MessageType.StickerPackContent, json, new Dictionary<string, string> { { "packName", packName } });
    }

    private async Task HandleGetStickerAsync(ProtocolMessage message)
    {
        var parts = message.Data?.Split('|');
        if (parts == null || parts.Length != 2) return;
        var packName = parts[0];
        var fileName = parts[1];
        var key = $"{packName}|{fileName}";
        
        var cachedData = _server.GetCachedSticker(key);
        if (cachedData != null)
        {
            Console.WriteLine($"[STICKER CACHE HIT] '{key}' served from memory");
            await SendResponseAsync(MessageType.StickerDataResponse, cachedData, new Dictionary<string, string>
            {
                { "packName", packName },
                { "fileName", fileName }
            });
            return;
        }
        
        var path = Path.Combine(GetStickersRootPath(), packName, fileName);
        if (File.Exists(path))
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var base64 = Convert.ToBase64String(bytes);
            
            _server.CacheSticker(key, base64);
            
            Console.WriteLine($"[STICKER CACHE MISS] '{key}' loaded from disk and cached");
            await SendResponseAsync(MessageType.StickerDataResponse, base64, new Dictionary<string, string>
        {
            { "packName", packName },
            { "fileName", fileName }
        });
        }
    }

    private string GetStickersRootPath()
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

    private async Task HandleSearchHistoryAsync(ProtocolMessage message)
    {
        if (_currentUser == null) return;

        string query = message.Data ?? "";
        string? targetUsername = message.Parameters?.GetValueOrDefault("targetUsername");
        string? groupIdStr = message.Parameters?.GetValueOrDefault("groupId");
        int? groupId = null;
        if (int.TryParse(groupIdStr, out int gid)) groupId = gid;
        var results = _db.SearchMessagesInChat(_currentUser.Id, query, targetUsername, groupId);
        var resultDtos = results.Select(m => new
        {
            m.Id,
            m.Content,
            m.SentAt,
            m.SenderUsername,
            SenderLogin = m.SenderUsername
        });

        var json = JsonSerializer.Serialize(resultDtos);
        await SendResponseAsync(MessageType.SearchHistoryResponse, json, null);
    }

    private async Task HandleGetHistoryAroundIdAsync(ProtocolMessage message)
    {
        if (_currentUser == null) return;
        if (!int.TryParse(message.Data, out int msgId)) return;

        List<Message> history = new();

        if (message.Parameters?.ContainsKey("groupId") == true)
        {
            int gid = int.Parse(message.Parameters["groupId"]);
            history = _db.GetMessagesAroundId(msgId, 100, groupId: gid);
        }
        else if (message.Parameters?.ContainsKey("targetUsername") == true)
        {
            var targetName = message.Parameters["targetUsername"];
            var targetUser = _db.GetUserByUsername(targetName);
            if (targetUser != null)
                history = _db.GetMessagesAroundId(msgId, 100, userId1: _currentUser.Id, userId2: targetUser.Id);
        }
        else
        {
            history = _db.GetMessagesAroundId(msgId, 100);
        }

        var json = JsonSerializer.Serialize(history);
        var responseType = message.Parameters?.ContainsKey("groupId") == true
            ? MessageType.GroupHistoryResponse
            : (message.Parameters?.ContainsKey("targetUsername") == true ? MessageType.PrivateHistoryResponse : MessageType.HistoryResponse);

        await SendResponseAsync(responseType, json, message.Parameters);
    }
    private async Task HandleSearchUsersAsync(ProtocolMessage message)
    {
        if (_currentUser == null || string.IsNullOrEmpty(message.Data)) return;
        var foundUsers = _db.SearchUsers(message.Data);
        var json = JsonSerializer.Serialize(foundUsers.Select(u => new
        {
            u.Id,
            u.Username,
            u.DisplayName,
            u.Bio,
            u.AvatarColor,
            u.AvatarData
        }));
        await SendResponseAsync(MessageType.SearchUsersResponse, json, null);
    }

    private async Task HandleDeleteChatAsync(ProtocolMessage message)
    {
        if (_currentUser == null || message.Parameters == null) return;
        var targetUsername = message.Parameters.GetValueOrDefault("targetUsername");
        var deleteForEveryone = message.Parameters.GetValueOrDefault("forEveryone") == "true";
        if (string.IsNullOrEmpty(targetUsername)) return;
        var targetUser = _db.GetUserByUsername(targetUsername);
        if (targetUser == null) return;
        if (deleteForEveryone)
        {
            _db.DeleteAllPrivateMessages(_currentUser.Id, targetUser.Id);
            var payload = new ProtocolMessage
            {
                Type = MessageType.ChatDeleted,
                Parameters = new Dictionary<string, string> { { "username", _currentUser.Username } }
            };
            await _server.SendPrivateMessageToUserAsync(targetUsername, payload);
        }
        else
        {
            var lastMessages = _db.GetPrivateMessages(_currentUser.Id, targetUser.Id, 1);
            var lastMsg = lastMessages.FirstOrDefault();
            int lastId = lastMsg?.Id ?? int.MaxValue;

            _db.SetPrivateChatCleared(_currentUser.Id, targetUser.Id, lastId);
            _db.TryCleanupGarbageMessages(_currentUser.Id, targetUser.Id);
        }
        await SendResponseAsync(MessageType.ChatDeleted, null, new Dictionary<string, string>
    {
        { "username", targetUsername }
    });
    }

    private async Task HandleLeaveGroupAsync(ProtocolMessage message)
    {
        if (_currentUser == null) return;
        if (!int.TryParse(message.Data, out int groupId)) return;
        if (_db.RemoveMemberFromGroup(groupId, _currentUser.Id))
        {
            var notification = new ProtocolMessage
            {
                Type = MessageType.GroupDeleted,
                Data = groupId.ToString()
            };
            await SendMessageAsync(notification);
        }
    }

    private async Task HandleMessagesReadAsync(ProtocolMessage message)
    {
        if (_currentUser == null) return;
        if (message.Parameters.ContainsKey("isGlobal"))
        {
            _db.MarkGlobalMessagesAsRead(_currentUser.Id);
            var notification = new ProtocolMessage
            {
                Type = MessageType.MessagesRead,
                Parameters = new Dictionary<string, string>
            {
                { "reader", _currentUser.Username },
                { "isGlobal", "true" }
            }
            };
            _server.BroadcastToAll(notification);
        }
        if (message.Parameters.ContainsKey("targetUsername") && message.Parameters.ContainsKey("isPrivate"))
        {
            var senderUsername = message.Parameters["targetUsername"];
            var senderUser = _db.GetUserByUsername(senderUsername);

            if (senderUser != null)
            {
                _db.MarkPrivateMessagesAsRead(_currentUser.Id, senderUser.Id);
                var notification = new ProtocolMessage
                {
                    Type = MessageType.MessagesRead,
                    Parameters = new Dictionary<string, string>
                {
                    { "reader", _currentUser.Username },
                    { "isPrivate", "true" }
                }
                };
                await _server.SendPrivateMessageToUserAsync(senderUsername, notification);
            }
        }
        else if (message.Parameters.ContainsKey("groupId") && int.TryParse(message.Parameters["groupId"], out int groupId))
        {
            _db.MarkGroupMessagesAsRead(groupId, _currentUser.Id);
            var memberIds = _db.GetGroupMemberIds(groupId);

            var notification = new ProtocolMessage
            {
                Type = MessageType.MessagesRead,
                Parameters = new Dictionary<string, string>
            {
                { "reader", _currentUser.Username },
                { "groupId", groupId.ToString() }
            }
            };

            await _server.SendToGroupMembersAsync(memberIds, notification);
        }

    }
}
