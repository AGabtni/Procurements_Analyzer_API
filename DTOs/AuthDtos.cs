namespace ProcurePortal.API.DTOs;

public record RegisterRequest(string Email, string FullName, string Password);

public record LoginRequest(string Email, string Password);

public record AuthResponse(string Token, string Email, string FullName, string Role);

public record UserDto(int Id, string Email, string FullName, string Role, bool IsActive, DateTime CreatedAt);
