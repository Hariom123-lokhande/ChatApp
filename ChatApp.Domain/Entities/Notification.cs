using System;

namespace ChatApp.Domain.Entities
{
    public class Notification
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }

        public Guid MessageId { get; set; }

        public string Type { get; set; } = "NewMessage"; // NewMessage, GroupInvite, etc.

        public string? Content { get; set; } // Notification preview text

        public bool IsRead { get; set; } = false;

        // Audit Fields
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }
        public bool IsDeleted { get; set; } = false;

        // Navigation Properties
        public User User { get; set; } = null!;
        public Message Message { get; set; } = null!;
    }
}