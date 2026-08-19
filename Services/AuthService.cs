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

    public int TrialDays => int.TryParse(_config["App:TrialDays"], out var d) ? d : 7;

    public async Task<(DateTime? ActivatedAt, int TrialDays)> GetSessionMetaAsync(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        return (user?.ActivatedAt, TrialDays);
    }

    public AuthService(ProcurementsDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<(AuthResponse? Response, string? Error)> LoginAsync(LoginRequest request)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == request.Email.ToLower().Trim());

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return (null, "Invalid credentials");

        if (!user.IsActive)
            return (null, "Account is inactive. Please contact an administrator.");

        user.LastLogin = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var token = GenerateToken(user);
        return (new AuthResponse(token, user.Email, user.FullName, user.Role, user.EmailConfirmed, user.NotificationsEnabled, user.ActivatedAt, TrialDays), null);
    }

    public async Task<bool> VerifyPasswordAsync(int userId, string password)
    {
        var user = await _db.Users.FindAsync(userId);
        return user is not null && BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
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

        return (ToDto(user), null);
    }

    public async Task<List<UserDto>> GetAllUsersAsync()
    {
        return await _db.Users
            .Include(u => u.CompanyProfile)
            .OrderBy(u => u.CreatedAt)
            .Select(u => new UserDto(
                u.Id, u.Email, u.FullName, u.Role, u.IsActive, u.CreatedAt,
                u.EmailConfirmed, u.NotificationsEnabled,
                u.CompanyProfile != null ? u.CompanyProfile.Id : (int?)null,
                u.CompanyProfile != null ? u.CompanyProfile.CompanyName : null,
                u.ActivatedAt, TrialDays, u.LastLogin
            ))
            .ToListAsync();
    }

    public async Task<List<UserDto>> GetUnlinkedUsersAsync()
    {
        return await _db.Users
            .Include(u => u.CompanyProfile)
            .Where(u => u.CompanyProfile == null && u.IsActive)
            .OrderBy(u => u.FullName)
            .Select(u => new UserDto(
                u.Id, u.Email, u.FullName, u.Role, u.IsActive, u.CreatedAt,
                u.EmailConfirmed, u.NotificationsEnabled, null, null, u.ActivatedAt, TrialDays, u.LastLogin
            ))
            .ToListAsync();
    }

    public async Task<(bool Found, DateTime? ActivatedAt)> SetActiveAsync(int userId, bool active)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return (false, null);
        user.IsActive = active;
        user.ActivatedAt = active ? DateTime.UtcNow : null;
        await _db.SaveChangesAsync();
        return (true, user.ActivatedAt);
    }

    public async Task<(string Token, string? Error)> SendConfirmationAsync(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return (null!, "User not found");

        if (user.EmailConfirmed)
            return (null!, "Email already confirmed");

        var token = Guid.NewGuid().ToString("N");
        user.EmailConfirmationToken = token;
        await _db.SaveChangesAsync();

        return (token, null);
    }

    public async Task<bool> ConfirmEmailAsync(string token)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.EmailConfirmationToken == token);

        if (user is null) return false;

        user.EmailConfirmed = true;
        user.EmailConfirmationToken = null;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<SettingsDto?> GetSettingsAsync(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return null;
        return new SettingsDto(user.Email, user.EmailConfirmed, user.NotificationsEnabled);
    }

    public async Task<(SettingsDto? Settings, string? Error)> UpdateSettingsAsync(int userId, UpdateSettingsRequest request)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return (null, "User not found");

        if (request.Email is not null)
        {
            var newEmail = request.Email.ToLower().Trim();
            if (newEmail != user.Email)
            {
                if (await _db.Users.AnyAsync(u => u.Email == newEmail && u.Id != userId))
                    return (null, "Email already in use");

                user.Email = newEmail;
                user.EmailConfirmed = false;
                user.EmailConfirmationToken = null; // will be regenerated on resend
            }
        }

        if (request.NotificationsEnabled is not null)
        {
            user.NotificationsEnabled = request.NotificationsEnabled.Value;
        }

        await _db.SaveChangesAsync();
        return (new SettingsDto(user.Email, user.EmailConfirmed, user.NotificationsEnabled), null);
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

    private UserDto ToDto(AppUser u) =>
        new(u.Id, u.Email, u.FullName, u.Role, u.IsActive, u.CreatedAt, u.EmailConfirmed, u.NotificationsEnabled,
            u.CompanyProfile?.Id, u.CompanyProfile?.CompanyName, u.ActivatedAt, TrialDays, u.LastLogin);
}
