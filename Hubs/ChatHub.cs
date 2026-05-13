using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;
using WEBchat.Models;
using WEBchat.Services;

namespace WEBchat.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly MongoService _mongoService;

    public ChatHub(MongoService mongoService)
    {
        _mongoService = mongoService;
    }

    public async Task JoinConversation(string conversationId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, conversationId);
    }

    public async Task LeaveConversation(string conversationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, conversationId);
    }

    public async Task SendMessage(string conversationId, string content, string type = "text")
    {
        var senderId = Context.UserIdentifier;
        if (string.IsNullOrEmpty(senderId)) return;

        var senderName = Context.User?.Identity?.Name ?? "Unknown";

        // Verify conversation and authorization
        var conversation = await _mongoService.Conversations.Find(c => c.Id == conversationId).FirstOrDefaultAsync();
        if (conversation == null) return;

        if (!conversation.Participants.Contains(senderId)) return;

        // Channel/Group read-only constraint: Nếu nhóm đang bật ReadOnlyMode, chỉ có Admin hoặc Owner mới được nhắn.
        if (conversation.IsGroup && conversation.IsReadOnlyMode)
        {
            bool isAuthorized = conversation.Owners.Contains(senderId) || conversation.Admins.Contains(senderId);
            if (!isAuthorized) return;
        }

        var message = new Message
        {
            ConversationId = conversationId,
            SenderId = senderId,
            SenderName = senderName,
            Content = content,
            MessageType = type,
            Timestamp = DateTime.UtcNow
        };

        await _mongoService.Messages.InsertOneAsync(message);

        // Cập nhật cuộc hội thoại cuối
        var update = Builders<Conversation>.Update
            .Set(c => c.LastMessage, type == "text" ? content : $"[{type.ToUpper()}] Được gửi đi")
            .Set(c => c.UpdatedAt, DateTime.UtcNow);
        await _mongoService.Conversations.UpdateOneAsync(c => c.Id == conversationId, update);

        // Gửi cho tất cả mọi người trong nhóm hội thoại
        await Clients.Group(conversationId).SendAsync("ReceiveMessage", message);

        // Thông báo cho các thành viên cập nhật danh sách hội thoại (quan trọng cho tin nhắn đầu tiên)
        await Clients.Users(conversation.Participants).SendAsync("UpdateConversationList");
    }

    // WebRTC Signaling
    public async Task CallUser(string targetUserId, string conversationId, object offer)
    {
        var callerId = Context.UserIdentifier;
        var callerName = Context.User?.Identity?.Name;
        await Clients.User(targetUserId).SendAsync("ReceiveCall", callerId, callerName, conversationId, offer);
    }

    public async Task AnswerCall(string callerId, object answer)
    {
        var targetId = Context.UserIdentifier;
        await Clients.User(callerId).SendAsync("CallAccepted", targetId, answer);
    }

    public async Task SendICECandidate(string targetUserId, object candidate)
    {
        await Clients.User(targetUserId).SendAsync("ReceiveICECandidate", candidate);
    }

    public async Task RejectCall(string callerId)
    {
        await Clients.User(callerId).SendAsync("CallRejected", Context.UserIdentifier);
    }
}
