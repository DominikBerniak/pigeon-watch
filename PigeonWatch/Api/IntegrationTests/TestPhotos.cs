using System.Buffers.Binary;
using SkiaSharp;

namespace PigeonWatch.IntegrationTests;

public sealed class TestPhotos
{
    private static readonly byte[] exifIdentifier = [0x45, 0x78, 0x69, 0x66, 0x00, 0x00];

    public static byte[] Gif { get; } =
    [
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00, 0xFF, 0xFF, 0xFF,
        0x00, 0x00, 0x00, 0x21, 0xF9, 0x04, 0x01, 0x00, 0x00, 0x00, 0x00, 0x2C, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x02, 0x44, 0x01, 0x00, 0x3B
    ];

    public static byte[] JpegWithExif()
    {
        using SKBitmap bitmap = new(64, 48, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (SKCanvas canvas = new(bitmap))
            canvas.Clear(SKColors.SkyBlue);

        using SKData data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);
        byte[] jpeg = data.ToArray();

        return InsertExifSegment(jpeg, CreateOrientationExifPayload());
    }

    public static bool IsJpeg(byte[] bytes)
    {
        return bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[^2] == 0xFF && bytes[^1] == 0xD9;
    }

    public static (int Width, int Height) Dimensions(byte[] jpeg)
    {
        using SKCodec? codec = SKCodec.Create(new MemoryStream(jpeg));

        if (codec is null)
            throw new InvalidOperationException("The bytes are not a readable image.");

        return (codec.Info.Width, codec.Info.Height);
    }

    public static bool ContainsExifSegment(byte[] bytes)
    {
        for (int i = 0; i + 4 + exifIdentifier.Length <= bytes.Length; i++)
        {
            if (bytes[i] == 0xFF && bytes[i + 1] == 0xE1 && bytes.AsSpan(i + 4, exifIdentifier.Length).SequenceEqual(exifIdentifier))
                return true;
        }

        return false;
    }

    private static byte[] InsertExifSegment(byte[] jpeg, byte[] tiffPayload)
    {
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

    private static byte[] CreateOrientationExifPayload()
    {
        byte[] payload = new byte[8 + 2 + 12 + 4];
        payload[0] = (byte)'M';
        payload[1] = (byte)'M';
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2), 0x002A);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(10), 0x0112);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(12), 3);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(14), 1);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(18), 1);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(22), 0);

        return payload;
    }
}
