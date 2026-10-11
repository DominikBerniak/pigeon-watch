using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;

namespace PigeonWatch.UnitTests;

public class GeoDistanceCalculatorTests
{
    private readonly GeoDistanceCalculator calculator = new();

    [Fact]
    public void Distance_between_warsaw_and_krakow_matches_the_great_circle_distance()
    {
        double distance = calculator.GetDistanceKilometers(52.2297, 21.0122, 50.0647, 19.9450);

        Assert.Equal(251.98, distance, 0.05);
    }

    [Fact]
    public void One_degree_of_latitude_is_about_111_kilometers()
    {
        double distance = calculator.GetDistanceKilometers(0, 0, 1, 0);

        Assert.Equal(111.195, distance, 0.01);
    }

    [Fact]
    public void Distance_to_the_same_point_is_zero()
    {
        Assert.Equal(0, calculator.GetDistanceKilometers(52.0, 21.0, 52.0, 21.0), 0.000001);
    }

    [Fact]
    public void Bounding_box_widens_the_longitude_delta_by_the_latitude()
    {
        GeoBoundingBox box = calculator.GetBoundingBox(52.0, 21.0, 10);

        Assert.Equal(52.0 - 0.08993, box.MinLatitude, 0.0001);
        Assert.Equal(52.0 + 0.08993, box.MaxLatitude, 0.0001);
        Assert.Equal(21.0 - 0.14607, box.MinLongitude, 0.0001);
        Assert.Equal(21.0 + 0.14607, box.MaxLongitude, 0.0001);
    }

    [Fact]
    public void Bounding_box_edges_are_the_radius_away_from_the_centre()
    {
        GeoBoundingBox box = calculator.GetBoundingBox(52.0, 21.0, 10);

        Assert.Equal(10, calculator.GetDistanceKilometers(52.0, 21.0, box.MaxLatitude, 21.0), 0.001);
        Assert.Equal(10, calculator.GetDistanceKilometers(52.0, 21.0, box.MinLatitude, 21.0), 0.001);
        Assert.True(calculator.GetDistanceKilometers(52.0, 21.0, 52.0, box.MaxLongitude) >= 10);
    }

    [Fact]
    public void Bounding_box_near_a_pole_spans_every_longitude_and_is_clamped()
    {
        GeoBoundingBox box = calculator.GetBoundingBox(89.95, 21.0, 10);

        Assert.Equal(90, box.MaxLatitude);
        Assert.Equal(-180, box.MinLongitude);
        Assert.Equal(180, box.MaxLongitude);
    }

    [Fact]
    public void Bounding_box_longitude_is_clamped_at_the_antimeridian()
    {
        GeoBoundingBox box = calculator.GetBoundingBox(0, 179.95, 10);

        Assert.Equal(180, box.MaxLongitude);
        Assert.True(box.MinLongitude < 179.95);
    }
}
