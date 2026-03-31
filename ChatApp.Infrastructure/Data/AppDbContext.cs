using ChatApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Infrastructure.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<Group> Groups { get; set; }
        public DbSet<GroupMember> GroupMembers { get; set; }
        public DbSet<MessageReadStatus> MessageReadStatuses { get; set; }
        public DbSet<Notification> Notifications { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ============================================================
            // 🔥 GLOBAL QUERY FILTERS (Soft Delete: WHERE IsDeleted = 0)
            // ============================================================
            modelBuilder.Entity<User>().HasQueryFilter(u => !u.IsDeleted);
            modelBuilder.Entity<Message>().HasQueryFilter(m => !m.IsDeleted);
            modelBuilder.Entity<Group>().HasQueryFilter(g => !g.IsDeleted);
            modelBuilder.Entity<GroupMember>().HasQueryFilter(gm => !gm.IsDeleted);
            modelBuilder.Entity<MessageReadStatus>().HasQueryFilter(mrs => !mrs.IsDeleted);
            modelBuilder.Entity<Notification>().HasQueryFilter(n => !n.IsDeleted);

            // ============================================================
            // 🧑 USER Configuration
            // ============================================================
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(u => u.UserId);

                entity.HasIndex(u => u.Email).IsUnique();
                entity.HasIndex(u => u.Username);

                entity.Property(u => u.Username).HasMaxLength(100).IsRequired();
                entity.Property(u => u.Email).HasMaxLength(256).IsRequired();
                entity.Property(u => u.PasswordHash).IsRequired();
                entity.Property(u => u.ProfilePictureUrl).HasMaxLength(500);
            });

            // ============================================================
            // 💬 MESSAGE Configuration
            // ============================================================
            modelBuilder.Entity<Message>(entity =>
            {
                entity.HasKey(m => m.MessageId);

                // Idempotency: Unique RequestId
                entity.HasIndex(m => m.RequestId).IsUnique();

                // Performance indexes
                entity.HasIndex(m => m.SenderId);
                entity.HasIndex(m => m.ReceiverId);
                entity.HasIndex(m => m.GroupId);
                entity.HasIndex(m => m.CreatedAt);

                // Composite index for chat history queries
                entity.HasIndex(m => new { m.SenderId, m.ReceiverId, m.CreatedAt });
                entity.HasIndex(m => new { m.GroupId, m.CreatedAt });

                entity.Property(m => m.RequestId).HasMaxLength(100).IsRequired();
                entity.Property(m => m.Content).HasMaxLength(4000).IsRequired();
                entity.Property(m => m.MessageType).HasMaxLength(50).IsRequired();

                // Relationships
                entity.HasOne(m => m.Sender)
                    .WithMany(u => u.SentMessages)
                    .HasForeignKey(m => m.SenderId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(m => m.Receiver)
                    .WithMany(u => u.ReceivedMessages)
                    .HasForeignKey(m => m.ReceiverId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(m => m.Group)
                    .WithMany(g => g.Messages)
                    .HasForeignKey(m => m.GroupId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ============================================================
            // 👥 GROUP Configuration
            // ============================================================
            modelBuilder.Entity<Group>(entity =>
            {
                entity.HasKey(g => g.GroupId);

                entity.HasIndex(g => g.Name);

                entity.Property(g => g.Name).HasMaxLength(200).IsRequired();
                entity.Property(g => g.Description).HasMaxLength(1000);

                entity.HasOne(g => g.Creator)
                    .WithMany()
                    .HasForeignKey(g => g.CreatedBy)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ============================================================
            // 👥 GROUP MEMBER Configuration
            // ============================================================
            modelBuilder.Entity<GroupMember>(entity =>
            {
                entity.HasKey(gm => gm.Id);

                // Prevent duplicate membership
                entity.HasIndex(gm => new { gm.GroupId, gm.UserId }).IsUnique();

                entity.Property(gm => gm.Role).HasMaxLength(50).IsRequired();

                entity.HasOne(gm => gm.Group)
                    .WithMany(g => g.Members)
                    .HasForeignKey(gm => gm.GroupId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(gm => gm.User)
                    .WithMany(u => u.GroupMemberships)
                    .HasForeignKey(gm => gm.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ============================================================
            // 📥 MESSAGE READ STATUS Configuration
            // ============================================================
            modelBuilder.Entity<MessageReadStatus>(entity =>
            {
                entity.HasKey(mrs => mrs.Id);

                // Prevent duplicate read status per user per message
                entity.HasIndex(mrs => new { mrs.MessageId, mrs.UserId }).IsUnique();

                entity.HasOne(mrs => mrs.Message)
                    .WithMany(m => m.ReadStatuses)
                    .HasForeignKey(mrs => mrs.MessageId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(mrs => mrs.User)
                    .WithMany(u => u.MessageReadStatuses)
                    .HasForeignKey(mrs => mrs.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ============================================================
            // 🔔 NOTIFICATION Configuration
            // ============================================================
            modelBuilder.Entity<Notification>(entity =>
            {
                entity.HasKey(n => n.Id);

                entity.HasIndex(n => new { n.UserId, n.IsRead });

                entity.Property(n => n.Type).HasMaxLength(100).IsRequired();
                entity.Property(n => n.Content).HasMaxLength(500);

                entity.HasOne(n => n.User)
                    .WithMany(u => u.Notifications)
                    .HasForeignKey(n => n.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(n => n.Message)
                    .WithMany()
                    .HasForeignKey(n => n.MessageId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}