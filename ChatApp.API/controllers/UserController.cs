using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ChatApp.Infrastructure.Data;
using ChatApp.Application.DTOs;
using ChatApp.Application.Services;
using System.Security.Claims;

namespace ChatApp.API.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConnectionManager _connectionManager;

        public UserController(AppDbContext context, IConnectionManager connectionManager)
        {
            (_context, _connectionManager) = (context, connectionManager);
        }

        // ================= GET ALL USERS =================
        [HttpGet]
        public async Task<IActionResult> GetAllUsers()
        {
            var uid = GetUserId();
            if (uid == null) return Unauthorized();

            var users = await _context.Users
                .Where(u => u.UserId != uid.Value)
                .Select(u => Map(u))
                .ToListAsync();

            users.ForEach(u => u.IsOnline = _connectionManager.IsOnline(u.UserId));
            return Ok(users);
        }

        // ================= GET USER BY ID =================
        [HttpGet("{targetUserId}")]
        public async Task<IActionResult> GetUserById(Guid targetUserId)
        {
            var u = await _context.Users
                .Where(u => u.UserId == targetUserId)
                .Select(u => Map(u))
                .FirstOrDefaultAsync();

            if (u == null) return NotFound("User not found");

            u.IsOnline = _connectionManager.IsOnline(u.UserId);
            return Ok(u);
        }

        // ================= SEARCH USERS =================
        [HttpGet("search")]
        public async Task<IActionResult> SearchUsers([FromQuery] string query)
        {
            var uid = GetUserId();
            if (uid == null) return Unauthorized();

            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
                return BadRequest("Search query too short");

            var users = await _context.Users
                .Where(u => u.UserId != uid.Value && (u.Username.Contains(query) || u.Email.Contains(query)))
                .Select(u => Map(u))
                .Take(20)
                .ToListAsync();

            users.ForEach(u => u.IsOnline = _connectionManager.IsOnline(u.UserId));
            return Ok(users);
        }

        // ================= UPDATE PROFILE =================
        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest r)
        {
            var uid = GetUserId();
            if (uid == null) return Unauthorized();

            var u = await _context.Users.FindAsync(uid.Value);
            if (u == null) return NotFound();

            if (!string.IsNullOrWhiteSpace(r.Username))
                u.Username = r.Username.Trim();

            if (r.ProfilePictureUrl != null)
                u.ProfilePictureUrl = r.ProfilePictureUrl;

            u.UpdatedAt = DateTime.UtcNow;
            u.UpdatedBy = uid.Value;

            await _context.SaveChangesAsync();

            var resp = Map(u);
            resp.IsOnline = _connectionManager.IsOnline(u.UserId);
            return Ok(resp);
        }

        // ================= 🔔 GET NOTIFICATIONS =================
        [HttpGet("notifications")]
        public async Task<IActionResult> GetNotifications(int page = 1, int pageSize = 20)
        {
            var uid = GetUserId();
            if (uid == null) return Unauthorized();

            var q = _context.Notifications
                .Where(n => n.UserId == uid.Value && !n.IsDeleted)
                .OrderByDescending(n => n.CreatedAt);

            var items = await q.Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(n => new { n.Id, n.Type, n.Content, n.IsRead, n.CreatedAt, n.MessageId })
                .ToListAsync();

            return Ok(new
            {
                Items = items,
                TotalCount = await q.CountAsync(),
                Page = page,
                PageSize = pageSize
            });
        }

        // ================= ✅ MARK NOTIFICATIONS READ =================
        [HttpPost("notifications/read")]
        public async Task<IActionResult> MarkNotificationsRead([FromBody] List<Guid> ids)
        {
            var uid = GetUserId();
            if (uid == null) return Unauthorized();

            var list = await _context.Notifications
                .Where(n => ids.Contains(n.Id) && n.UserId == uid.Value && !n.IsRead)
                .ToListAsync();

            list.ForEach(n =>
            {
                n.IsRead = true;
                n.UpdatedAt = DateTime.UtcNow;
                n.UpdatedBy = uid.Value;
            });

            await _context.SaveChangesAsync();
            return Ok(new { Count = list.Count });
        }

        private static UserResponse Map(ChatApp.Domain.Entities.User u) => new UserResponse
        {
            UserId = u.UserId,
            Username = u.Username,
            Email = u.Email,
            ProfilePictureUrl = u.ProfilePictureUrl,
            IsOnline = u.IsOnline,
            LastSeen = u.LastSeen
        };

        private Guid? GetUserId() => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
    }

    public class UpdateProfileRequest { public string? Username { get; set; } public string? ProfilePictureUrl { get; set; } }
}

  
