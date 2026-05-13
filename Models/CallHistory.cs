using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace WEBchat.Models;

public class CallHistory
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string CallerId { get; set; } = null!;
    public string ReceiverId { get; set; } = null!;
    public string ConversationId { get; set; } = null!;
    
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    
    public string CallType { get; set; } = null!; // "Voice", "Video"
    public string Status { get; set; } = null!; // "Missed", "Completed", "Rejected"
}
