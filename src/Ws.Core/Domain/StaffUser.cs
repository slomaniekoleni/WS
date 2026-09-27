namespace Ws.Core.Domain;

public enum StaffRole
{
    /// <summary>Salon owner/admin: everything.</summary>
    Owner,
    /// <summary>An artist's own login (linked via ArtistId). For now same rights as Owner; scoping comes later.</summary>
    Artist,
}

/// <summary>A login to the admin panel.</summary>
public sealed class StaffUser
{
    public int Id { get; set; }
    public int SalonId { get; set; }
    public string Login { get; set; } = "";
    public string DisplayName { get; set; } = "";
    /// <summary>ASP.NET Core Identity password hash (PBKDF2), never the password itself.</summary>
    public string PasswordHash { get; set; } = "";
    public StaffRole Role { get; set; }
    public int? ArtistId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
}
