using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using PigeonWatch.BusinessLogic.Services;
using PigeonWatch.BusinessObjects;
using SkiaSharp;

namespace PigeonWatch.UnitTests;

public class PhotoNormalizationProviderTests
{
    private static readonly byte[] exifIdentifier = [0x45, 0x78, 0x69, 0x66, 0x00, 0x00];

    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    private readonly PhotoNormalizationProvider provider = new();

    [Fact]
    public async Task Jpeg_with_exif_gps_comes_out_without_an_exif_segment()
    {
        byte[] input = InsertExifSegment(CreateJpeg(640, 480), CreateGpsExifPayload());
        Assert.True(ContainsExifSegment(input));

        PhotoNormalizationResult result = await NormalizeAsync(input);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Jpeg);
        Assert.False(ContainsExifSegment(result.Jpeg));
        AssertJpeg(result.Jpeg, 640, 480);
    }

    [Fact]
    public async Task Large_jpeg_is_scaled_down_to_the_maximum_dimension()
    {
        byte[] input = CreateJpeg(4000, 3000);

        PhotoNormalizationResult result = await NormalizeAsync(input);

        Assert.True(result.Succeeded);
        AssertJpeg(result.Jpeg, 1600, 1200);
    }

    [Fact]
    public async Task Small_png_comes_out_as_a_jpeg_of_the_same_size()
    {
        byte[] input = Encode(CreateBitmap(800, 600), SKEncodedImageFormat.Png, 100);

        PhotoNormalizationResult result = await NormalizeAsync(input);

        Assert.True(result.Succeeded);
        AssertJpeg(result.Jpeg, 800, 600);
    }

    [Fact]
    public async Task Orientation_six_jpeg_comes_out_rotated()
    {
        byte[] input = InsertExifSegment(CreateJpeg(640, 480), CreateOrientationExifPayload(6));
        using (SKCodec? codec = SKCodec.Create(new MemoryStream(input)))
        {
            Assert.NotNull(codec);
            Assert.Equal(SKEncodedOrigin.RightTop, codec.EncodedOrigin);
        }

        PhotoNormalizationResult result = await NormalizeAsync(input);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Jpeg);
        Assert.False(ContainsExifSegment(result.Jpeg));
        AssertJpeg(result.Jpeg, 480, 640);
        using SKBitmap output = SKBitmap.Decode(result.Jpeg);
        Assert.True(IsReddish(output.GetPixel(470, 10)));
        Assert.False(IsReddish(output.GetPixel(10, 10)));
    }

    [Fact]
    public async Task Webp_is_accepted()
    {
        byte[] input = Encode(CreateBitmap(320, 240), SKEncodedImageFormat.Webp, 80);

        PhotoNormalizationResult result = await NormalizeAsync(input);

        Assert.True(result.Succeeded);
        AssertJpeg(result.Jpeg, 320, 240);
    }

    [Fact]
    public async Task Gif_is_rejected_as_an_unsupported_type()
    {
        byte[] input =
        [
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00, 0xFF, 0xFF, 0xFF,
            0x00, 0x00, 0x00, 0x21, 0xF9, 0x04, 0x01, 0x00, 0x00, 0x00, 0x00, 0x2C, 0x00, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x02, 0x44, 0x01, 0x00, 0x3B
        ];

        PhotoNormalizationResult result = await NormalizeAsync(input);

        AssertFailure(result, SightingErrorCodes.PhotoUnsupportedType);
    }

    [Fact]
    public async Task Text_renamed_to_jpg_is_rejected_as_an_unsupported_type()
    {
        byte[] input = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("This is a text file renamed to photo.jpg. ", 20)));

        PhotoNormalizationResult result = await NormalizeAsync(input);

        AssertFailure(result, SightingErrorCodes.PhotoUnsupportedType);
    }

    [Fact]
    public async Task Truncated_jpeg_is_rejected_as_unreadable()
    {
        byte[] complete = CreateJpeg(400, 300);
        byte[] input = complete[..(complete.Length / 2)];

        PhotoNormalizationResult result = await NormalizeAsync(input);

        AssertFailure(result, SightingErrorCodes.PhotoUnreadable);
    }

    [Fact]
    public async Task Oversized_length_is_rejected_as_too_large_before_reading()
    {
        byte[] input = CreateJpeg(64, 48);
        using MemoryStream stream = new(input);

        PhotoNormalizationResult result = await provider.NormalizeAsync(stream, SightingRules.PhotoMaxBytes + 1L, cancellationToken);

        AssertFailure(result, SightingErrorCodes.PhotoTooLarge);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task Stream_longer_than_the_maximum_is_rejected_as_too_large()
    {
        byte[] input = new byte[SightingRules.PhotoMaxBytes + 16];
        using MemoryStream stream = new(input);

        PhotoNormalizationResult result = await provider.NormalizeAsync(stream, 1024, cancellationToken);

        AssertFailure(result, SightingErrorCodes.PhotoTooLarge);
    }

    [Fact]
    public async Task Header_claiming_more_than_the_maximum_pixels_is_rejected_without_decoding()
    {
        byte[] input = CreateBlankPng(10000, 5000);
        Assert.True(input.Length < SightingRules.PhotoMaxBytes);
        using (SKCodec? codec = SKCodec.Create(new MemoryStream(input)))
        {
            Assert.NotNull(codec);
            Assert.Equal(10000, codec.Info.Width);
            Assert.Equal(5000, codec.Info.Height);
        }

        PhotoNormalizationResult result = await NormalizeAsync(input);

        AssertFailure(result, SightingErrorCodes.PhotoUnreadable);
    }

    private async Task<PhotoNormalizationResult> NormalizeAsync(byte[] input)
    {
        using MemoryStream stream = new(input);

        return await provider.NormalizeAsync(stream, input.Length, cancellationToken);
    }

    private static void AssertFailure(PhotoNormalizationResult result, string expectedCode)
    {
        Assert.False(result.Succeeded);
        Assert.Null(result.Jpeg);
        Assert.NotNull(result.Error);
        Assert.Equal(expectedCode, result.Error.Code);
    }

    private static void AssertJpeg(byte[]? bytes, int expectedWidth, int expectedHeight)
    {
        Assert.NotNull(bytes);
        using SKCodec? codec = SKCodec.Create(new MemoryStream(bytes));
        Assert.NotNull(codec);
        Assert.Equal(SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
        Assert.Equal(SKEncodedOrigin.TopLeft, codec.EncodedOrigin);
        Assert.Equal(expectedWidth, codec.Info.Width);
        Assert.Equal(expectedHeight, codec.Info.Height);
    }

    private static bool IsReddish(SKColor color)
    {
        return color.Red > 100 && color.Green < 60 && color.Blue < 60;
    }

    private static SKBitmap CreateBitmap(int width, int height)
    {
        SKBitmap bitmap = new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.SkyBlue);
        Random random = new(width * 31 + height);
        using SKPaint dot = new();
        for (int i = 0; i < 400; i++)
        {
            dot.Color = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            canvas.DrawCircle(random.Next(width), random.Next(height), 1 + random.Next(Math.Max(2, width / 40)), dot);
        }

        using SKPaint marker = new() { Color = SKColors.DarkRed };
        canvas.DrawRect(0, 0, width / 4f, height / 8f, marker);

        return bitmap;
    }

    private static byte[] CreateJpeg(int width, int height)
    {
        using SKBitmap bitmap = CreateBitmap(width, height);

        return Encode(bitmap, SKEncodedImageFormat.Jpeg, 90);
    }

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
        using (bitmap)
        {
            using SKData data = bitmap.Encode(format, quality);

            return data.ToArray();
        }
    }

    private static byte[] InsertExifSegment(byte[] jpeg, byte[] tiffPayload)
    {
        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);
        int segmentLength = 2 + exifIdentifier.Length + tiffPayload.Length;
        using MemoryStream output = new();
        output.Write(jpeg, 0, 2);
        output.WriteByte(0xFF);
        output.WriteByte(0xE1);
        output.WriteByte((byte)(segmentLength >> 8));
        output.WriteByte((byte)(segmentLength & 0xFF));
        output.Write(exifIdentifier);
        output.Write(tiffPayload);
        output.Write(jpeg, 2, jpeg.Length - 2);

        return output.ToArray();
    }

    private static byte[] CreateOrientationExifPayload(ushort orientation)
    {
        byte[] payload = new byte[8 + 2 + 12 + 4];
        WriteTiffHeader(payload);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(8), 1);
        WriteIfdEntry(payload, 10, 0x0112, 3, 1, 0);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(18), orientation);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(22), 0);

        return payload;
    }

    private static byte[] CreateGpsExifPayload()
    {
        const int ifd0Offset = 8;
        const int gpsIfdOffset = ifd0Offset + 2 + 12 + 4;
        const int gpsEntries = 4;
        const int latitudeOffset = gpsIfdOffset + 2 + gpsEntries * 12 + 4;
        const int longitudeOffset = latitudeOffset + 24;
        byte[] payload = new byte[longitudeOffset + 24];
        WriteTiffHeader(payload);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(ifd0Offset), 1);
        WriteIfdEntry(payload, ifd0Offset + 2, 0x8825, 4, 1, gpsIfdOffset);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(ifd0Offset + 14), 0);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(gpsIfdOffset), gpsEntries);
        int entry = gpsIfdOffset + 2;
        WriteIfdEntry(payload, entry, 0x0001, 2, 2, 0);
        payload[entry + 8] = (byte)'N';
        WriteIfdEntry(payload, entry + 12, 0x0002, 5, 3, latitudeOffset);
        WriteIfdEntry(payload, entry + 24, 0x0003, 2, 2, 0);
        payload[entry + 32] = (byte)'E';
        WriteIfdEntry(payload, entry + 36, 0x0004, 5, 3, longitudeOffset);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(entry + 48), 0);
        WriteRationals(payload, latitudeOffset, [52, 1, 13, 1, 4, 1]);
        WriteRationals(payload, longitudeOffset, [21, 1, 0, 1, 42, 1]);

        return payload;
    }

    private static void WriteTiffHeader(byte[] payload)
    {
        payload[0] = (byte)'M';
        payload[1] = (byte)'M';
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2), 0x002A);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4), 8);
    }

    private static void WriteIfdEntry(byte[] payload, int offset, ushort tag, ushort type, uint count, uint value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset), tag);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset + 2), type);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(offset + 4), count);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(offset + 8), value);
    }

    private static void WriteRationals(byte[] payload, int offset, uint[] values)
    {
        for (int i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(offset + i * 4), values[i]);
    }

    private static bool ContainsExifSegment(byte[] bytes)
    {
        for (int i = 0; i + 4 + exifIdentifier.Length <= bytes.Length; i++)
        {
            if (bytes[i] == 0xFF && bytes[i + 1] == 0xE1 && bytes.AsSpan(i + 4, exifIdentifier.Length).SequenceEqual(exifIdentifier))
                return true;
        }

        return false;
    }

    private static byte[] CreateBlankPng(int width, int height)
    {
        using MemoryStream output = new();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 2;
        WritePngChunk(output, "IHDR", header);
        WritePngChunk(output, "IDAT", CompressBlankRows(width * 3 + 1, height));
        WritePngChunk(output, "IEND", []);

        return output.ToArray();
    }

    private static byte[] CompressBlankRows(int rowLength, int rowCount)
    {
        using MemoryStream output = new();
        byte[] row = new byte[rowLength];
        using (ZLibStream zlib = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (int i = 0; i < rowCount; i++)
                zlib.Write(row);
        }

        return output.ToArray();
    }

    private static void WritePngChunk(Stream output, string type, byte[] data)
    {
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        output.Write(typeBytes);
        output.Write(data);
        byte[] crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32([.. typeBytes, .. data]));
        output.Write(crc);
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }

        return ~crc;
    }
}
