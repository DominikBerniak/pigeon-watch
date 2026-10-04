namespace PigeonWatch.BusinessObjects;

public sealed record AccountCreationResult(bool Succeeded, IReadOnlyList<AccountError> Errors, RegisteredAccount? Account)
{
    public static AccountCreationResult Success(RegisteredAccount account) => new(true, [], account);

    public static AccountCreationResult Failure(IReadOnlyList<AccountError> errors) => new(false, errors, null);
}
