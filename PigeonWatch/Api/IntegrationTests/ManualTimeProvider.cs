namespace PigeonWatch.IntegrationTests;

public sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private long utcTicks = utcNow.UtcTicks;

    public override DateTimeOffset GetUtcNow()
    {
        return new DateTimeOffset(Interlocked.Read(ref utcTicks), TimeSpan.Zero);
    }

    public void SetUtcNow(DateTimeOffset value)
    {
        Interlocked.Exchange(ref utcTicks, value.UtcTicks);
    }

    public void Advance(TimeSpan delta)
    {
        Interlocked.Add(ref utcTicks, delta.Ticks);
    }
}
