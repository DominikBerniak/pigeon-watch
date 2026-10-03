using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Entities;

namespace PigeonWatch.Data.Mappers;

public class SmokeCheckMapper : ISmokeCheckMapper
{
    SmokeCheckResult ISmokeCheckMapper.ToResult(SmokeCheckEntity entity) =>
        new(entity.Id, DateTime.SpecifyKind(entity.CreateDate, DateTimeKind.Utc));
}
