using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace WEBchat.Models;

public class Message
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string ConversationId { get; set; } = null!;

    [BsonRepresentation(BsonType.ObjectId)]
    public string SenderId { get; set; } = null!;

    public string SenderName { get; set; } = null!;
    public string Content { get; set; } = null!;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    // Type: "text", "voice", "video", "image"
    public string MessageType { get; set; } = "text";
}
