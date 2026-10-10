namespace PigeonWatch.BusinessObjects;

public sealed record PasswordChangeResult(bool Succeeded, IReadOnlyList<AccountError> Errors)
{
    public static PasswordChangeResult Success()
    {
        return new(true, []);
    }

    public static PasswordChangeResult Failure(IReadOnlyList<AccountError> errors)
    {
        return new(false, errors);
    }
}
