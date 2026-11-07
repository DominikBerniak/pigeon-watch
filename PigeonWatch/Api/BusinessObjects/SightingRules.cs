namespace PigeonWatch.BusinessObjects;

public static class SightingRules
{
    public const int NameMaxLength = 60;
    public const int DescriptionMaxLength = 1000;
    public const int BehaviourMaxLength = 500;
    public const int CooldownSeconds = 60;
    public const int NearbyRadiusKilometers = 10;
    public const int NearbyMaxResults = 200;
    public const int NearbyBoxMaxRows = NearbyMaxResults * 2;
    public const int MineMaxResults = 50;
    public const int ListPageSizeSmall = 10;
    public const int ListPageSizeMedium = 20;
    public const int ListPageSizeLarge = 30;
    public const int ListDefaultPageSize = ListPageSizeSmall;
    public const int PhotoMaxBytes = 2 * 1024 * 1024;
    public const int PhotoMaxDimension = 1600;
    public const int PhotoMaxPixels = 40_000_000;
    public const int PhotoJpegQuality = 85;
    public const string PhotoAllowedContentTypes = "image/jpeg,image/png,image/webp";
    public const int PhotoRequestMaxBytes = PhotoMaxBytes + 512 * 1024;
    public const int PhotoBlobNameMaxLength = 64;
    public const string PhotoBlobExtension = ".jpg";
    public const string PhotoThumbnailBlobSuffix = "-thumb";
    public const int PhotoThumbnailSize = 160;
    public const int PhotoThumbnailJpegQuality = 70;
}
