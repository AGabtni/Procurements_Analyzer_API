namespace ProcurePortal.API.DTOs;

public record RegisterRequest(string Email, string FullName, string Password);

public record LoginRequest(string Email, string Password);

public record AuthResponse(string Token, string Email, string FullName, string Role, bool EmailConfirmed, bool NotificationsEnabled, DateTime? ActivatedAt, int TrialDays);

public record UserDto(int Id, string Email, string FullName, string Role, bool IsActive, DateTime CreatedAt, bool EmailConfirmed, bool NotificationsEnabled, int? CompanyId, string? CompanyName, DateTime? ActivatedAt, int TrialDays, DateTime? LastLogin);

public record UpdateSettingsRequest(string? Email, bool? NotificationsEnabled);

public record SettingsDto(string Email, bool EmailConfirmed, bool NotificationsEnabled);

public record AdminPasswordConfirmRequest(string Password);
