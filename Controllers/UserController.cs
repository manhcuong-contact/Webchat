using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Bson;
using WEBchat.Models;
using WEBchat.Services;
using WEBchat.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace WEBchat.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class UserController : ControllerBase
{
    private readonly MongoService _mongoService;
    private readonly IHubContext<ChatHub> _hubContext;

    public UserController(MongoService mongoService, IHubContext<ChatHub> hubContext)
    {
        _mongoService = mongoService;
        _hubContext = hubContext;
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchUsers([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q)) return Ok(new List<object>());

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        // Case-insensitive search by DisplayName or Username using Regex for better compatibility
        var escapedQ = System.Text.RegularExpressions.Regex.Escape(q);
        var filter = Builders<User>.Filter.And(
            Builders<User>.Filter.Ne(u => u.Id, currentUserId),
            Builders<User>.Filter.Or(
                Builders<User>.Filter.Regex(u => u.DisplayName, new BsonRegularExpression(escapedQ, "i")),
                Builders<User>.Filter.Regex(u => u.Username, new BsonRegularExpression(escapedQ, "i"))
            )
        );

        var users = await _mongoService.Users.Find(filter).Limit(20).ToListAsync();

        var result = new List<object>();
        foreach (var u in users)
        {
            var friendship = await GetFriendshipStatus(currentUserId, u.Id!);
            result.Add(new
            {
                u.Id,
                u.DisplayName,
                u.Username,
                u.Avatar,
                FriendshipStatus = friendship?.Status ?? "None"
            });
        }

        return Ok(result);
    }

    [HttpGet("profile/{id}")]
    public async Task<IActionResult> GetProfile(string id)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _mongoService.Users.Find(u => u.Id == id).FirstOrDefaultAsync();

        if (user == null) return NotFound();

        if (currentUserId == id)
        {
            // Self
            return Ok(new { user.Id, user.DisplayName, user.Username, user.Avatar, user.Age, user.Email, user.Phone, IsSelf = true });
        }

        var friendship = await GetFriendshipStatus(currentUserId, id);
        bool isFriend = friendship?.Status == "Accepted";

        if (isFriend)
        {
            // Friend -> See all info
            return Ok(new { user.Id, user.DisplayName, user.Username, user.Avatar, user.Age, user.Email, user.Phone, IsSelf = false, FriendshipStatus = "Accepted" });
        }
        else
        {
            // Stranger -> Hidden phone/email
            return Ok(new { user.Id, user.DisplayName, user.Username, user.Avatar, Age = user.Age, IsSelf = false, FriendshipStatus = friendship?.Status ?? "None" });
        }
    }

    [HttpPost("friend-request/{receiverId}")]
    public async Task<IActionResult> SendFriendRequest(string receiverId)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (currentUserId == receiverId) return BadRequest("Cannot send request to yourself");

        var existing = await GetFriendshipStatus(currentUserId, receiverId);
        if (existing != null) return BadRequest("Friendship or request already exists");

        var friendship = new Friendship
        {
            RequesterId = currentUserId,
            ReceiverId = receiverId,
            Status = "Pending"
        };

        await _mongoService.Friendships.InsertOneAsync(friendship);

        // Notify the receiver via SignalR
        var requesterName = User.Identity?.Name ?? "Người dùng ẩn danh";
        await _hubContext.Clients.User(receiverId).SendAsync("ReceiveFriendRequest", requesterName);

        return Ok(new { message = "Request sent" });
    }

    [HttpPost("friend-accept/{requesterId}")]
    public async Task<IActionResult> AcceptFriendRequest(string requesterId)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var filter = Builders<Friendship>.Filter.Where(f => f.RequesterId == requesterId && f.ReceiverId == currentUserId && f.Status == "Pending");
        var update = Builders<Friendship>.Update.Set(f => f.Status, "Accepted");

        var result = await _mongoService.Friendships.UpdateOneAsync(filter, update);
        if (result.ModifiedCount == 0) return BadRequest("Request not found or already processed");

        return Ok(new { message = "Request accepted" });
    }

    [HttpPost("friend-decline/{requesterId}")]
    public async Task<IActionResult> DeclineFriendRequest(string requesterId)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var filter = Builders<Friendship>.Filter.Where(f => f.RequesterId == requesterId && f.ReceiverId == currentUserId && f.Status == "Pending");
        var update = Builders<Friendship>.Update.Set(f => f.Status, "Declined");

        var result = await _mongoService.Friendships.UpdateOneAsync(filter, update);
        if (result.ModifiedCount == 0) return BadRequest("Request not found or already processed");

        return Ok(new { message = "Request declined" });
    }

    [HttpGet("friend-requests")]
    public async Task<IActionResult> GetPendingRequests()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        // Tìm các bản ghi mà mình là người nhận và trạng thái là Pending
        var requests = await _mongoService.Friendships.Find(f =>
            f.ReceiverId == currentUserId && f.Status == "Pending"
        ).ToListAsync();

        var requesterIds = requests.Select(f => f.RequesterId).ToList();

        var requesters = await _mongoService.Users.Find(u => requesterIds.Contains(u.Id)).ToListAsync();

        return Ok(requesters.Select(u => new { u.Id, u.DisplayName, u.Username, u.Avatar }));
    }

    [HttpGet("friends")]
    public async Task<IActionResult> GetFriends()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var friendships = await _mongoService.Friendships.Find(f =>
            (f.RequesterId == currentUserId || f.ReceiverId == currentUserId) && f.Status == "Accepted"
        ).ToListAsync();

        var friendIds = friendships.Select(f => f.RequesterId == currentUserId ? f.ReceiverId : f.RequesterId).ToList();

        var friends = await _mongoService.Users.Find(u => friendIds.Contains(u.Id)).ToListAsync();

        return Ok(friends.Select(u => new { u.Id, u.DisplayName, u.Username, u.Avatar }));
    }

    // Helper
    private async Task<Friendship?> GetFriendshipStatus(string userId1, string userId2)
    {
        return await _mongoService.Friendships.Find(f =>
            (f.RequesterId == userId1 && f.ReceiverId == userId2) ||
            (f.RequesterId == userId2 && f.ReceiverId == userId1)
        ).FirstOrDefaultAsync();
    }
}
