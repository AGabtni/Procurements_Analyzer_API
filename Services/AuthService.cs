using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ProcurePortal.API.Data;
using ProcurePortal.API.DTOs;
using ProcurePortal.API.Models;

namespace ProcurePortal.API.Services;

public class AuthService
{
    private readonly ProcurementsDbContext _db;
    private readonly IConfiguration _config;

    public AuthService(ProcurementsDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == request.Email.ToLower().Trim());

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return null;

        if (!user.IsActive)
            return null;

        user.LastLogin = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var token = GenerateToken(user);
        return new AuthResponse(token, user.Email, user.FullName, user.Role);
    }

    public async Task<(UserDto? User, string? Error)> RegisterAsync(RegisterRequest request)
    {
        var email = request.Email.ToLower().Trim();

        if (await _db.Users.AnyAsync(u => u.Email == email))
            return (null, "Email already registered");

        if (request.Password.Length < 8)
            return (null, "Password must be at least 8 characters");

        var user = new AppUser
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            FullName = request.FullName.Trim(),
            Role = "user",
            IsActive = false,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // Auto-create empty company profile for this user
        var profile = new CompanyProfile
        {
            CompanyName = request.FullName.Trim(),
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.Set<CompanyProfile>().Add(profile);
        await _db.SaveChangesAsync();

        return (ToDto(user), null);
    }

    public async Task<List<UserDto>> GetAllUsersAsync()
    {
        return await _db.Users
            .OrderBy(u => u.CreatedAt)
            .Select(u => ToDto(u))
            .ToListAsync();
    }

    public async Task<bool> SetActiveAsync(int userId, bool active)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return false;
        user.IsActive = active;
        await _db.SaveChangesAsync();
        return true;
    }

    private string GenerateToken(AppUser user)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Role, user.Role),
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(
                int.Parse(_config["Jwt:ExpiryHours"] ?? "24")),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static UserDto ToDto(AppUser u) =>
        new(u.Id, u.Email, u.FullName, u.Role, u.IsActive, u.CreatedAt);
}
