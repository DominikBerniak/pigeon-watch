using PigeonWatch.BusinessObjects;

namespace PigeonWatch.BusinessLogic.Services;

public class GeoDistanceCalculator : IGeoDistanceCalculator
{
    private const double earthRadiusKilometers = 6371.0088;
    private const double maxLatitude = 90;
    private const double maxLongitude = 180;

    public GeoBoundingBox GetBoundingBox(double latitude, double longitude, double radiusKilometers)
    {
        double angularRadius = radiusKilometers / earthRadiusKilometers;
        double latitudeDelta = ToDegrees(angularRadius);
        double minLatitude = latitude - latitudeDelta;
        double maxBoxLatitude = latitude + latitudeDelta;

        if (minLatitude <= -maxLatitude || maxBoxLatitude >= maxLatitude)
            return new GeoBoundingBox(Math.Max(minLatitude, -maxLatitude), Math.Min(maxBoxLatitude, maxLatitude), -maxLongitude, maxLongitude);

        double ratio = Math.Sin(angularRadius) / Math.Cos(ToRadians(latitude));

        if (ratio >= 1)
            return new GeoBoundingBox(minLatitude, maxBoxLatitude, -maxLongitude, maxLongitude);

        double longitudeDelta = ToDegrees(Math.Asin(ratio));

        return new GeoBoundingBox(
            minLatitude,
            maxBoxLatitude,
            Math.Max(longitude - longitudeDelta, -maxLongitude),
            Math.Min(longitude + longitudeDelta, maxLongitude));
    }

    public double GetDistanceKilometers(double fromLatitude, double fromLongitude, double toLatitude, double toLongitude)
    {
        double fromLatitudeRadians = ToRadians(fromLatitude);
        double toLatitudeRadians = ToRadians(toLatitude);
        double latitudeDelta = ToRadians(toLatitude - fromLatitude);
        double longitudeDelta = ToRadians(toLongitude - fromLongitude);
        double halfChord = Math.Pow(Math.Sin(latitudeDelta / 2), 2)
            + (Math.Cos(fromLatitudeRadians) * Math.Cos(toLatitudeRadians) * Math.Pow(Math.Sin(longitudeDelta / 2), 2));

        return 2 * earthRadiusKilometers * Math.Asin(Math.Min(1, Math.Sqrt(halfChord)));
    }

    private static double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180;
    }

    private static double ToDegrees(double radians)
    {
        return radians * 180 / Math.PI;
    }
}
