using System.Buffers.Binary;

namespace AltomateHR.Api.Modules.Organizations;

// What a company logo file really is, read from its bytes — never from its
// name or the Content-Type the browser sent, both of which the uploader
// controls. Only PNG, JPEG and WebP are recognised: SVG is refused because it
// can carry script, GIF because QuestPDF can't print it.
//
// Pure header parsing (PNG IHDR, JPEG SOFn, WebP VP8 / VP8L / VP8X) — no
// decoder, no new package. Whether the rest of the file decodes is checked
// separately by the logo service.
public static class LogoImage
{
    public sealed record Info(string ContentType, string Extension, int Width, int Height);

    // Null = not a PNG/JPEG/WebP, or a header too damaged to read its size.
    public static Info? Read(ReadOnlySpan<byte> data)
    {
        if (IsPng(data)) return ReadPng(data);
        if (IsJpeg(data)) return ReadJpeg(data);
        if (IsWebP(data)) return ReadWebP(data);
        return null;
    }

    // Whether the whole file is there and intact — the header can be fine on a
    // truncated or damaged file. QuestPDF alone can't tell: FromBinaryData only
    // reads the header, and Skia draws a cut-off image as a half-drawn logo
    // without complaint. So first check the file's own structure (every PNG
    // chunk present with a matching CRC up to IEND; a JPEG that reaches its EOI
    // marker after the scan data; a WebP whose RIFF and chunk sizes fit the
    // file), then let QuestPDF confirm it can open it.
    public static bool CanDecode(byte[] data)
    {
        var complete = IsPng(data) ? PngIsComplete(data)
            : IsJpeg(data) ? JpegIsComplete(data)
            : IsWebP(data) && WebPIsComplete(data);
        if (!complete) return false;

        try
        {
            var image = QuestPDF.Infrastructure.Image.FromBinaryData(data);
            (image as IDisposable)?.Dispose();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ─── PNG ─────────────────────────────────────────────────────────────

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static bool IsPng(ReadOnlySpan<byte> d) => d.Length >= 8 && d[..8].SequenceEqual(PngSignature);

    // The first chunk must be IHDR: width and height as big-endian uint32.
    private static Info? ReadPng(ReadOnlySpan<byte> d)
    {
        if (d.Length < 24 || !d.Slice(12, 4).SequenceEqual("IHDR"u8)) return null;
        var width = BinaryPrimitives.ReadUInt32BigEndian(d.Slice(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(d.Slice(20, 4));
        return Make("image/png", ".png", width, height);
    }

    // ─── JPEG ────────────────────────────────────────────────────────────

    private static bool IsJpeg(ReadOnlySpan<byte> d) => d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF;

    // Walk the marker segments to the first start-of-frame (SOF0–SOF15, less
    // DHT C4, JPG C8 and DAC CC), which holds height then width.
    private static Info? ReadJpeg(ReadOnlySpan<byte> d)
    {
        var i = 2;
        while (i < d.Length)
        {
            if (d[i] != 0xFF) return null;
            while (i < d.Length && d[i] == 0xFF) i++;   // fill bytes
            if (i >= d.Length) return null;
            var marker = d[i++];

            // Stand-alone markers carry no length.
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7)) continue;
            // End of image or start of scan before any frame header: no size.
            if (marker is 0xD9 or 0xDA) return null;

            if (i + 2 > d.Length) return null;
            int length = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(i, 2));
            if (length < 2) return null;

            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (length < 7 || i + 7 > d.Length) return null;
                var height = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(i + 3, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(i + 5, 2));
                return Make("image/jpeg", ".jpg", width, height);
            }

            i += length;
        }
        return null;
    }

    // ─── WebP ────────────────────────────────────────────────────────────

    private static bool IsWebP(ReadOnlySpan<byte> d) =>
        d.Length >= 16 && d[..4].SequenceEqual("RIFF"u8) && d.Slice(8, 4).SequenceEqual("WEBP"u8);

