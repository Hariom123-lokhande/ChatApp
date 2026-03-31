using ChatApp.Infrastructure.Data;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
//using ChatApp.API.Data;
using ChatApp.Application.Services;
using ChatApp.API.Hubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
// 🔥 DATABASE
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
        }));

// 🔌 CONNECTION MANAGER
builder.Services.AddSingleton<IConnectionManager, ConnectionManager>();

// 🔥 SIGNALR
builder.Services.AddSignalR(options =>
{
    var signalRConfig = builder.Configuration.GetSection("SignalR");
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = signalRConfig.GetValue<long>("MaxMessageSizeKB", 64) * 1024;
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
});


// 🔥 CORS
builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// 🔐 JWT AUTHENTICATION
var jwtSettings = builder.Configuration.GetSection("Jwt");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,

        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtSettings["Key"]!))
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;

            // If no token in header or query string, try getting it from cookies
            if (string.IsNullOrEmpty(context.Token))
            {
                accessToken = context.Request.Cookies["chat_token"];
            }

            if (!string.IsNullOrEmpty(accessToken))
            {
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        }
    };
});

// 🔥 CONTROLLERS & SWAGGER
builder.Services.AddControllersWithViews();
//add resources multilanguage
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var cultures = new[] { "en", "ja" };

    options.SetDefaultCulture("en")
        .AddSupportedCultures(cultures)
        .AddSupportedUICultures(cultures);
});
//end of multilanguage

// 🔥 SESSION & CACHE
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    var sessionConfig = builder.Configuration.GetSection("Session");
    var timeout = sessionConfig.GetValue<int>("TimeoutInMinutes", 60);
    
    options.IdleTimeout = TimeSpan.FromMinutes(timeout);
    options.Cookie.HttpOnly = true; 
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() 
        ? CookieSecurePolicy.SameAsRequest 
        : CookieSecurePolicy.Always; 
    options.Cookie.Name = sessionConfig["CookieName"] ?? ".ChatApp.Session";
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ChatApp API",
        Version = "v1",
        Description = "Production-grade real-time chat system API"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});
// 🔥 HEALTH CHECK (NEW)

builder.Services.AddHealthChecks();

var app = builder.Build();
// 🧹 CLEANUP: Reset all users to offline on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var onlineUsers = db.Users.Where(u => u.IsOnline).ToList();
    foreach (var u in onlineUsers)
    {
        u.IsOnline = false;
        u.LastSeen = u.LastSeen ?? DateTime.UtcNow; // Keep existing LastSeen if present
    }
    db.SaveChanges();
}


// 🔥 MIDDLEWARE

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 🔥 GLOBAL ERROR HANDLER (NEW)
app.UseExceptionHandler("/error");

app.Map("/error", () => Results.Problem("Something went wrong"));

// UseDefaultFiles removed — MVC route (Home/Chat) handles default page
app.UseStaticFiles(); // wwwroot assets (images, fonts etc) still served


//multilanguage start
var locOptions = app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>();
app.UseRequestLocalization(locOptions.Value);
app.UseCors("AllowFrontend");
//multilanguage end


app.UseAuthentication();
app.UseAuthorization();
app.UseSession(); // Authentication aur Authorization ke baad, Controllers se pehle

// Controllers (API + MVC Views)
app.MapControllers();

// 🔥 MVC Default Route → Home/Chat
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Chat}/{id?}");

// 🔥 SignalR Hub
app.MapHub<ChatHub>("/chatHub");

// 🔥 HEALTH CHECK ENDPOINT
app.MapHealthChecks("/health");

app.Run();