namespace PigeonWatch.Data.Auditing;

public class SystemCurrentUserProvider : ICurrentUserProvider
{
    public const string SystemUserName = "SYSTEM";

    public string GetCurrentUserName() => SystemUserName;
}
