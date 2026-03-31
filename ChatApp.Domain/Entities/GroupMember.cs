using System;

namespace ChatApp.Domain.Entities
{
    public class GroupMember
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid GroupId { get; set; }

        public Guid UserId { get; set; }

        public string Role { get; set; } = "Member"; // Admin / Member

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        // Audit Fields
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }
        public bool IsDeleted { get; set; } = false;

        // Navigation Properties
        public Group Group { get; set; } = null!;
        public User User { get; set; } = null!;
    }
}
