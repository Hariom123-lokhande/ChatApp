using System;

namespace ChatApp.Domain.Entities
{
    public class MessageReadStatus
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid MessageId { get; set; }

        public Guid UserId { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTime? ReadAt { get; set; }

        // Audit Fields
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }
        public bool IsDeleted { get; set; } = false;

        // Navigation Properties
        public Message Message { get; set; } = null!;
        public User User { get; set; } = null!;
    }
}
