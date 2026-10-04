namespace PigeonWatch.BusinessObjects;

public static class AccountRules
{
    public const int PasswordMinLength = 8;
    public const int DisplayNameMinLength = 3;
    public const int DisplayNameMaxLength = 30;
    public const char DisplayNameForbiddenCharacter = '@';
}
