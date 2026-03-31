using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ChatApp.Infrastructure.Data;
using ChatApp.Domain.Entities;
using ChatApp.Application.DTOs;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace ChatApp.API.Controllers
{
    [ApiController]
    [Route("api/groups")]
    [Authorize]
    public class GroupController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly Microsoft.AspNetCore.SignalR.IHubContext<ChatApp.API.Hubs.ChatHub> _hub;
        private readonly ChatApp.Application.Services.IConnectionManager _conn;

        public GroupController(AppDbContext context, Microsoft.AspNetCore.SignalR.IHubContext<ChatApp.API.Hubs.ChatHub> hub, ChatApp.Application.Services.IConnectionManager conn)
        {
            _context = context;
            _hub = hub;
            _conn = conn;
        }

        [HttpPost]
        public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest r)
        {
            var uid = GetUserId();
            if (uid == null) return Unauthorized();

            if (string.IsNullOrWhiteSpace(r.Name))
                return BadRequest(new { message = "Group name is required" });

            var group = new Group
            {
                GroupId = Guid.NewGuid(),
                Name = r.Name.Trim(),
                Description = r.Description?.Trim(),
                CreatedBy = uid.Value,
                CreatedAt = DateTime.UtcNow
            };
            _context.Groups.Add(group);

            // Add creator as Admin
            _context.GroupMembers.Add(new GroupMember
            {
                GroupId = group.GroupId,
                UserId = uid.Value,
                Role = "Admin",
                JoinedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = uid.Value
            });

            // Add initial members if provided
            if (r.MemberIds != null)
            {
                foreach (var mid in r.MemberIds.Distinct())
                {
                    if (mid != uid.Value && await _context.Users.AnyAsync(u => u.UserId == mid))
                    {
                        _context.GroupMembers.Add(new GroupMember
                        {
                            GroupId = group.GroupId,
                            UserId = mid,
                            Role = "Member",
                            JoinedAt = DateTime.UtcNow,
                            CreatedAt = DateTime.UtcNow,
                            CreatedBy = uid.Value
                        });
                    }
                }
            }

            await _context.SaveChangesAsync();

            return Ok(new GroupResponse
            {
                GroupId = group.GroupId,
                Name = group.Name,
                Description = group.Description,
                MemberCount = 1 + (r.MemberIds?.Count ?? 0),
                OnlineCount = 1, // Creator is online
                CreatedAt = group.CreatedAt,
                CreatedBy = group.CreatedBy
            });
        }

        [HttpPost("{groupId}/members")]
        public async Task<IActionResult> AddMember(Guid groupId, [FromBody] AddGroupMemberRequest r)
        {
            var uid = GetUserId();
            if (uid == null) return Unauthorized();

            var m = await GetM(groupId, uid.Value);
            var group = await _context.Groups.FindAsync(groupId);
            // Allow if Admin role OR if user is the original creator
            if ((m == null || m.Role != "Admin") && group?.CreatedBy != uid.Value)
                return BadRequest(new { message = "Only admins can add members" });

            if (!await _context.Users.AnyAsync(u => u.UserId == r.UserId))
                return NotFound(new { message = "User not found" });

            if (await _context.GroupMembers.AnyAsync(gm => gm.GroupId == groupId && gm.UserId == r.UserId && !gm.IsDeleted))
                return BadRequest(new { message = "Already a member" });

            var existing = await _context.GroupMembers.FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == r.UserId);
            if (existing != null)
            {
                existing.IsDeleted = false;
                existing.Role = r.Role ?? "Member";
                existing.JoinedAt = DateTime.UtcNow;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.UpdatedBy = uid.Value;
            }
            else
            {
                _context.GroupMembers.Add(new GroupMember
                {
                    GroupId = groupId,
                    UserId = r.UserId,
                    Role = r.Role ?? "Member",
                    JoinedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = uid.Value
                });
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Member added successfully" });
        }

        [HttpPost("{groupId}/members/add-by-email")]
        public async Task<IActionResult> AddMemberByEmail(Guid groupId, [FromBody] AddMemberByEmailRequest r)
        {
            var uid = GetUserId();
            if (uid == null) return Unauthorized();

            var m = await GetM(groupId, uid.Value);
            var groupCheck = await _context.Groups.FindAsync(groupId);
            // Allow if Admin role OR if user is the original creator
            if ((m == null || m.Role != "Admin") && groupCheck?.CreatedBy != uid.Value)
                return BadRequest(new { message = "Only admins can add members" });

            var target = await _context.Users.FirstOrDefaultAsync(u => u.Email == r.Email);
            if (target == null) return NotFound(new { message = "User not found" });

            var existing = await _context.GroupMembers.FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == target.UserId);
            if (existing != null)
            {
                if (!existing.IsDeleted) return BadRequest(new { message = "Already a member" });
                existing.IsDeleted = false;
                existing.Role = r.Role ?? "Member";
                existing.JoinedAt = DateTime.UtcNow;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.UpdatedBy = uid.Value;
            }
            else
            {
                _context.GroupMembers.Add(new GroupMember
                {
                    GroupId = groupId,
                    UserId = target.UserId,
                    Role = r.Role ?? "Member",
                    JoinedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = uid.Value
                });
            }

            await _context.SaveChangesAsync();

            // 🔥 Notify the target user in real-time if they are online to avoid manual page refresh
            var targetConns = _conn.GetConnections(target.UserId);
            if (targetConns.Any())
            {
                var groupInfo = await _context.Groups.FindAsync(groupId);
                await _hub.Clients.Clients(targetConns).SendAsync("AddedToGroup", new
                {
                    GroupId = groupId,
                    Name = groupInfo?.Name,
                    Description = groupInfo?.Description,
                    MemberCount = await _context.GroupMembers.CountAsync(m => m.GroupId == groupId && !m.IsDeleted),
                    CreatedAt = groupInfo?.CreatedAt ?? DateTime.UtcNow,
                    CreatedBy = groupInfo?.CreatedBy ?? uid.Value
                });

                // Also bind their live websockets to the SignalR group room natively
                foreach (var c in targetConns)
                {
                    await _hub.Groups.AddToGroupAsync(c, groupId.ToString());
                }
            }

            return Ok(new { message = $"User {target.Username} added successfully" });
        }

        [HttpDelete("{groupId}/members/{mid}")]
        public async Task<IActionResult> RemoveMember(Guid groupId, Guid mid)
        {
            var uid = GetUserId();
            if (uid == null) return Unauthorized();

            var reqM = await GetM(groupId, uid.Value);
            if (reqM == null || (reqM.Role != "Admin" && mid != uid.Value))
                return BadRequest(new { message = "Unauthorized" });

            var m = await GetM(groupId, mid);
            if (m == null) return NotFound("Member not found");

            m.IsDeleted = true;
            m.UpdatedAt = DateTime.UtcNow;
            m.UpdatedBy = uid.Value;

            await _context.SaveChangesAsync();
            return Ok(new { message = "Removed" });
        }

        [HttpGet("{groupId}")]
        public async Task<IActionResult> GetGroupDetails(Guid groupId)
        {
            var uid = GetUserId();
            if (uid == null) return Unauthorized();

            var group = await _context.Groups.FirstOrDefaultAsync(g => g.GroupId == groupId && !g.IsDeleted);
            if (group == null || !await _context.GroupMembers.AnyAsync(gm => gm.GroupId == groupId && gm.UserId == uid.Value && !gm.IsDeleted))
                return NotFound();

            var members = await _context.GroupMembers
                .Include(gm => gm.User)
                .Where(gm => gm.GroupId == groupId && !gm.IsDeleted)
                .ToListAsync();

            var memberResponses = members.Select(gm => new GroupMemberResponse
                {
                    UserId = gm.UserId,
                    Username = gm.User.Username,
                    Role = gm.Role,
                    IsOnline = _conn.IsOnline(gm.UserId),
                    JoinedAt = gm.JoinedAt
                })
                .ToList();

            var creator = await _context.Users.FindAsync(group.CreatedBy);

            return Ok(new
            {
                Group = new GroupResponse
                {
                    GroupId = group.GroupId,
                    Name = group.Name,
                    Description = group.Description,
                    MemberCount = memberResponses.Count,
                    OnlineCount = memberResponses.Count(m => m.IsOnline),
                    CreatedAt = group.CreatedAt,
                    CreatedBy = group.CreatedBy,
                    CreatorName = creator?.Username ?? "Unknown"
                },
                Members = memberResponses
            });
        }

        [HttpGet("{groupId}/members")]
        public async Task<IActionResult> GetGroupMembers(Guid groupId)
        {
            var uid = GetUserId();
            if (uid == null) return Unauthorized();

            if (!await _context.GroupMembers.AnyAsync(gm => gm.GroupId == groupId && gm.UserId == uid.Value && !gm.IsDeleted))
                return Forbid();

            var members = await _context.GroupMembers
                .Include(gm => gm.User)
                .Where(gm => gm.GroupId == groupId && !gm.IsDeleted)
                .ToListAsync();

            var resp = members.Select(gm => new GroupMemberResponse
                {
                    UserId = gm.UserId,
                    Username = gm.User.Username,
                    Role = gm.Role,
                    IsOnline = _conn.IsOnline(gm.UserId),
                    JoinedAt = gm.JoinedAt
                })
                .ToList();

            return Ok(resp);
        }

        [HttpGet]
        public async Task<IActionResult> GetMyGroups()
        {
            var uid = GetUserId();
            if (uid == null) return Unauthorized();

            var result = await _context.GroupMembers
                .Include(gm => gm.Group)
                .Where(gm => gm.UserId == uid.Value && !gm.IsDeleted && !gm.Group.IsDeleted)
                .ToListAsync();

            var groups = new List<GroupResponse>();
            foreach (var gm in result)
            {
                var members = await _context.GroupMembers.Where(m => m.GroupId == gm.GroupId && !m.IsDeleted).ToListAsync();
                groups.Add(new GroupResponse
                {
                    GroupId = gm.Group.GroupId,
                    Name = gm.Group.Name,
                    Description = gm.Group.Description,
                    CreatedAt = gm.Group.CreatedAt,
                    CreatedBy = gm.Group.CreatedBy,
                    MemberCount = members.Count,
                    OnlineCount = members.Count(m => _conn.IsOnline(m.UserId))
                });
            }

            return Ok(groups);
        }

        [HttpPut("{groupId}")]
        public async Task<IActionResult> UpdateGroup(Guid groupId, [FromBody] CreateGroupRequest r)
        {
            var uid = GetUserId();
            if (uid == null) return Unauthorized();

            var group = await _context.Groups.FindAsync(groupId);
            var m = await GetM(groupId, uid.Value);

            if (group == null || m?.Role != "Admin")
                return BadRequest(new { message = "Unauthorized" });

            group.Name = r.Name?.Trim() ?? group.Name;
            group.Description = r.Description?.Trim();
            group.UpdatedAt = DateTime.UtcNow;
            group.UpdatedBy = uid.Value;

            await _context.SaveChangesAsync();
            return Ok(new GroupResponse
            {
                GroupId = group.GroupId,
                Name = group.Name,
                Description = group.Description,
                CreatedAt = group.CreatedAt,
                CreatedBy = group.CreatedBy
            });
        }

        [HttpDelete("{groupId}")]
        public async Task<IActionResult> DeleteGroup(Guid groupId)
        {
            var uid = GetUserId();
            if (uid == null) return Unauthorized();

            var group = await _context.Groups.FindAsync(groupId);
            if (group == null)
                return NotFound();

            // ✅ STEP 1: Creator → direct allow
            if (group.CreatedBy == uid.Value)
            {
                group.IsDeleted = true;
                group.UpdatedAt = DateTime.UtcNow;
                group.UpdatedBy = uid.Value;

                await _context.SaveChangesAsync();
                return Ok(new { message = "Deleted successfully" });
            }

            // ✅ STEP 2: Only Admin (non-creator case)
            var member = await GetM(groupId, uid.Value);

            if (member == null || member.Role != "Admin")
                return Forbid();

            group.IsDeleted = true;
            group.UpdatedAt = DateTime.UtcNow;
            group.UpdatedBy = uid.Value;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Deleted successfully" });
        }

        private async Task<GroupMember?> GetM(Guid gid, Guid uid) => await _context.GroupMembers.FirstOrDefaultAsync(gm => gm.GroupId == gid && gm.UserId == uid && !gm.IsDeleted);
        private Guid? GetUserId() => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
    }
}
