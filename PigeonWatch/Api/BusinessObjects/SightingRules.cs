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
}
