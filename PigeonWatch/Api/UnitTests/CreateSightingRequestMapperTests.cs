using Microsoft.AspNetCore.Http;
using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Mappers;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.UnitTests;

public class CreateSightingRequestMapperTests
{
    private readonly CreateSightingRequestMapper mapper = new();

    [Fact]
    public void Map_without_a_photo_part_maps_no_photo()
    {
        NewSighting sighting = mapper.Map(Guid.NewGuid(), Request(null));

        Assert.Null(sighting.Photo);
        Assert.Null(sighting.PhotoBlobName);
    }

    [Fact]
    public void Map_treats_an_empty_photo_part_as_no_photo()
    {
        using MemoryStream empty = new();
        FormFile file = new(empty, 0, 0, "photo", "photo.jpg");

        NewSighting sighting = mapper.Map(Guid.NewGuid(), Request(file));

        Assert.Null(sighting.Photo);
    }

    [Fact]
    public void Map_passes_a_non_empty_photo_part_with_its_length()
    {
        byte[] bytes = [0xFF, 0xD8, 0xFF, 0xD9];
        using MemoryStream content = new(bytes);
        FormFile file = new(content, 0, bytes.Length, "photo", "photo.jpg");

        NewSighting sighting = mapper.Map(Guid.NewGuid(), Request(file));

        Assert.NotNull(sighting.Photo);
        Assert.Equal(bytes.Length, sighting.Photo.Length);
        using MemoryStream copy = new();
        sighting.Photo.Content.CopyTo(copy);
        Assert.Equal(bytes, copy.ToArray());
    }

    private static CreateSightingRequestModel Request(IFormFile? photo)
    {
        return new()
        {
            Name = "Grey",
            Description = "Limping near the fountain",
            Behaviour = "Cannot fly",
            Latitude = 52.2297,
            Longitude = 21.0122,
            Status = 1,
            Criticality = 2,
            Photo = photo
        };
    }
}
