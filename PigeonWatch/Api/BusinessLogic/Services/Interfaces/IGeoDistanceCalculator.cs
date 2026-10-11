using PigeonWatch.BusinessObjects;

namespace PigeonWatch.BusinessLogic.Services;

public interface IGeoDistanceCalculator
{
    GeoBoundingBox GetBoundingBox(double latitude, double longitude, double radiusKilometers);
    double GetDistanceKilometers(double fromLatitude, double fromLongitude, double toLatitude, double toLongitude);
}
