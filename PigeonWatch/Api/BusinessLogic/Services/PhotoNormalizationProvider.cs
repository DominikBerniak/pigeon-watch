using PigeonWatch.BusinessObjects;
using SkiaSharp;

namespace PigeonWatch.BusinessLogic.Services;

public class PhotoNormalizationProvider : IPhotoNormalizationProvider
{
    private static readonly IReadOnlySet<SKEncodedImageFormat> allowedFormats = new HashSet<SKEncodedImageFormat>
    {
        SKEncodedImageFormat.Jpeg,
        SKEncodedImageFormat.Png,
        SKEncodedImageFormat.Webp
    };

    private static readonly SKSamplingOptions sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    public async Task<PhotoNormalizationResult> NormalizeAsync(Stream input, long length, CancellationToken cancellationToken = default)
    {
        if (length > SightingRules.PhotoMaxBytes)
            return TooLarge();

        byte[]? bytes = await ReadLimitedAsync(input, cancellationToken);

        if (bytes is null)
            return TooLarge();

        if (bytes.Length == 0)
            return Unreadable("The photo is empty.");

        return Normalize(bytes);
    }

    private static async Task<byte[]?> ReadLimitedAsync(Stream input, CancellationToken cancellationToken = default)
    {
        using MemoryStream buffer = new();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > SightingRules.PhotoMaxBytes)
                return null;

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static PhotoNormalizationResult Normalize(byte[] bytes)
    {
        using SKCodec? codec = SKCodec.Create(new MemoryStream(bytes, writable: false), out SKCodecResult createResult);

        if (codec is null && createResult == SKCodecResult.Unimplemented)
            return UnsupportedType();

        if (codec is null)
            return Unreadable($"The photo could not be read ({createResult}).");

        if (!allowedFormats.Contains(codec.EncodedFormat))
            return UnsupportedType();

        SKImageInfo encodedInfo = codec.Info;

        if (encodedInfo.Width <= 0 || encodedInfo.Height <= 0)
            return Unreadable("The photo has no dimensions.");

        if ((long)encodedInfo.Width * encodedInfo.Height > SightingRules.PhotoMaxPixels)
            return Unreadable($"The photo must not exceed {SightingRules.PhotoMaxPixels} pixels.");

        using SKBitmap? source = Decode(codec);

        if (source is null)
            return Unreadable("The photo could not be decoded.");

        SKEncodedOrigin origin = codec.EncodedOrigin;
        bool swapsAxes = SwapsAxes(origin);
        int orientedWidth = swapsAxes ? source.Height : source.Width;
        int orientedHeight = swapsAxes ? source.Width : source.Height;
        int longestEdge = Math.Max(orientedWidth, orientedHeight);
        float scale = longestEdge > SightingRules.PhotoMaxDimension ? (float)SightingRules.PhotoMaxDimension / longestEdge : 1f;
        int targetWidth = Math.Max(1, (int)Math.Round(orientedWidth * scale));
        int targetHeight = Math.Max(1, (int)Math.Round(orientedHeight * scale));

        using SKBitmap target = new(new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (SKCanvas canvas = new(target))
        {
            canvas.Clear(SKColors.White);
            canvas.Scale(scale);
            canvas.Concat(GetOrientationMatrix(origin, source.Width, source.Height));
            using SKImage image = SKImage.FromBitmap(source);
            canvas.DrawImage(image, 0, 0, sampling);
        }

        using SKData encoded = target.Encode(SKEncodedImageFormat.Jpeg, SightingRules.PhotoJpegQuality);

        if (encoded is null)
            throw new InvalidOperationException("SkiaSharp failed to encode the normalized photo as JPEG.");

        return PhotoNormalizationResult.Success(encoded.ToArray());
    }

    private static SKBitmap? Decode(SKCodec codec)
    {
        SKSizeI scaled = ScaledDecodeSize(codec);

        if (scaled.Width != codec.Info.Width || scaled.Height != codec.Info.Height)
        {
            SKBitmap? reduced = DecodeAt(codec, scaled, out SKCodecResult reducedResult);

            if (reduced is not null || reducedResult != SKCodecResult.InvalidScale)
                return reduced;
        }

        return DecodeAt(codec, codec.Info.Size, out _);
    }

    private static SKSizeI ScaledDecodeSize(SKCodec codec)
    {
        if (Math.Max(codec.Info.Width, codec.Info.Height) <= SightingRules.PhotoMaxDimension)
            return codec.Info.Size;

        for (int eighths = 1; eighths < 8; eighths++)
        {
            SKSizeI scaled = codec.GetScaledDimensions(eighths / 8f);

            if (Math.Max(scaled.Width, scaled.Height) >= SightingRules.PhotoMaxDimension)
                return scaled;
        }

        return codec.Info.Size;
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

    private static bool SwapsAxes(SKEncodedOrigin origin)
    {
        return origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
    }

    private static SKMatrix GetOrientationMatrix(SKEncodedOrigin origin, int width, int height)
    {
        return origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, width, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, width, 0, -1, height, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, height, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, height, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, height, -1, 0, width, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, width, 0, 0, 1),
            _ => SKMatrix.Identity
        };
    }

    private static PhotoNormalizationResult TooLarge()
    {
        return PhotoNormalizationResult.Failure(
            new SightingError(SightingErrorCodes.PhotoTooLarge, $"The photo must not exceed {SightingRules.PhotoMaxBytes} bytes."));
    }

    private static PhotoNormalizationResult UnsupportedType()
    {
        return PhotoNormalizationResult.Failure(
            new SightingError(SightingErrorCodes.PhotoUnsupportedType, "The photo must be a JPEG, PNG or WebP image."));
    }

    private static PhotoNormalizationResult Unreadable(string description)
    {
        return PhotoNormalizationResult.Failure(new SightingError(SightingErrorCodes.PhotoUnreadable, description));
    }
}
