namespace PigeonWatch.BusinessObjects;

public sealed record DatabaseHealthResult(bool IsHealthy, Guid? SmokeCheckId, DateTime? CreatedAtUtc)
{
    public static DatabaseHealthResult Healthy(SmokeCheckResult smokeCheck) =>
        new(true, smokeCheck.Id, smokeCheck.CreatedAtUtc);

    public static DatabaseHealthResult Unhealthy() => new(false, null, null);
}
