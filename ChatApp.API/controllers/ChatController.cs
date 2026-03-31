using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ChatApp.Infrastructure.Data;
using ChatApp.Application.DTOs;
using System.Security.Claims;

namespace ChatApp.API.Controllers
{
    [ApiController]
    [Route("api/chat")]
    [Authorize]
    public class ChatController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ChatController(AppDbContext context)
        {
            _context = context;
        }

        // ================= PRIVATE CHAT =================
        [HttpGet("private/{otherUserId}")]
        public async Task<IActionResult> GetPrivateChatHistory(Guid otherUserId, int page = 1, int pageSize = 50)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var query = _context.Messages
                .Include(m => m.Sender)
                .Include(m => m.Receiver)
                .Where(m => !m.IsDeleted &&
                    ((m.SenderId == userId && m.ReceiverId == otherUserId) ||
                     (m.SenderId == otherUserId && m.ReceiverId == userId)))
                .OrderByDescending(m => m.CreatedAt);

            var totalCount = await query.CountAsync();

            var messages = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(m => new MessageResponse
                {
                    MessageId = m.MessageId,
                    RequestId = m.RequestId,
                    SenderId = m.SenderId,
                    SenderName = m.Sender.Username,
                    ReceiverId = m.ReceiverId,
                    ReceiverName = m.Receiver != null ? m.Receiver.Username : null,
                    Content = m.Content,
                    MessageType = m.MessageType,
                    IsEdited = m.IsEdited,
                    CreatedAt = m.CreatedAt,
                    UpdatedAt = m.UpdatedAt,
                    DeliveryStatus = m.DeliveryStatus // 🔥 added
                })
                .ToListAsync();

            messages.Reverse();

            return Ok(new PaginatedResult<MessageResponse>
            {
                Items = messages,
                TotalCount = totalCount,
                PageNumber = page,
                PageSize = pageSize
            });
        }

        // ================= GROUP CHAT =================
        [HttpGet("group/{groupId}")]
        public async Task<IActionResult> GetGroupChatHistory(Guid groupId, int page = 1, int pageSize = 50)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            // Check membership OR if user is the group creator
            var isMember = await _context.GroupMembers
                .AnyAsync(g => g.GroupId == groupId && g.UserId == userId && !g.IsDeleted);

            var isCreator = await _context.Groups
                .AnyAsync(g => g.GroupId == groupId && g.CreatedBy == userId && !g.IsDeleted);

            if (!isMember && !isCreator) return Forbid();

            var query = _context.Messages
                .Include(m => m.Sender)
                .Include(m => m.Group)
                .Where(m => !m.IsDeleted && m.GroupId == groupId)
                .OrderByDescending(m => m.CreatedAt);

            var totalCount = await query.CountAsync();

            var messages = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(m => new MessageResponse
                {
                    MessageId = m.MessageId,
                    RequestId = m.RequestId,
                    SenderId = m.SenderId,
                    SenderName = m.Sender.Username,
                    GroupId = m.GroupId,
                    GroupName = m.Group != null ? m.Group.Name : null,
                    Content = m.Content,
                    MessageType = m.MessageType,
                    IsEdited = m.IsEdited,
                    CreatedAt = m.CreatedAt,
                    UpdatedAt = m.UpdatedAt,
                    DeliveryStatus = m.DeliveryStatus // 🔥 added
                })
                .ToListAsync();

            messages.Reverse();

            return Ok(new PaginatedResult<MessageResponse>
            {
                Items = messages,
                TotalCount = totalCount,
                PageNumber = page,
                PageSize = pageSize
            });
        }

        // ================= CONVERSATIONS =================
        [HttpGet("conversations")]
        public async Task<IActionResult> GetConversations()
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var privateChats = await _context.Messages
                .Include(m => m.Sender)
                .Include(m => m.Receiver)
                .Where(m => !m.IsDeleted &&
                    (m.SenderId == userId || m.ReceiverId == userId) &&
                    m.GroupId == null)
                .GroupBy(m => m.SenderId == userId ? m.ReceiverId : m.SenderId)
                .Select(g => new
                {
                    UserId = g.Key,
                    LastMessage = g.OrderByDescending(m => m.CreatedAt).First()
                })
                .ToListAsync();

            var groups = await _context.GroupMembers
                .Include(g => g.Group)
                .Where(g => g.UserId == userId && !g.IsDeleted && !g.Group.IsDeleted)
                .Select(g => g.Group)
                .ToListAsync();

            return Ok(new { privateChats, groups });
        }

        private Guid? GetUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(claim, out var id) ? id : null;
        }
    }
}