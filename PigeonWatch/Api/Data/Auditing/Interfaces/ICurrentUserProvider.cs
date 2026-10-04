namespace PigeonWatch.Data.Auditing;

public interface ICurrentUserProvider
{
    string GetCurrentUserName();
}
