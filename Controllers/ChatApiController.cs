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
    private readonly IWebHostEnvironment _env;

    public ChatApiController(MongoService mongoService, IWebHostEnvironment env)
    {
        _mongoService = mongoService;
        _env = env;
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        
        // 1. Lấy danh sách ID của những người đã kết bạn (Accepted)
        var friendships = await _mongoService.Friendships.Find(f =>
            (f.RequesterId == currentUserId || f.ReceiverId == currentUserId) && f.Status == "Accepted"
        ).ToListAsync();

        var friendIds = friendships.Select(f => f.RequesterId == currentUserId ? f.ReceiverId : f.RequesterId).ToList();

        // 2. Lấy thông tin người dùng từ danh sách ID bạn bè đó
        var users = await _mongoService.Users.Find(u => friendIds.Contains(u.Id)).ToListAsync();
        
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
            Owners = new List<string> { currentUserId! },
            IsReadOnlyMode = req.IsChannel, // Nếu là channel, mặc định bật chế độ ReadOnly
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

    [HttpGet("conversation/{conversationId}/details")]
    public async Task<IActionResult> GetConversationDetails(string conversationId)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (currentUserId == null) return Unauthorized();

        var conversation = await _mongoService.Conversations.Find(c => c.Id == conversationId).FirstOrDefaultAsync();
        if (conversation == null || !conversation.Participants.Contains(currentUserId))
        {
            return Forbid();
        }

        // Lấy danh sách users thuộc nhóm
        var users = await _mongoService.Users.Find(u => conversation.Participants.Contains(u.Id)).ToListAsync();

        var participantsInfo = users.Select(u => new
        {
            u.Id,
            u.DisplayName,
            u.Avatar,
            Role = conversation.Owners.Contains(u.Id!) ? "Owner" :
                   (conversation.Admins.Contains(u.Id!) ? "Admin" : "Member")
        });

        return Ok(new
        {
            conversation.Id,
            conversation.Name,
            conversation.IsGroup,
            conversation.IsReadOnlyMode,
            IsMuted = conversation.MutedByUsers.Contains(currentUserId),
            MyRole = conversation.Owners.Contains(currentUserId) ? "Owner" :
                   (conversation.Admins.Contains(currentUserId) ? "Admin" : "Member"),
            Participants = participantsInfo
        });
    }

    [HttpPut("conversation/{conversationId}/readonly")]
    public async Task<IActionResult> ToggleReadOnly(string conversationId, [FromBody] bool isReadOnly)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (currentUserId == null) return Unauthorized();

        var conversation = await _mongoService.Conversations.Find(c => c.Id == conversationId).FirstOrDefaultAsync();
        if (conversation == null) return NotFound();

        // Chỉ Owner và Admin mới được set ReadOnly toggle
        if (!conversation.Owners.Contains(currentUserId) && !conversation.Admins.Contains(currentUserId))
        {
            return Forbid();
        }

        var update = Builders<Conversation>.Update.Set(c => c.IsReadOnlyMode, isReadOnly);
        await _mongoService.Conversations.UpdateOneAsync(c => c.Id == conversationId, update);

        return Ok(new { message = "Cập nhật thành công", isReadOnly });
    }

    [HttpPut("conversation/{conversationId}/role/{targetUserId}")]
    public async Task<IActionResult> ChangeRole(string conversationId, string targetUserId, [FromQuery] string newRole)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (currentUserId == null) return Unauthorized();

        var conversation = await _mongoService.Conversations.Find(c => c.Id == conversationId).FirstOrDefaultAsync();
        if (conversation == null) return NotFound();

        // Chỉ Owner mới được thay đổi quyền (thăng cấp Admin hoặc giáng cấp)
        if (!conversation.Owners.Contains(currentUserId))
        {
            return Forbid();
        }

        if (newRole == "Admin")
        {
            var update = Builders<Conversation>.Update.AddToSet(c => c.Admins, targetUserId);
            await _mongoService.Conversations.UpdateOneAsync(c => c.Id == conversationId, update);
        }
        else if (newRole == "Member")
        {
            var update = Builders<Conversation>.Update.Pull(c => c.Admins, targetUserId);
            await _mongoService.Conversations.UpdateOneAsync(c => c.Id == conversationId, update);
        }

        return Ok(new { message = "Cập nhật quyền thành công." });
    }

    [HttpPost("conversation/{conversationId}/participants")]
    public async Task<IActionResult> InviteToGroup(string conversationId, [FromBody] List<string> participantIds)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (currentUserId == null) return Unauthorized();

        var conversation = await _mongoService.Conversations.Find(c => c.Id == conversationId).FirstOrDefaultAsync();
        if (conversation == null) return NotFound();

        // Chỉ Owner và Admin mới được mời thêm người
        if (!conversation.Owners.Contains(currentUserId) && !conversation.Admins.Contains(currentUserId))
        {
            return Forbid();
        }

        var update = Builders<Conversation>.Update.AddToSetEach(c => c.Participants, participantIds);
        await _mongoService.Conversations.UpdateOneAsync(c => c.Id == conversationId, update);

        return Ok(new { message = "Đã thêm thành viên thành công." });
    }

    [HttpPut("conversation/{conversationId}/mute")]
    public async Task<IActionResult> ToggleMute(string conversationId, [FromBody] bool isMuted)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (currentUserId == null) return Unauthorized();

        var conversation = await _mongoService.Conversations.Find(c => c.Id == conversationId).FirstOrDefaultAsync();
        if (conversation == null || !conversation.Participants.Contains(currentUserId))
        {
            return NotFound();
        }

        UpdateDefinition<Conversation> update;
        if (isMuted)
        {
            update = Builders<Conversation>.Update.AddToSet(c => c.MutedByUsers, currentUserId);
        }
        else
        {
            update = Builders<Conversation>.Update.Pull(c => c.MutedByUsers, currentUserId);
        }

        await _mongoService.Conversations.UpdateOneAsync(c => c.Id == conversationId, update);

        return Ok(new { message = isMuted ? "Đã tắt thông báo" : "Đã bật thông báo", isMuted });
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

    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile([FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest("No file found.");

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(currentUserId)) return Unauthorized();

        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".pdf", ".doc", ".docx", ".zip", ".webm", ".mkv", ".mp3", ".wav" };
        var ext = Path.GetExtension(file.FileName).ToLower();

        if (!allowedExtensions.Contains(ext))
            return BadRequest("File type not allowed.");

        // Create folder if not exists
        var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads");
        if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

        var uniqueName = Guid.NewGuid().ToString() + ext;
        var filePath = Path.Combine(uploadsFolder, uniqueName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var fileUrl = $"/uploads/{uniqueName}";

        // Determine message type
        var type = "file";
        if (new[] { ".jpg", ".jpeg", ".png", ".gif" }.Contains(ext)) type = "image";
        else if (new[] { ".webm", ".mp3", ".wav" }.Contains(ext)) type = "audio";

        return Ok(new { url = fileUrl, type = type });
    }

    // ==================== Quản lý nhóm: Rời nhóm + Hủy nhóm ====================

    [HttpPost("conversation/{conversationId}/leave")]
    public async Task<IActionResult> LeaveGroup(string conversationId)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (currentUserId == null) return Unauthorized();

        var conversation = await _mongoService.Conversations.Find(c => c.Id == conversationId).FirstOrDefaultAsync();
        if (conversation == null) return NotFound();
        if (!conversation.IsGroup) return BadRequest(new { message = "Chỉ áp dụng cho nhóm." });
        if (!conversation.Participants.Contains(currentUserId)) return BadRequest(new { message = "Bạn không thuộc nhóm này." });

        // Owner không thể rời, phải chuyển quyền hoặc hủy nhóm
        if (conversation.Owners.Contains(currentUserId))
        {
            return BadRequest(new { message = "Chủ nhóm không thể rời. Hãy chuyển quyền hoặc hủy nhóm." });
        }

        // Xóa khỏi participants, admins
        var update = Builders<Conversation>.Update
            .Pull(c => c.Participants, currentUserId)
            .Pull(c => c.Admins, currentUserId)
            .Pull(c => c.MutedByUsers, currentUserId);
        await _mongoService.Conversations.UpdateOneAsync(c => c.Id == conversationId, update);

        return Ok(new { message = "Đã rời nhóm thành công." });
    }

    [HttpDelete("conversation/{conversationId}/delete")]
    public async Task<IActionResult> DeleteGroup(string conversationId)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (currentUserId == null) return Unauthorized();

        var conversation = await _mongoService.Conversations.Find(c => c.Id == conversationId).FirstOrDefaultAsync();
        if (conversation == null) return NotFound();
        if (!conversation.IsGroup) return BadRequest(new { message = "Chỉ áp dụng cho nhóm." });

        // Chỉ Owner mới được hủy nhóm
        if (!conversation.Owners.Contains(currentUserId))
        {
            return Forbid();
        }

        // Xóa tất cả tin nhắn của nhóm
        await _mongoService.Messages.DeleteManyAsync(m => m.ConversationId == conversationId);
        // Xóa conversation
        await _mongoService.Conversations.DeleteOneAsync(c => c.Id == conversationId);

        return Ok(new { message = "Đã hủy nhóm và xóa toàn bộ dữ liệu." });
    }
}

public class CreateGroupRequest
{
    public string Name { get; set; } = null!;
    public List<string> ParticipantIds { get; set; } = new();
    public bool IsChannel { get; set; } // If true, only admin can send
}
