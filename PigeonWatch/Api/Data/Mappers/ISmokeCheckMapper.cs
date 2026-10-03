using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Entities;

namespace PigeonWatch.Data.Mappers;

public interface ISmokeCheckMapper
{
    internal SmokeCheckResult ToResult(SmokeCheckEntity entity);
}
