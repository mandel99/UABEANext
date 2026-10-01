using System.Buffers.Binary;
using System.Text;

namespace VideoPlugin;

public static class VideoContainer
{
    public static readonly string[] Extensions =
        ["mp4", "m4v", "mov", "webm", "ogv", "avi", "asf", "wmv", "mpg", "mpeg", "dv", "vp8", "ivf", "3gp", "3g2", "mkv", "ts", "mts", "m2ts"];

    public static string Detect(Stream data, string originalPath)
    {
        var oldPosition = data.Position;
        var bytes = new byte[(int)Math.Min(data.Length - oldPosition, 4096)];
        data.ReadExactly(bytes);
        data.Position = oldPosition;
        var hint = Path.GetExtension(originalPath.Replace('\\', '/')).TrimStart('.').ToLowerInvariant();
        bool At(int start, string text) => bytes.Length >= start + text.Length
            && bytes.AsSpan(start, text.Length).SequenceEqual(Encoding.ASCII.GetBytes(text));

        if (At(4, "ftyp") && bytes.Length >= 12 && BinaryPrimitives.ReadUInt32BigEndian(bytes) >= 12)
        {
            if (At(8, "qt  ")) return "mov";
            if (At(8, "3g2")) return "3g2";
            if (At(8, "3gp")) return "3gp";
            return hint == "m4v" ? "m4v" : "mp4";
        }
        if (At(4, "moov") || At(4, "mdat") || At(4, "wide")) return "mov";
        if (At(0, "RIFF") && At(8, "AVI ")) return "avi";
        if (At(0, "OggS")) return "ogv";
        if (At(0, "DKIF")) return hint == "vp8" ? "vp8" : "ivf";
        if (bytes.AsSpan().StartsWith(new byte[] { 0x30, 0x26, 0xb2, 0x75, 0x8e, 0x66, 0xcf, 0x11,
            0xa6, 0xd9, 0x00, 0xaa, 0x00, 0x62, 0xce, 0x6c })) return hint == "asf" ? "asf" : "wmv";
        if (bytes.AsSpan().StartsWith(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }))
        {
            // EBML DocType (0x4282) identifies WebM vs Matroska; the original path can be stale after transcoding.
            for (int i = 4; i + 3 < bytes.Length; i++)
            {
                if (bytes[i] != 0x42 || bytes[i + 1] != 0x82 || (bytes[i + 2] & 0x80) == 0) continue;
                int length = bytes[i + 2] & 0x7f;
                if (length == 4 && At(i + 3, "webm")) return "webm";
                if (length == 8 && At(i + 3, "matroska")) return "mkv";
            }
            return "mkv";
        }
        if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 1
            && bytes[3] is 0xba or 0xb3) return hint == "mpeg" ? "mpeg" : "mpg";
        if (bytes.Length > 376 && bytes[0] == 0x47 && bytes[188] == 0x47 && bytes[376] == 0x47) return "ts";
        if (bytes.Length > 388 && bytes[4] == 0x47 && bytes[196] == 0x47 && bytes[388] == 0x47) return "m2ts";
        if (bytes.Length >= 80 && bytes[0] == 0x1f && bytes[1] == 0x07 && bytes[2] == 0x00) return "dv";
        // Unknown/platform-specific payloads are still exportable without losing bytes.
        return Extensions.Contains(hint) ? hint : "bin";
    }

}
