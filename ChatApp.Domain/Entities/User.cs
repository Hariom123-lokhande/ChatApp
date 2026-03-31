using System;
using System.Collections.Generic;

namespace ChatApp.Domain.Entities
{
    public class User
    {
        public Guid UserId { get; set; } = Guid.NewGuid();

        public string Username { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string PasswordHash { get; set; } = string.Empty;

        public string? ProfilePictureUrl { get; set; }

        public DateTime? LastSeen { get; set; }

        public bool IsOnline { get; set; } = false;

        public bool IsActive { get; set; } = true;

        // Audit Fields
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }
        public bool IsDeleted { get; set; } = false;

        // Navigation Properties
        public ICollection<Message> SentMessages { get; set; } = new List<Message>();
        public ICollection<Message> ReceivedMessages { get; set; } = new List<Message>();
        public ICollection<GroupMember> GroupMemberships { get; set; } = new List<GroupMember>();
        public ICollection<MessageReadStatus> MessageReadStatuses { get; set; } = new List<MessageReadStatus>();
        public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    }
}