namespace PigeonWatch.BusinessObjects;

public sealed record GeoRadiusFilter(
    GeoBoundingBox Box,
    double CentreLatitudeRadians,
    double CentreLongitudeRadians,
    double CosCentreLatitude,
    double HalfChordThreshold);
