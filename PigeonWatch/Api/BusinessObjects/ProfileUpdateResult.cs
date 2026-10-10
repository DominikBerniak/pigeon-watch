namespace PigeonWatch.BusinessObjects;

public sealed record ProfileUpdateResult(bool Succeeded, IReadOnlyList<AccountError> Errors, UpdatedProfile? Profile)
{
    public static ProfileUpdateResult Success(UpdatedProfile profile)
    {
        return new(true, [], profile);
    }

    public static ProfileUpdateResult Failure(IReadOnlyList<AccountError> errors)
    {
        return new(false, errors, null);
    }
}
