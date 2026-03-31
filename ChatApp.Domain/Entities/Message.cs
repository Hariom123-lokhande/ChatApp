using System;
using System.Collections.Generic;

namespace ChatApp.Domain.Entities
{
    public class Message
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();

        // Duplicate prevention (Idempotency)
        public string RequestId { get; set; } = string.Empty;

        public Guid SenderId { get; set; }

        public Guid? ReceiverId { get; set; }  // NULL = group message

        public Guid? GroupId { get; set; }       // NULL = private message

        public string Content { get; set; } = string.Empty;
        public int DeliveryStatus { get; set; } = 0;

        public string MessageType { get; set; } = "Text"; // Text, Image, File

        public bool IsEdited { get; set; } = false;

        // Audit Fields
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }
        public bool IsDeleted { get; set; } = false;

        // Navigation Properties
        public User Sender { get; set; } = null!;
        public User? Receiver { get; set; }
        public Group? Group { get; set; }
        public ICollection<MessageReadStatus> ReadStatuses { get; set; } = new List<MessageReadStatus>();
    }
}