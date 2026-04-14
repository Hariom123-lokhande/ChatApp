using Microsoft.AspNetCore.Mvc;
using ChatApp.Infrastructure.Data;
using ChatApp.Domain.Entities;
using ChatApp.Application.DTOs;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthController(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                return BadRequest(new { message = "Email is required" });

            if (string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { message = "Password is required" });

            if (string.IsNullOrWhiteSpace(request.Username))
                return BadRequest(new { message = "Username is required" });

            var security = _configuration.GetSection("Security");
            var minLen = security.GetValue<int>("MinPasswordLength", 6);
            if (request.Password.Length < minLen)
                return BadRequest(new { message = $"Password must be at least {minLen} characters" });

            if (security.GetValue<bool>("RequireUppercase", true) && !request.Password.Any(char.IsUpper))
                return BadRequest(new { message = "Password must contain at least one uppercase letter" });

            if (security.GetValue<bool>("RequireLowercase", true) && !request.Password.Any(char.IsLower))
                return BadRequest(new { message = "Password must contain at least one lowercase letter" });

            if (security.GetValue<bool>("RequireDigit", true) && !request.Password.Any(char.IsDigit))
                return BadRequest(new { message = "Password must contain at least one number (0-9)" });

            if (security.GetValue<bool>("RequireSpecialCharacter", true) && !request.Password.Any(ch => !char.IsLetterOrDigit(ch)))
                return BadRequest(new { message = "Password must contain at least one special character (e.g. !@$#%&)" });

            // Check email format
            if (!request.Email.Contains('@'))
                return BadRequest(new { message = "Invalid email format" });

            if (await _context.Users.AnyAsync(x => x.Email == request.Email))
                return BadRequest(new { message = "Email already exists" });

            if (await _context.Users.AnyAsync(x => x.Username == request.Username))
                return BadRequest(new { message = "Username already exists" });

            var user = new User
            {
                UserId = Guid.NewGuid(),
                Username = request.Username.Trim(),
                Email = request.Email.Trim().ToLower(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                ProfilePictureUrl = request.ProfilePictureUrl,
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "User registered successfully",
                userId = user.UserId,
                username = user.Username
            });
        }

       
        /// 🔥 LOGIN - Authenticate and get JWT token
       
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { message = "Email and password are required" });

            var user = await _context.Users
                .FirstOrDefaultAsync(x => x.Email == request.Email.Trim().ToLower());

            if (user == null)
                return Unauthorized(new { message = "Invalid email or password" });

            if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                return Unauthorized(new { message = "Invalid email or password" });

            // 🔥 JWT TOKEN GENERATION
            var jwtSettings = _configuration.GetSection("Jwt");

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email)
            };

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSettings["Key"]!));

            var durationMinutes = int.TryParse(jwtSettings["DurationInMinutes"], out var d) ? d : 60;

            var token = new JwtSecurityToken(
                issuer: jwtSettings["Issuer"],
                audience: jwtSettings["Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(durationMinutes),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
            );

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = false, 
                SameSite = SameSiteMode.Lax, 
                Expires = DateTime.UtcNow.AddMinutes(durationMinutes),
                Path = "/"
            };

            Response.Cookies.Append("chat_token", tokenString, cookieOptions);

            // 🔥 SESSION SET (Same browser tabs k liye)
            HttpContext.Session.SetString("UserId", user.UserId.ToString());

            // Update last seen
            user.LastSeen = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new LoginResponse
            {
                Token = tokenString,
                UserId = user.UserId,
                Username = user.Username,
                Email = user.Email,
                ProfilePictureUrl = user.ProfilePictureUrl
            });
        }

        /// <summary>
        /// 🚪 LOGOUT - Clear the authentication cookie
        /// </summary>
        [HttpPost("logout")]
        public IActionResult Logout()
        {
            Response.Cookies.Delete("chat_token");
            HttpContext.Session.Clear(); // 🔥 SESSION CLEAR (Logout hone par)
            return Ok(new { message = "Logged out successfully" });
        }

        /// <summary>
        /// 🔍 Verify token validity
        /// </summary>
        [HttpGet("verify")]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public IActionResult VerifyToken()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var username = User.FindFirst(ClaimTypes.Name)?.Value;
            var email = User.FindFirst(ClaimTypes.Email)?.Value;

            return Ok(new
            {
                valid = true,
                userId,
                username,
                email
            });
        }
    }
}