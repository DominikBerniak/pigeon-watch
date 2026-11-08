namespace PigeonWatch.BusinessObjects;

public sealed record SightingThreadResult(bool Succeeded, IReadOnlyList<SightingError> Errors, SightingThread? Thread, SightingMessage? Message)
{
    public static SightingThreadResult Success(SightingThread thread, SightingMessage message)
    {
        return new(true, [], thread, message);
    }

    public static SightingThreadResult Failure(IReadOnlyList<SightingError> errors)
    {
        return new(false, errors, null, null);
    }
}
