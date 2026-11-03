namespace PigeonWatch.BusinessObjects;

public sealed record PhotoNormalizationResult(bool Succeeded, byte[]? Jpeg, SightingError? Error)
{
    public static PhotoNormalizationResult Success(byte[] jpeg)
    {
        return new(true, jpeg, null);
    }

    public static PhotoNormalizationResult Failure(SightingError error)
    {
        return new(false, null, error);
    }
}
