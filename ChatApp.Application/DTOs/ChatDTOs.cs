using System;

namespace ChatApp.Application.DTOs
{
    // ============================================================
    // 🔐 AUTH DTOs
    // ============================================================
    public class RegisterRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? ProfilePictureUrl { get; set; }
    }

    public class LoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class LoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? ProfilePictureUrl { get; set; }
    }

    // ============================================================
    // 💬 MESSAGE DTOs
    // ============================================================
    public class SendPrivateMessageRequest
    {
        public string RequestId { get; set; } = string.Empty;  // Idempotency key
        public Guid ReceiverId { get; set; }
        public string Content { get; set; } = string.Empty;
        public string MessageType { get; set; } = "Text";
    }

    public class SendGroupMessageRequest
    {
        public string RequestId { get; set; } = string.Empty;  // Idempotency key
        public Guid GroupId { get; set; }
        public string Content { get; set; } = string.Empty;
        public string MessageType { get; set; } = "Text";
    }

    public class MessageResponse
    {
        public Guid MessageId { get; set; }
        public int DeliveryStatus { get; set; }
        public string RequestId { get; set; } = string.Empty;
        public Guid SenderId { get; set; }
        public string SenderName { get; set; } = string.Empty;
        public Guid? ReceiverId { get; set; }
        public string? ReceiverName { get; set; }
        public Guid? GroupId { get; set; }
        public string? GroupName { get; set; }
        public string Content { get; set; } = string.Empty;
        public string MessageType { get; set; } = "Text";
        public bool IsEdited { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // ============================================================
    // 👥 GROUP DTOs
    // ============================================================
    public class CreateGroupRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<Guid>? MemberIds { get; set; }
    }

    public class AddGroupMemberRequest
    {
        public Guid GroupId { get; set; }
        public Guid UserId { get; set; }
        public string Role { get; set; } = "Member";
    }

    public class AddMemberByEmailRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = "Member";
    }

    public class GroupResponse
    {
        public Guid GroupId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int MemberCount { get; set; }
        public int OnlineCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid CreatedBy { get; set; }
        public string CreatorName { get; set; } = string.Empty;
    }

    public class GroupMemberResponse
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsOnline { get; set; }
        public DateTime JoinedAt { get; set; }
    }

    // ============================================================
    // 🧑 USER DTOs
    // ============================================================
    public class UserResponse
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? ProfilePictureUrl { get; set; }
        public bool IsOnline { get; set; }
        public DateTime? LastSeen { get; set; }
    }

    // ============================================================
    // 📥 READ STATUS DTOs
    // ============================================================
    public class MarkReadRequest
    {
        public List<Guid> MessageIds { get; set; } = new();
    }

    public class UnreadCountResponse
    {
        public int TotalUnread { get; set; }
        public Dictionary<string, int> PerConversation { get; set; } = new();
    }

  //notification
    public class NotificationResponse
    {
        public Guid Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string? Content { get; set; }
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid MessageId { get; set; }
    }
//reconnect dto
    public class ReconnectRequest
    {
        public DateTime LastMessageTimestamp { get; set; }
    }
    //PAGINATION
    public class PaginatedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public bool HasNext => PageNumber * PageSize < TotalCount;
        public bool HasPrevious => PageNumber > 1;
    }
}
