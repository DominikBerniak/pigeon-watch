using PigeonWatch.BusinessObjects;

namespace PigeonWatch.BusinessLogic.Services;

public class SubmissionCooldownProvider(TimeProvider timeProvider) : ISubmissionCooldownProvider
{
    public int? GetRetryAfterSeconds(DateTime? latestSubmission)
    {
        if (latestSubmission is null)
            return null;

        TimeSpan cooldown = TimeSpan.FromSeconds(SightingRules.CooldownSeconds);
        TimeSpan elapsed = timeProvider.GetUtcNow().UtcDateTime - latestSubmission.Value;

        if (elapsed >= cooldown)
            return null;

        return (int)Math.Min(Math.Ceiling((cooldown - elapsed).TotalSeconds), SightingRules.CooldownSeconds);
    }
}
