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
    private static readonly string[] SupportedLocales = { "en-CA", "fr-CA" };
    private const string DefaultLocale = "en-CA";

    private readonly ProcurementsDbContext _db;
    private readonly IConfiguration _config;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public int TrialDays => int.TryParse(_config["App:TrialDays"], out var d) ? d : 14;

    public async Task<(DateTime? ActivatedAt, int TrialDays, string? SubscriptionStatus, DateTime? TrialEndsAt, int? CompanyId, string Locale, string CommsLocale)> GetSessionMetaAsync(int userId)
    {
        var user = await _db.Users
            .Include(u => u.CompanyProfile)
            .Include(u => u.Preferences)
            .FirstOrDefaultAsync(u => u.Id == userId);
        return (
            user?.ActivatedAt,
            TrialDays,
            SubscriptionStatusOf(user),
            user?.CompanyProfile?.TrialEndsAt,
            user?.CompanyId,
            user?.Preferences?.Locale ?? DefaultLocale,
            user?.Preferences?.CommsLocale ?? DefaultLocale
        );
    }

    private static string? SubscriptionStatusOf(AppUser? user) =>
        user?.CompanyProfile is null ? null : CompanyService.EffectiveStatus(user.CompanyProfile);

    private static bool IsSupportedLocale(string? locale) =>
        locale is not null && Array.IndexOf(SupportedLocales, locale) >= 0;

    // Reads Accept-Language and returns 'fr-CA' if the top preference starts with 'fr', else 'en-CA'.
    private string InferLocaleFromRequest()
    {
        var header = _httpContextAccessor.HttpContext?.Request.Headers.AcceptLanguage.ToString();
        if (string.IsNullOrWhiteSpace(header)) return DefaultLocale;
        var first = header.Split(',')[0].Split(';')[0].Trim();
        return first.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? "fr-CA" : DefaultLocale;
    }

    public AuthService(ProcurementsDbContext db, IConfiguration config, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _config = config;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<(AuthResponse? Response, string? Error)> LoginAsync(LoginRequest request)
    {
        var user = await _db.Users
            .Include(u => u.CompanyProfile)
            .Include(u => u.Preferences)
            .FirstOrDefaultAsync(u => u.Email == request.Email.ToLower().Trim());

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return (null, "invalidCredentials");

        if (!user.IsActive)
            return (null, "accountInactive");

        user.LastLogin = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var token = GenerateToken(user);
        return (new AuthResponse(token, user.Email, user.FullName, user.Role, user.EmailConfirmed,
            user.Preferences?.NotificationsEnabled ?? true,
            user.ActivatedAt, TrialDays, SubscriptionStatusOf(user),
            user.CompanyProfile?.TrialEndsAt, user.CompanyId,
            user.Preferences?.Locale ?? DefaultLocale,
            user.Preferences?.CommsLocale ?? DefaultLocale), null);
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
            return (null, "emailRegistered");

        if (request.Password.Length < 8)
            return (null, "passwordTooShort");

        var inferredLocale = InferLocaleFromRequest();
        var user = new AppUser
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            FullName = request.FullName.Trim(),
            Role = "user",
            IsActive = false,
            Preferences = new UserPreferences
            {
                Locale = inferredLocale,
                CommsLocale = inferredLocale,
            },
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
                u.EmailConfirmed,
                u.Preferences != null ? u.Preferences.NotificationsEnabled : true,
                u.CompanyProfile != null ? u.CompanyProfile.Id : (int?)null,
                u.CompanyProfile != null ? u.CompanyProfile.CompanyName : null,
                u.ActivatedAt, TrialDays, u.LastLogin,
                u.Preferences != null ? u.Preferences.Locale : DefaultLocale,
                u.Preferences != null ? u.Preferences.CommsLocale : DefaultLocale
            ))
            .ToListAsync();
    }

    public async Task<List<UserDto>> GetUnlinkedUsersAsync()
    {
        return await _db.Users
            .Where(u => u.CompanyId == null && u.IsActive)
            .OrderBy(u => u.FullName)
            .Select(u => new UserDto(
                u.Id, u.Email, u.FullName, u.Role, u.IsActive, u.CreatedAt,
                u.EmailConfirmed,
                u.Preferences != null ? u.Preferences.NotificationsEnabled : true,
                null, null, u.ActivatedAt, TrialDays, u.LastLogin,
                u.Preferences != null ? u.Preferences.Locale : DefaultLocale,
                u.Preferences != null ? u.Preferences.CommsLocale : DefaultLocale
            ))
            .ToListAsync();
    }

    public async Task<(bool Found, string? Locale, string? Error)> UpdateLocaleAsync(int userId, string locale)
    {
        if (!IsSupportedLocale(locale))
            return (false, null, "unsupportedLocale");

        var prefs = await _db.UserPreferences.FindAsync(userId);
        if (prefs is null) return (false, null, "userNotFound");

        prefs.Locale = locale;
        await _db.SaveChangesAsync();
        return (true, prefs.Locale, null);
    }

    public async Task<(bool Found, string? CommsLocale, string? Error)> UpdateCommsLocaleAsync(int userId, string commsLocale)
    {
        if (!IsSupportedLocale(commsLocale))
            return (false, null, "unsupportedLocale");

        var prefs = await _db.UserPreferences.FindAsync(userId);
        if (prefs is null) return (false, null, "userNotFound");

        prefs.CommsLocale = commsLocale;
        await _db.SaveChangesAsync();
        return (true, prefs.CommsLocale, null);
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

    public async Task<(string Token, string? CommsLocale, string? Error)> SendConfirmationAsync(int userId)
    {
        var user = await _db.Users
            .Include(u => u.Preferences)
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return (null!, null, "userNotFound");

        if (user.EmailConfirmed)
            return (null!, null, "emailAlreadyConfirmed");

        var token = Guid.NewGuid().ToString("N");
        user.EmailConfirmationToken = token;
        await _db.SaveChangesAsync();

        return (token, user.Preferences?.CommsLocale, null);
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
        var user = await _db.Users
            .Include(u => u.Preferences)
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return null;
        var p = user.Preferences;
        return new SettingsDto(user.Email, user.EmailConfirmed,
            p?.NotificationsEnabled ?? true,
            p?.NotifyExpiryEnabled ?? false,
            p?.NotifyHour ?? 8);
    }

    public async Task<(SettingsDto? Settings, string? Error)> UpdateSettingsAsync(int userId, UpdateSettingsRequest request)
    {
        var user = await _db.Users
            .Include(u => u.Preferences)
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return (null, "userNotFound");

        if (request.Email is not null)
        {
            var newEmail = request.Email.ToLower().Trim();
            if (newEmail != user.Email)
            {
                if (await _db.Users.AnyAsync(u => u.Email == newEmail && u.Id != userId))
                    return (null, "emailInUse");

                user.Email = newEmail;
                user.EmailConfirmed = false;
                user.EmailConfirmationToken = null;
            }
        }

        var prefs = user.Preferences ?? new UserPreferences { UserId = userId };
        if (user.Preferences is null)
            _db.UserPreferences.Add(prefs);

        if (request.NotificationsEnabled is not null)
            prefs.NotificationsEnabled = request.NotificationsEnabled.Value;

        if (request.NotifyExpiryEnabled is not null)
            prefs.NotifyExpiryEnabled = request.NotifyExpiryEnabled.Value;

        if (request.NotifyHour is not null)
            prefs.NotifyHour = (short)Math.Clamp((int)request.NotifyHour.Value, 0, 23);

        await _db.SaveChangesAsync();
        return (new SettingsDto(user.Email, user.EmailConfirmed,
            prefs.NotificationsEnabled, prefs.NotifyExpiryEnabled, prefs.NotifyHour), null);
    }

    public async Task<string?> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return "userNotFound";
        if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
            return "currentPasswordIncorrect";

        if (BCrypt.Net.BCrypt.Verify(newPassword, user.PasswordHash))
            return "newPasswordSameAsCurrent";

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _db.SaveChangesAsync();
        return null;
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
        new(u.Id, u.Email, u.FullName, u.Role, u.IsActive, u.CreatedAt, u.EmailConfirmed,
            u.Preferences?.NotificationsEnabled ?? true,
            u.CompanyProfile?.Id, u.CompanyProfile?.CompanyName, u.ActivatedAt, TrialDays, u.LastLogin,
            u.Preferences?.Locale ?? DefaultLocale,
            u.Preferences?.CommsLocale ?? DefaultLocale);
}
