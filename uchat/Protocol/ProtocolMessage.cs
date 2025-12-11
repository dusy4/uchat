using System.Text;
using System.Text.Json;

namespace uchat.Protocol;

public enum MessageType
{
    Login,
    Register,
    LoginResponse,
    RegisterResponse,
    SendMessage,
    MessageReceived,
    EditMessage,
    DeleteMessage,
    GetHistory,
    HistoryResponse,
    Error,
    Heartbeat,
    GetUsers,
    UsersList,
    SendPrivateMessage,
    PrivateMessageReceived,
    GetPrivateHistory,
    PrivateHistoryResponse,
    EditPrivateMessage,
    DeletePrivateMessage,
    UpdateProfile,
    ProfileUpdated,
    NewUserRegistered,
    ScheduleMessage,
    GetScheduledMessages,
    ScheduledMessagesList,
    UpdateScheduledMessage,
    DeleteScheduledMessage,
    DeleteAccount,
    DeleteAccountResponse,
    GetGifList,
    GifListResponse,
    SendGif,
    GifReceived,
    GetGif,
    GifDataResponse,
    SendVoiceMessage,
    VoiceMessageReceived,
    SendImage,
    ImageReceived,
    SendFile,
    FileReceived,
    GetFileContent,
    FileContentResponse,
    ChangePassword,
    ChangePasswordResponse,
    CreateGroup,
    GroupCreated,
    GetGroups,
    GroupsList,
    SendGroupMessage,
    GroupMessageReceived,
    GetGroupHistory,
    GroupHistoryResponse,
    GetGroupDetails,
    GroupDetailsResponse,
    AddGroupMember,
    RemoveGroupMember,
    GroupMemberAdded,
    GroupMemberRemoved,
    DeleteGroup,
    GroupDeleted,
    DeleteGroupMessage,
    EditGroupMessage,
    UpdateGroupProfile,
    GroupProfileUpdated,
    GetStickerPacks,
    StickerPacksList,
    GetStickerPackContent,
    StickerPackContent,
    GetSticker,
    StickerDataResponse,
    SearchHistory,
    SearchHistoryResponse,
    GetHistoryAroundId,
    SearchUsers,
    SearchUsersResponse,
    DeleteChat,
    LeaveGroup,
    ChatDeleted,
    MessagesRead,
    Sending
}

public class ProtocolMessage
{
    public MessageType Type { get; set; }
    public string? Data { get; set; }
    public Dictionary<string, string>? Parameters { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = false
    };

    public static ProtocolMessage FromBytes(byte[] bytes)
    {
        try
        {
            var json = Encoding.UTF8.GetString(bytes);
            var message = JsonSerializer.Deserialize<ProtocolMessage>(json, JsonOptions);
            return message ?? new ProtocolMessage();
        }
        catch (Exception)
        {
            return new ProtocolMessage();
        }
    }

    public byte[] ToBytes()
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        return Encoding.UTF8.GetBytes(json);
    }
}