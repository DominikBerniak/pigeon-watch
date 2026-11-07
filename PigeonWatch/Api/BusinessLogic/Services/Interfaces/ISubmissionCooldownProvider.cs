namespace PigeonWatch.BusinessLogic.Services;

public interface ISubmissionCooldownProvider
{
    int? GetRetryAfterSeconds(DateTime? latestSubmission);
}
