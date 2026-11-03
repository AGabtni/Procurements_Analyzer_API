using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ProcurePortal.API.Data;
using ProcurePortal.API.Services;
using Resend;

var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<ProcurementsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
);

// Resend email
builder.Services.AddOptions();
builder.Services.AddHttpClient<ResendClient>();
builder.Services.Configure<ResendClientOptions>(o =>
    o.ApiToken =
        builder.Configuration["Resend:ApiKey"]
        ?? throw new InvalidOperationException("Resend:ApiKey must be configured")
);
builder.Services.AddTransient<IResend, ResendClient>();

// Services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TenderService>();
builder.Services.AddScoped<CompanyService>();
builder.Services.AddScoped<IndustryService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<ExpiryNotificationService>();
builder.Services.AddHostedService<ProcurePortal.API.Workers.ExpiryNotificationWorker>();
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

// JWT Authentication
var jwtKey =
    builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key must be configured");
builder
    .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        };
    });

// Controllers + Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc(
        "v1",
        new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "ProcurePortal API",
            Version = "v1",
            Description = "Canadian procurement tender search and lead generation API",
        }
    );
    options.AddSecurityDefinition(
        "Bearer",
        new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Description = "Enter your JWT token",
        }
    );
    options.AddSecurityRequirement(
        new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                        Id = "Bearer",
                    },
                },
                Array.Empty<string>()
            },
        }
    );
});

// CORS — restrict to configured frontend URL in production
builder.Services.AddCors(options =>
{
    options.AddPolicy(
        "AllowFrontend",
        policy =>
        {
            var frontendUrl =
                builder.Configuration["App:FrontendUrl"]
                ?? throw new InvalidOperationException("App:FrontendUrl must be configured");
            policy.WithOrigins(frontendUrl).AllowAnyMethod().AllowAnyHeader();
        }
    );
});

// Configure Forwarded Headers
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // Caddy's container IP is assigned dynamically on the Docker network.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { code = "tooManyRequests" },
            token
        );
    };

    options.AddPolicy(
        "PwdResetLimitPolicy",
        context =>
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip";
            return RateLimitPartition.GetFixedWindowLimiter(
                ip,
                factory: partition => new FixedWindowRateLimiterOptions()
                {
                    Window = TimeSpan.FromMinutes(5),
                    PermitLimit = 5,
                    AutoReplenishment = true,
                }
            );
        }
    );
});
var app = builder.Build();

app.UseForwardedHeaders();

// Swagger only in development
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "ProcurePortal API v1");
        options.RoutePrefix = string.Empty;
    });
}

app.UseCors("AllowFrontend");

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .AllowAnonymous();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
