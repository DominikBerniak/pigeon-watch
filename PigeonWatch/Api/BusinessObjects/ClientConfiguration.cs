namespace PigeonWatch.BusinessObjects;

public sealed record ClientConfiguration(
    int PasswordMinLength,
    bool PasswordRequiresDigit,
    bool PasswordRequiresLowercase,
    bool PasswordRequiresUppercase,
    bool PasswordRequiresNonAlphanumeric,
    int DisplayNameMinLength,
    int DisplayNameMaxLength);
