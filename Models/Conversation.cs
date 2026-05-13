using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace WEBchat.Models;

public class Conversation
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string? Name { get; set; } // Only for group chats
    public bool IsGroup { get; set; } = false;

    [BsonRepresentation(BsonType.ObjectId)]
    public List<string> Participants { get; set; } = new();

    [BsonRepresentation(BsonType.ObjectId)]
    public List<string> Owners { get; set; } = new();

    [BsonRepresentation(BsonType.ObjectId)]
    public List<string> Admins { get; set; } = new();

    public bool IsReadOnlyMode { get; set; } = false;

    [BsonRepresentation(BsonType.ObjectId)]
    public List<string> MutedByUsers { get; set; } = new();

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? LastMessage { get; set; }
}
