namespace PigeonWatch.BusinessObjects;

public static class AccountRules
{
    public const int PasswordMinLength = 8;
    public const bool PasswordRequiresDigit = true;
    public const bool PasswordRequiresLowercase = true;
    public const bool PasswordRequiresUppercase = true;
    public const bool PasswordRequiresNonAlphanumeric = true;
    public const int EmailMaxLength = 256;
    public const int DisplayNameMinLength = 3;
    public const int DisplayNameMaxLength = 30;
    public const char DisplayNameForbiddenCharacter = '@';
}
