using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace WEBchat.Models;

public class Friendship
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string RequesterId { get; set; } = null!;

    [BsonRepresentation(BsonType.ObjectId)]
    public string ReceiverId { get; set; } = null!;

    // Trạng thái: "Pending" (Chờ xác nhận), "Accepted" (Đã là bạn), "Declined" (Từ chối)
    public string Status { get; set; } = "Pending";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
