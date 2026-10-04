namespace PigeonWatch.BusinessObjects;

public sealed record AccountCreationResult(bool Succeeded, IReadOnlyList<AccountError> Errors, RegisteredAccount? Account)
{
    public static AccountCreationResult Success(RegisteredAccount account)
    {
        return new(true, [], account);
    }

    public static AccountCreationResult Failure(IReadOnlyList<AccountError> errors)
    {
        return new(false, errors, null);
    }
}
