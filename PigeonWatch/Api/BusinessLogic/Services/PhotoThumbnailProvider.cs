using PigeonWatch.BusinessObjects;
using SkiaSharp;

namespace PigeonWatch.BusinessLogic.Services;

public class PhotoThumbnailProvider : IPhotoThumbnailProvider
{
    private static readonly SKSamplingOptions sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    public byte[] CreateThumbnail(byte[] jpeg)
    {
        using SKCodec? codec = SKCodec.Create(new MemoryStream(jpeg, writable: false), out SKCodecResult createResult);

        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
            throw new InvalidOperationException($"The stored photo could not be read to create its thumbnail ({createResult}).");

        using SKBitmap? source = Decode(codec);

        if (source is null)
            throw new InvalidOperationException("The stored photo could not be decoded to create its thumbnail.");

        source.SetImmutable();
        int side = Math.Min(source.Width, source.Height);
        int targetSide = Math.Min(SightingRules.PhotoThumbnailSize, side);
        SKRect sourceRect = SKRect.Create((source.Width - side) / 2f, (source.Height - side) / 2f, side, side);
        SKRect targetRect = SKRect.Create(0, 0, targetSide, targetSide);

        using SKBitmap target = new(new SKImageInfo(targetSide, targetSide, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (SKCanvas canvas = new(target))
        {
            canvas.Clear(SKColors.White);
            using SKImage image = SKImage.FromBitmap(source);
            canvas.DrawImage(image, sourceRect, targetRect, sampling);
        }

        using SKData encoded = target.Encode(SKEncodedImageFormat.Jpeg, SightingRules.PhotoThumbnailJpegQuality);

        if (encoded is null)
            throw new InvalidOperationException("SkiaSharp failed to encode the photo thumbnail as JPEG.");

        return encoded.ToArray();
    }

    internal static SKSizeI DecodeSize(SKCodec codec)
    {
        int coveredSide = Math.Min(SightingRules.PhotoThumbnailSize, Math.Min(codec.Info.Width, codec.Info.Height));

        for (int eighths = 1; eighths < 8; eighths++)
        {
            SKSizeI scaled = codec.GetScaledDimensions(eighths / 8f);

            if (Math.Min(scaled.Width, scaled.Height) >= coveredSide)
                return scaled;
        }

        return codec.Info.Size;
    }

    private static SKBitmap? Decode(SKCodec codec)
    {
        SKSizeI scaled = DecodeSize(codec);

        if (scaled.Width != codec.Info.Width || scaled.Height != codec.Info.Height)
        {
            SKBitmap? reduced = DecodeAt(codec, scaled, out SKCodecResult reducedResult);

            if (reduced is not null || reducedResult != SKCodecResult.InvalidScale)
                return reduced;
        }

        return DecodeAt(codec, codec.Info.Size, out _);
    }

    private static SKBitmap? DecodeAt(SKCodec codec, SKSizeI size, out SKCodecResult result)
    {
        SKImageInfo info = new(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        SKBitmap bitmap = new(info);
        result = codec.GetPixels(info, bitmap.GetPixels());

        if (result == SKCodecResult.Success)
            return bitmap;

        bitmap.Dispose();

        return null;
    }
}
