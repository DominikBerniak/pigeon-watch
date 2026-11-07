using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;
using SkiaSharp;

namespace PigeonWatch.UnitTests;

public class PhotoThumbnailProviderTests
{
    private readonly PhotoThumbnailProvider provider = new();

    [Fact]
    public void Landscape_photo_becomes_a_square_thumbnail_of_its_centre()
    {
        byte[] input = CreateBandedJpeg(1200, 400, horizontal: true);

        byte[] thumbnail = provider.CreateThumbnail(input);

        using SKBitmap bitmap = Decode(thumbnail, SightingRules.PhotoThumbnailSize, SightingRules.PhotoThumbnailSize);
        Assert.True(IsReddish(bitmap.GetPixel(4, 4)));
        Assert.True(IsReddish(bitmap.GetPixel(bitmap.Width - 5, bitmap.Height - 5)));
        Assert.True(IsReddish(bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2)));
    }

    [Fact]
    public void Portrait_photo_becomes_a_square_thumbnail_of_its_centre()
    {
        byte[] input = CreateBandedJpeg(400, 1200, horizontal: false);

        byte[] thumbnail = provider.CreateThumbnail(input);

        using SKBitmap bitmap = Decode(thumbnail, SightingRules.PhotoThumbnailSize, SightingRules.PhotoThumbnailSize);
        Assert.True(IsReddish(bitmap.GetPixel(4, 4)));
        Assert.True(IsReddish(bitmap.GetPixel(bitmap.Width - 5, bitmap.Height - 5)));
    }

    [Fact]
    public void Photo_smaller_than_the_thumbnail_is_cropped_but_never_upscaled()
    {
        byte[] input = CreateBandedJpeg(150, 50, horizontal: true);

        byte[] thumbnail = provider.CreateThumbnail(input);

        using SKBitmap bitmap = Decode(thumbnail, 50, 50);
        Assert.True(IsReddish(bitmap.GetPixel(25, 25)));
    }

    [Fact]
    public void Thumbnail_is_smaller_than_the_full_photo()
    {
        byte[] input = CreateBandedJpeg(1600, 1200, horizontal: true);

        byte[] thumbnail = provider.CreateThumbnail(input);

        Assert.True(thumbnail.Length < input.Length);
    }

    [Theory]
    [InlineData(1600, 1200, 400, 300)]
    [InlineData(1200, 1600, 300, 400)]
    [InlineData(1200, 400, 600, 200)]
    [InlineData(150, 50, 150, 50)]
    public void A_jpeg_is_decoded_at_the_smallest_eighth_scale_that_still_covers_the_thumbnail(int width, int height, int decodedWidth, int decodedHeight)
    {
        byte[] input = CreateBandedJpeg(width, height, horizontal: width >= height);
        using SKCodec? codec = SKCodec.Create(new MemoryStream(input));
        Assert.NotNull(codec);

        SKSizeI size = PhotoThumbnailProvider.DecodeSize(codec);

        Assert.Equal(new SKSizeI(decodedWidth, decodedHeight), size);
    }

    [Fact]
    public void A_full_size_normalized_photo_still_becomes_a_centred_thumbnail()
    {
        byte[] input = CreateBandedJpeg(1600, 1200, horizontal: true);

        byte[] thumbnail = provider.CreateThumbnail(input);

        using SKBitmap bitmap = Decode(thumbnail, SightingRules.PhotoThumbnailSize, SightingRules.PhotoThumbnailSize);
        Assert.True(IsReddish(bitmap.GetPixel(4, 4)));
        Assert.True(IsReddish(bitmap.GetPixel(bitmap.Width - 5, bitmap.Height - 5)));
    }

    [Fact]
    public void Unreadable_bytes_throw()
    {
        byte[] input = "not a jpeg"u8.ToArray();

        Assert.Throws<InvalidOperationException>(() => provider.CreateThumbnail(input));
    }

    private static SKBitmap Decode(byte[] jpeg, int expectedWidth, int expectedHeight)
    {
        using (SKCodec? codec = SKCodec.Create(new MemoryStream(jpeg)))
        {
            Assert.NotNull(codec);
            Assert.Equal(SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
            Assert.Equal(expectedWidth, codec.Info.Width);
            Assert.Equal(expectedHeight, codec.Info.Height);
        }

        SKBitmap? bitmap = SKBitmap.Decode(jpeg);
        Assert.NotNull(bitmap);

        return bitmap;
    }

    private static bool IsReddish(SKColor color)
    {
        return color.Red > 150 && color.Green < 90 && color.Blue < 90;
    }

    private static byte[] CreateBandedJpeg(int width, int height, bool horizontal)
    {
        using SKBitmap bitmap = new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (SKCanvas canvas = new(bitmap))
        {
            canvas.Clear(SKColors.Blue);
            using SKPaint centre = new() { Color = SKColors.Red };
            int side = Math.Min(width, height);

            if (horizontal)
                canvas.DrawRect((width - side) / 2f, 0, side, height, centre);
            else
                canvas.DrawRect(0, (height - side) / 2f, width, side, centre);

            Random random = new(width * 7 + height);
            using SKPaint noise = new();
            for (int i = 0; i < 2000; i++)
            {
                noise.Color = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
                float x = horizontal ? random.Next((width - side) / 2) : random.Next(width);
                float y = horizontal ? random.Next(height) : random.Next((height - side) / 2);
                canvas.DrawPoint(x, y, noise);
            }
        }

        using SKData data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);

        return data.ToArray();
    }
}
