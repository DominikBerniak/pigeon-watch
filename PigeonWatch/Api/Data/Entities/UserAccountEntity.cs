using System.ComponentModel.DataAnnotations;
using PigeonWatch.BusinessObjects;

namespace PigeonWatch.Data.Entities;

internal class UserAccountEntity : AuditableEntity
{
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(256)]
    public string NormalizedEmail { get; set; } = string.Empty;

    [MaxLength(256)]
    public string UserName { get; set; } = string.Empty;

    [MaxLength(256)]
    public string NormalizedUserName { get; set; } = string.Empty;

    [MaxLength(AccountRules.DisplayNameMaxLength)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(AccountRules.DisplayNameMaxLength)]
    public string NormalizedDisplayName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string SecurityStamp { get; set; } = string.Empty;

    public string ConcurrencyStamp { get; set; } = string.Empty;

    public DateTimeOffset? LockoutEnd { get; set; }

    public bool LockoutEnabled { get; set; }

    public int AccessFailedCount { get; set; }
}
