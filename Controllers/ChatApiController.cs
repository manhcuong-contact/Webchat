using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using WEBchat.Models;
using WEBchat.Services;

namespace WEBchat.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class ChatApiController : ControllerBase
{
    private readonly MongoService _mongoService;

    public ChatApiController(MongoService mongoService)
    {
        _mongoService = mongoService;
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        // Bỏ qua current user
        var users = await _mongoService.Users.Find(u => u.Id != currentUserId).ToListAsync();
        return Ok(users.Select(u => new { u.Id, u.Username, u.DisplayName, u.Avatar }));
    }

    [HttpGet("conversations")]
    public async Task<IActionResult> GetConversations()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var conversations = await _mongoService.Conversations
            .Find(c => c.Participants.Contains(currentUserId))
            .SortByDescending(c => c.UpdatedAt)
            .ToListAsync();

        var result = new List<object>();

        foreach (var conv in conversations)
        {
            if (!conv.IsGroup)
            {
                // Find the other user
                var otherUserId = conv.Participants.FirstOrDefault(p => p != currentUserId);
                if (otherUserId != null)
                {
                    var otherUser = await _mongoService.Users.Find(u => u.Id == otherUserId).FirstOrDefaultAsync();
                    if (otherUser != null)
                    {
                        result.Add(new
                        {
                            conv.Id,
                            Name = otherUser.DisplayName,
                            IsGroup = conv.IsGroup,
                            Avatar = otherUser.Avatar,
                            conv.LastMessage,
                            conv.UpdatedAt
                        });
                    }
                }
            }
            else
            {
                result.Add(new
                {
                    conv.Id,
                    conv.Name,
                    conv.IsGroup,
                    Avatar = "/img/group_avatar.png",
                    conv.LastMessage,
                    conv.UpdatedAt
                });
            }
        }

        return Ok(result);
    }

    [HttpPost("conversation/private/{otherUserId}")]
    public async Task<IActionResult> CreateOrGetPrivateConversation(string otherUserId)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        // Check if conversation exists
        var existing = await _mongoService.Conversations.Find(c =>
            !c.IsGroup &&
            c.Participants.Contains(currentUserId) &&
            c.Participants.Contains(otherUserId)).FirstOrDefaultAsync();

        if (existing != null)
        {
            return Ok(new { conversationId = existing.Id });
        }

        var newConv = new Conversation
        {
            IsGroup = false,
            Participants = new List<string> { currentUserId, otherUserId },
            UpdatedAt = DateTime.UtcNow
        };

        await _mongoService.Conversations.InsertOneAsync(newConv);
        return Ok(new { conversationId = newConv.Id });
    }

    [HttpPost("conversation/group")]
    public async Task<IActionResult> CreateGroupConversation([FromBody] CreateGroupRequest req)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        req.ParticipantIds.Add(currentUserId);

        var newConv = new Conversation
        {
            Name = req.Name,
            IsGroup = true,
            AdminId = req.IsChannel ? currentUserId : null,
            // Prefix '#' if it is a channel (1:N only admin chats)
            Participants = req.ParticipantIds.Distinct().ToList(),
            UpdatedAt = DateTime.UtcNow
        };

        if (req.IsChannel && !req.Name.StartsWith("#"))
        {
            newConv.Name = "#" + req.Name;
        }

        await _mongoService.Conversations.InsertOneAsync(newConv);
        return Ok(new { conversationId = newConv.Id });
    }

    [HttpGet("conversation/{conversationId}/messages")]
    public async Task<IActionResult> GetMessages(string conversationId)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var conversation = await _mongoService.Conversations.Find(c => c.Id == conversationId).FirstOrDefaultAsync();
        if (conversation == null || !conversation.Participants.Contains(currentUserId))
        {
            return Forbid();
        }

        var messages = await _mongoService.Messages
            .Find(m => m.ConversationId == conversationId)
            .SortBy(m => m.Timestamp)
            .ToListAsync();

        return Ok(messages);
    }
}

public class CreateGroupRequest
{
    public string Name { get; set; } = null!;
    public List<string> ParticipantIds { get; set; } = new();
    public bool IsChannel { get; set; } // If true, only admin can send
}