    private static Info? ReadWebP(ReadOnlySpan<byte> d)
    {
        if (d.Length < 30) return null;
        var chunk = d.Slice(12, 4);

        // Lossy: 3-byte frame tag, start code 9D 01 2A, then 14-bit width/height.
        if (chunk.SequenceEqual("VP8 "u8))
        {
            if (d[23] != 0x9D || d[24] != 0x01 || d[25] != 0x2A) return null;
            var width = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(26, 2)) & 0x3FFF;
            var height = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(28, 2)) & 0x3FFF;
            return Make("image/webp", ".webp", (uint)width, (uint)height);
        }

        // Lossless: signature 0x2F, then (width-1) and (height-1) as 14 bits each.
        if (chunk.SequenceEqual("VP8L"u8))
        {
            if (d[20] != 0x2F) return null;
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(21, 4));
            var width = (bits & 0x3FFF) + 1;
            var height = ((bits >> 14) & 0x3FFF) + 1;
            return Make("image/webp", ".webp", width, height);
        }

        // Extended: flags + reserved, then (canvas width-1) and (height-1) as 24 bits.
        if (chunk.SequenceEqual("VP8X"u8))
        {
            var width = (uint)(d[24] | d[25] << 8 | d[26] << 16) + 1;
            var height = (uint)(d[27] | d[28] << 8 | d[29] << 16) + 1;
            return Make("image/webp", ".webp", width, height);
        }

        return null;
    }

    // ─── Completeness ────────────────────────────────────────────────────

    // Every chunk (length, type, data, CRC) fits in the file and its CRC
    // matches, through to IEND. Anything after IEND is ignored.
    private static bool PngIsComplete(ReadOnlySpan<byte> d)
    {
        var i = 8;
        while (i + 12 <= d.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(d.Slice(i, 4));
            if (length > (uint)(d.Length - i - 12)) return false;
            var typeAndData = d.Slice(i + 4, 4 + (int)length);
            var crc = BinaryPrimitives.ReadUInt32BigEndian(d.Slice(i + 8 + (int)length, 4));
            if (Crc32(typeAndData) != crc) return false;
            if (typeAndData[..4].SequenceEqual("IEND"u8)) return true;
            i += 12 + (int)length;
        }
        return false;
    }

    // The marker segments reach a start of scan, and an EOI (FF D9) follows
    // it. Inside scan data FF is always stuffed (FF 00) or a restart/marker,
    // so FF D9 after SOS only appears as the real end; a thumbnail's own EOI
    // sits in APP1, before the scan, and doesn't count.
    private static bool JpegIsComplete(ReadOnlySpan<byte> d)
    {
        var i = 2;
        while (i < d.Length)
        {
            if (d[i] != 0xFF) return false;
            while (i < d.Length && d[i] == 0xFF) i++;
            if (i >= d.Length) return false;
            var marker = d[i++];
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7)) continue;
            if (marker == 0xD9) return false;   // ended before any scan
            if (i + 2 > d.Length) return false;
            int length = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(i, 2));
            if (length < 2 || i + length > d.Length) return false;
            if (marker == 0xDA) return d[(i + length)..].IndexOf([(byte)0xFF, (byte)0xD9]) >= 0;
            i += length;
        }
        return false;
    }

    // The RIFF size covers the whole file's payload, and the chunks inside it
    // (each padded to an even length) fit exactly.
    private static bool WebPIsComplete(ReadOnlySpan<byte> d)
    {
        var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(4, 4));
        if (riffSize < 4 || riffSize > (uint)(d.Length - 8)) return false;
        var end = 8 + (int)riffSize;
        var i = 12;
        var sawImage = false;
        while (i + 8 <= end)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(i + 4, 4));
            var padded = (long)size + (size & 1);
            if (padded > end - i - 8) return false;
            var type = d.Slice(i, 4);
            if (type.SequenceEqual("VP8 "u8) || type.SequenceEqual("VP8L"u8)) sawImage = true;
            i += 8 + (int)padded;
        }
        return sawImage && i == end;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in bytes) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static Info? Make(string contentType, string extension, uint width, uint height) =>
        width is 0 or > int.MaxValue || height is 0 or > int.MaxValue
            ? null
            : new Info(contentType, extension, (int)width, (int)height);
}
