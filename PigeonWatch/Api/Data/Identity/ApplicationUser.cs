namespace PigeonWatch.Data.Identity;

public class ApplicationUser
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string NormalizedEmail { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string NormalizedUserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string SecurityStamp { get; set; } = string.Empty;

    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();

    public DateTimeOffset? LockoutEnd { get; set; }

    public bool LockoutEnabled { get; set; }

    public int AccessFailedCount { get; set; }
}
