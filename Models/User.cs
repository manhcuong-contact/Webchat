using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace WEBchat.Models;

public class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string Username { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public int Age { get; set; }
    public string Email { get; set; } = null!;
    public string? Phone { get; set; }
    public string? Avatar { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
