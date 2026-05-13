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

        // Channel constraint: logic cho nhóm dạng kênh mà chỉ Admin dc nhắn (như yêu cầu 1:N)
        if (conversation.IsGroup && !string.IsNullOrEmpty(conversation.AdminId) && conversation.Name != null && conversation.Name.StartsWith("#", StringComparison.OrdinalIgnoreCase) && conversation.AdminId != senderId)
        {
            return; // Chỉ admin mới được gửi trong channel (kí hiệu channel bắt đầu bằng # chẳng hạn)
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

        var update = Builders<Conversation>.Update
            .Set(c => c.LastMessage, content)
            .Set(c => c.UpdatedAt, DateTime.UtcNow);
        await _mongoService.Conversations.UpdateOneAsync(c => c.Id == conversationId, update);

        await Clients.Group(conversationId).SendAsync("ReceiveMessage", message);
    }
}
