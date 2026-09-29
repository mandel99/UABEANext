using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using System.Security.Cryptography;

namespace TexturePlugin.Helpers;

/// <summary>
/// Read-only workaround for one verified resource with a duplicated 64 KiB prefix.
/// This is not a PS5 layout rule. Never infer an offset from duplicate bytes alone.
/// </summary>
public static class Ps5ResourceCompatibility
{
    private const string ResourceHash = "968AE79155F2DB1D6ECBA772925371D8319A9B58714A54996991ED3E9D563649";
    private const int PrefixSize = 65536;
    private const int ResourceSize = 12230656;

    // Original stream records independently checked against coherent decoded images.
    // Verify metadata as well as file contents, so already-adjusted stream offsets,
    // unrelated files, renamed copies, and embedded replacements are handled safely.
    private static readonly (ulong Offset, uint Size, int Width, int Height, int Format, int Mips)[] Records =
    [
        (0, 65536, 256, 256, 12, 1),
        (65536, 65536, 128, 128, 4, 1),
        (131072, 2097152, 1024, 2048, 25, 1),
        (2228224, 32768, 128, 64, 3, 1),
        (2260992, 65536, 256, 256, 12, 1),
        (2326528, 184320, 572, 308, 12, 1),
        (2510848, 81920, 256, 300, 12, 1),
        (2592768, 8192, 24, 25, 4, 5),
        (2600960, 1720320, 943, 433, 4, 1),
        (4321280, 430080, 450, 200, 4, 1),
        (4751360, 229376, 232, 836, 12, 1),
        (4980736, 163840, 230, 150, 4, 1),
        (5144576, 4194304, 2048, 2048, 25, 1),
        (9338880, 262144, 512, 512, 12, 1),
        (9601024, 401408, 836, 412, 12, 1),
        (10002432, 2097152, 1024, 2048, 12, 1),
        (12099584, 65536, 256, 256, 12, 1)
    ];

    public static byte[]? FillPictureData(TextureFile texture, AssetsFileInstance file)
    {
        if (texture.pictureData is { Length: > 0 })
            return texture.pictureData;

        if (file.parentBundle == null && file.file.Metadata.TargetPlatform == 44
            && texture.m_IsPreProcessed && texture.m_TextureDimension == 2 && texture.m_ImageCount == 1
            && !string.IsNullOrEmpty(texture.m_StreamData.path)
            && Records.Any(r => r.Offset == texture.m_StreamData.offset && r.Size == texture.m_StreamData.size
                && r.Width == texture.m_Width && r.Height == texture.m_Height
                && r.Format == texture.m_TextureFormat && r.Mips == texture.m_MipCount))
        {
            string path = texture.m_StreamData.path;
            if (!Path.IsPathRooted(path))
                path = Path.Combine(Path.GetDirectoryName(file.path)!, path);
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                if (stream.Length == ResourceSize
                    && Convert.ToHexString(SHA256.HashData(stream)) == ResourceHash)
                {
                    // Keep metadata intact. Normal image import embeds its replacement
                    // through TextureFile.WriteTo; it never rewrites this source resource.
                    stream.Position = checked((long)texture.m_StreamData.offset + PrefixSize);
                    var data = new byte[texture.m_StreamData.size];
                    stream.ReadExactly(data);
                    texture.pictureData = data;
                    System.Diagnostics.Trace.WriteLine("PS5 texture: applied verified duplicate-prefix compatibility offset (+65536): " + path);
                    return data;
                }
            }
        }
        return texture.FillPictureData(file);
    }
}
