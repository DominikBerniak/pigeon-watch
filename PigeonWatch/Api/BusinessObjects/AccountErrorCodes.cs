namespace PigeonWatch.BusinessObjects;

public static class AccountErrorCodes
{
    public const string DuplicateEmail = "DuplicateEmail";
    public const string InvalidEmail = "InvalidEmail";
    public const string DuplicateUserName = "DuplicateUserName";
    public const string InvalidUserName = "InvalidUserName";
    public const string RegistrationFailed = "RegistrationFailed";
    public const string DuplicateDisplayName = "DuplicateDisplayName";
    public const string DisplayNameLength = "DisplayNameLength";
    public const string DisplayNameInvalidCharacter = "DisplayNameInvalidCharacter";
    public const string UserNotFound = "UserNotFound";
    public const string PasswordMismatch = "PasswordMismatch";
    public const string PasswordTooShort = "PasswordTooShort";
    public const string PasswordRequiresDigit = "PasswordRequiresDigit";
    public const string PasswordRequiresLower = "PasswordRequiresLower";
    public const string PasswordRequiresUpper = "PasswordRequiresUpper";
    public const string PasswordRequiresNonAlphanumeric = "PasswordRequiresNonAlphanumeric";
}
