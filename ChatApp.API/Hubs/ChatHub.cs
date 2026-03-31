using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ChatApp.Infrastructure.Data;
using ChatApp.Domain.Entities;
using ChatApp.Application.Services;
using ChatApp.Application.DTOs;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web;

namespace ChatApp.API.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly AppDbContext _db;
        private readonly IConnectionManager _conn;
        private readonly ILogger<ChatHub> _logger;

        private static readonly ConcurrentDictionary<Guid, List<DateTime>> _rate = new();
        private static int _limit = 5;

        public ChatHub(AppDbContext db, IConnectionManager conn, ILogger<ChatHub> logger, IConfiguration config)
        {
            (_db, _conn, _logger) = (db, conn, logger);
            _limit = config.GetValue<int>("Security:MaxMessageRatePerSecond", 5);
        }

        // ================= CONNECTION =================
        public override async Task OnConnectedAsync()
        {
            var (uid, name) = (GetUserId(), GetUsername());
            if (uid == null)
            {
                Context.Abort();
                return;
            }

            _conn.AddConnection(uid.Value, Context.ConnectionId);
            _logger.LogInformation("[HUB] User {Username} ({UserId}) connected. ID: {ConnectionId}", name, uid, Context.ConnectionId);
            await Clients.Caller.SendAsync("OnlineUsers", _conn.GetAllOnlineUsers());

            var undelivered = await _db.Messages
                .Include(x => x.Sender)
                .Where(x => !x.IsDeleted && x.ReceiverId == uid && x.DeliveryStatus == 0)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync();

            foreach (var msg in undelivered)
            {
                await Clients.Caller.SendAsync("ReceivePrivateMessage", Map(msg, msg.Sender.Username));
                msg.DeliveryStatus = 1;
                msg.UpdatedAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();

            await SetUserStatus(uid.Value, true);
            await Clients.Others.SendAsync("UserOnline", new
            {
                UserId = uid,
                Username = name,
                Time = DateTime.UtcNow
            });

            // 🔥 JOIN GROUPS ON CONNECT (for SignalR rooms)
            var userGroups = await _db.GroupMembers
                .Where(x => x.UserId == uid.Value && !x.IsDeleted)
                .Select(x => x.GroupId)
                .ToListAsync();

            foreach (var gid in userGroups)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, gid.ToString());
            }
        }

        public override async Task OnDisconnectedAsync(Exception? ex)
        {
            var (uid, name) = (GetUserId(), GetUsername());
            if (uid == null) return;

            _conn.RemoveConnection(Context.ConnectionId);

            // 🔥 FIX: Wait longer to allow reconnect (tab refresh / network fluctuation)
            await Task.Delay(7000);

            // 🔥 DOUBLE CHECK (avoid false offline)
            if (!_conn.IsOnline(uid.Value))
            {
                // Extra safety delay
                await Task.Delay(1000);

                if (!_conn.IsOnline(uid.Value))
                {
                    await SetUserStatus(uid.Value, false);

                    await Clients.Others.SendAsync("UserOffline", new
                    {
                        UserId = uid,
                        Username = name,
                        Time = DateTime.UtcNow
                    });
                }
            }

            await base.OnDisconnectedAsync(ex);
        }

        // ================= PRIVATE MESSAGE =================
        public async Task SendPrivateMessage(SendPrivateMessageRequest r)
        {
            var (uid, name) = (GetUserId(), GetUsername());
            if (uid == null) throw new HubException("Unauthorized");

            Validate(r.Content);
            CheckRate(uid.Value);

            if (r.ReceiverId == Guid.Empty || r.ReceiverId == uid || string.IsNullOrWhiteSpace(r.RequestId))
                throw new HubException("Invalid request");

            var receiver = await _db.Users.FindAsync(r.ReceiverId);
            if (receiver == null) throw new HubException("Receiver not found");

            if (await _db.Messages.AnyAsync(x => x.RequestId == r.RequestId))
                return;

            var msg = Create(uid.Value, r.ReceiverId, null, r.Content, r.RequestId);
            _db.Messages.Add(msg);

            await SaveMeta(msg, r.ReceiverId, "PrivateMessage", name ?? "System");
            await _db.SaveChangesAsync();

            var resp = Map(msg, name ?? "System", receiver.Username);
            await Send(r.ReceiverId, "ReceivePrivateMessage", resp);
            await Send(uid.Value, "MessageSent", resp);
        }

        // ================= DELIVERY ACK =================
        public async Task MessageDelivered(Guid mid)
        {
            var uid = GetUserId();
            if (uid == null) return;

            var msg = await _db.Messages.FindAsync(mid);
            if (msg == null || msg.IsDeleted || (msg.ReceiverId != uid && msg.GroupId == null) || msg.DeliveryStatus >= 1)
                return;

            msg.DeliveryStatus = 1;
            msg.UpdatedAt = DateTime.UtcNow;
            msg.UpdatedBy = uid.Value;

            await _db.SaveChangesAsync();

            var conns = _conn.GetConnections(msg.SenderId);
            if (conns.Any())
            {
                await Clients.Clients(conns).SendAsync("MessageDelivered", new
                {
                    MessageId = msg.MessageId,
                    DeliveredAt = DateTime.UtcNow
                });
            }
        }

        // ================= GROUP MESSAGE =================
        public async Task SendGroupMessage(SendGroupMessageRequest r)
        {
            var (uid, name) = (GetUserId(), GetUsername());
            if (uid == null) throw new HubException("Unauthorized");

            Validate(r.Content);
            CheckRate(uid.Value);

            if (string.IsNullOrWhiteSpace(r.RequestId))
                throw new HubException("RequestId required");

            var group = await _db.Groups.FindAsync(r.GroupId);
            if (group == null || !await _db.GroupMembers.AnyAsync(x => x.GroupId == r.GroupId && x.UserId == uid.Value))
                throw new HubException("Error sending group message");

            if (await _db.Messages.AnyAsync(x => x.RequestId == r.RequestId))
                return;

            var members = await _db.GroupMembers
                .Where(x => x.GroupId == r.GroupId && x.UserId != uid.Value)
                .Select(x => x.UserId)
                .ToListAsync();

            var msg = Create(uid.Value, null, r.GroupId, r.Content, r.RequestId);
            _db.Messages.Add(msg);

            foreach (var m in members)
            {
                await SaveMeta(msg, m, "GroupMessage", name ?? "System", group?.Name);
            }

            await _db.SaveChangesAsync();

            var resp = Map(msg, name ?? "System", null, group?.Name);
            
            // 🔥 Use SignalR Groups for efficient delivery
            await Clients.Group(r.GroupId.ToString()).SendAsync("ReceiveGroupMessage", resp);
            await Clients.Caller.SendAsync("MessageSent", resp);
        }

        // ================= RECONNECT =================
        public async Task FetchMissedMessages(DateTime last)
        {
            var uid = GetUserId();
            if (uid == null) return;

            var userGroupIds = await _db.GroupMembers
                .Where(x => x.UserId == uid.Value && !x.IsDeleted)
                .Select(x => x.GroupId)
                .ToListAsync();

            var msgs = await _db.Messages
                .Include(x => x.Sender)
                .Include(x => x.Group)
                .Where(x => !x.IsDeleted && 
                    (x.ReceiverId == uid || (x.GroupId != null && userGroupIds.Contains(x.GroupId.Value))) && 
                    x.CreatedAt > last &&
                    _db.MessageReadStatuses.Any(rs => rs.MessageId == x.MessageId && rs.UserId == uid && !rs.IsRead))
                .OrderBy(x => x.CreatedAt)
                .ToListAsync();

            foreach (var m in msgs)
            {
                var senderName = m.Sender?.Username ?? "Unknown";
                var groupName = m.Group?.Name;
                var ev = m.GroupId != null ? "ReceiveGroupMessage" : "ReceivePrivateMessage";
                
                await Clients.Caller.SendAsync(ev, Map(m, senderName, null, groupName));
                
                if (m.ReceiverId == uid && m.DeliveryStatus == 0)
                {
                    m.DeliveryStatus = 1;
                    m.UpdatedAt = DateTime.UtcNow;
                }
            }
            await _db.SaveChangesAsync();
        }

        public async Task MarkMessagesRead(List<Guid> mids)
        {
            var uid = GetUserId();
            if (uid == null) return;

            var read = await _db.MessageReadStatuses
                .Where(x => mids.Contains(x.MessageId) && x.UserId == uid && !x.IsRead)
                .ToListAsync();

            foreach (var r in read)
            {
                r.IsRead = true;
                r.ReadAt = DateTime.UtcNow;
                var msg = await _db.Messages.FindAsync(r.MessageId);
                if (msg != null) msg.DeliveryStatus = 2;
            }

            // 🔥 FIX: Also mark the associated notifications as read
            var notifications = await _db.Notifications
                .Where(n => mids.Contains(n.MessageId) && n.UserId == uid && !n.IsRead)
                .ToListAsync();

            notifications.ForEach(n =>
            {
                n.IsRead = true;
                n.UpdatedAt = DateTime.UtcNow;
                n.UpdatedBy = uid;
            });

            await _db.SaveChangesAsync();

            foreach (var id in mids)
            {
                var msg = await _db.Messages.FindAsync(id);
                if (msg != null)
                {
                    await Send(msg.SenderId, "MessageRead", new { MessageId = id, ReadAt = DateTime.UtcNow });
                }
            }
        }

        // ================= TYPING =================
        public async Task SendTyping(Guid tid, bool isG) => await Broadcast(tid, isG, "UserTyping", true);
        public async Task StopTyping(Guid tid, bool isG) => await Broadcast(tid, isG, "UserStoppedTyping", false);

        private async Task Broadcast(Guid tid, bool isG, string ev, bool typing)
        {
            var data = new { UserId = GetUserId(), Username = GetUsername(), IsTyping = typing, Time = DateTime.UtcNow };
            if (isG)
            {
                var members = await _db.GroupMembers
                    .Where(x => x.GroupId == tid && x.UserId != GetUserId())
                    .Select(x => x.UserId)
                    .ToListAsync();

                foreach (var m in members) await Send(m, ev, data);
            }
            else
            {
                await Send(tid, ev, data);
            }
        }

        // ================= HELPERS =================
        private async Task Send(Guid uid, string ev, object data)
        {
            var conns = _conn.GetConnections(uid);
            _logger.LogInformation("[HUB] Sending {Event} to {UserId}. Active connections: {Count}", ev, uid, conns.Count);
            if (conns.Any()) await Clients.Clients(conns).SendAsync(ev, data);
        }

        private async Task SaveMeta(Message msg, Guid uid, string type, string sender, string? g = null)
        {
            _db.MessageReadStatuses.Add(new MessageReadStatus
            {
                MessageId = msg.MessageId,
                UserId = uid,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });

            _db.Notifications.Add(new Notification
            {
                UserId = uid,
                MessageId = msg.MessageId,
                Type = type,
                Content = g == null ? $"{sender}: {msg.Content}" : $"[{g}] {sender}: {msg.Content}",
                CreatedAt = DateTime.UtcNow
            });
        }

        private Message Create(Guid s, Guid? r, Guid? g, string c, string rid)
        {
            return new Message
            {
                MessageId = Guid.NewGuid(),
                RequestId = rid,
                SenderId = s,
                ReceiverId = r,
                GroupId = g,
                Content = HttpUtility.HtmlEncode(c.Trim()),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = s,
                DeliveryStatus = 0
            };
        }

        private MessageResponse Map(Message m, string s, string? r = null, string? g = null)
        {
            return new MessageResponse
            {
                MessageId = m.MessageId,
                RequestId = m.RequestId,
                SenderId = m.SenderId,
                SenderName = s,
                ReceiverId = m.ReceiverId,
                ReceiverName = r,
                GroupId = m.GroupId,
                GroupName = g,
                Content = m.Content,
                MessageType = m.MessageType,
                CreatedAt = m.CreatedAt,
                DeliveryStatus = m.DeliveryStatus
            };
        }

        private async Task SetUserStatus(Guid uid, bool online)
        {
            var u = await _db.Users.FindAsync(uid);
            if (u == null) return;
            u.IsOnline = online;
            u.LastSeen = online ? null : DateTime.UtcNow;
            u.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        private Guid? GetUserId() => Guid.TryParse(Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : null;
        private string? GetUsername() => Context.User?.FindFirst(ClaimTypes.Name)?.Value;
        private static void Validate(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg)) throw new HubException("Empty message");
            if (msg.Length > 4000) throw new HubException("Too long");
        }
        private static void CheckRate(Guid uid)
        {
            var now = DateTime.UtcNow;
            var list = _rate.GetOrAdd(uid, _ => new List<DateTime>());
            lock (list)
            {
                list.RemoveAll(x => (now - x).TotalSeconds > 1);
                if (list.Count >= _limit) throw new HubException("Slow down");
                list.Add(now);
            }
        }
    }
}