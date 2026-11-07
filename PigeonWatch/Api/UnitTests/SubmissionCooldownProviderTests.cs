using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;

namespace PigeonWatch.UnitTests;

public class SubmissionCooldownProviderTests
{
    private static readonly DateTimeOffset now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly SubmissionCooldownProvider provider = new(new ManualTimeProvider(now));

    [Fact]
    public void No_latest_submission_has_no_cooldown()
    {
        Assert.Null(provider.GetRetryAfterSeconds(null));
    }

    [Fact]
    public void A_submission_just_under_the_cooldown_ago_leaves_one_second()
    {
        DateTime latest = now.UtcDateTime.AddSeconds(-SightingRules.CooldownSeconds).AddMilliseconds(500);

        Assert.Equal(1, provider.GetRetryAfterSeconds(latest));
    }

    [Fact]
    public void A_submission_exactly_the_cooldown_ago_is_allowed()
    {
        DateTime latest = now.UtcDateTime.AddSeconds(-SightingRules.CooldownSeconds);

        Assert.Null(provider.GetRetryAfterSeconds(latest));
    }

    [Fact]
    public void A_submission_longer_than_the_cooldown_ago_is_allowed()
    {
        DateTime latest = now.UtcDateTime.AddMinutes(-5);

        Assert.Null(provider.GetRetryAfterSeconds(latest));
    }

    [Fact]
    public void A_submission_at_this_moment_leaves_the_whole_cooldown()
    {
        Assert.Equal(SightingRules.CooldownSeconds, provider.GetRetryAfterSeconds(now.UtcDateTime));
    }

    [Fact]
    public void A_submission_in_the_future_is_capped_at_the_whole_cooldown()
    {
        DateTime latest = now.UtcDateTime.AddSeconds(30);

        Assert.Equal(SightingRules.CooldownSeconds, provider.GetRetryAfterSeconds(latest));
    }
}
