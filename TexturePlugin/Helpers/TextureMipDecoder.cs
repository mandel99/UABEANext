using AssetsTools.NET.Texture;

namespace TexturePlugin.Helpers;

/// <summary>Reads stored lower levels; never regenerates mipmaps from the base image.</summary>
public static class TextureMipDecoder
{
    public static IEnumerable<(int Level, int Width, int Height, byte[] Pixels)> Decode(TextureFile t, byte[] raw)
    {
        if (t.m_ImageCount != 1 || t.m_TextureDimension != 2 || t.m_StreamingMipmaps)
            throw new NotSupportedException("Mip preview requires a complete, non-streaming 2D texture.");
        if (t.m_Width <= 0 || t.m_Height <= 0 || t.m_MipCount < 1
            || t.m_MipCount > TextureOperations.GetMaxMipCount(t.m_Width, t.m_Height))
            throw new InvalidDataException("Invalid mip dimensions or count.");
        var format = (TextureFormat)t.m_TextureFormat;
        Ps4MipChain? ps4 = null;
        Ps5MipChain? ps5 = null;
        if (t.swizzleType == SwizzleType.PS4)
        {
            format = Ps4MortonLayout.GetStorageFormat(format);
            ps4 = new(t.m_Width, t.m_Height, format, t.m_MipCount);
            if (raw.Length != ps4.TiledSize) throw new InvalidDataException("Incomplete PS4 mip chain.");
        }
        else if (t.swizzleType == SwizzleType.PS5)
        {
            format = Ps5GfxLayout.GetStorageFormat(format);
            ps5 = Ps5MipChain.ForUnity(t.m_Width, t.m_Height, format, t.m_MipCount, raw.Length);
        }
        else if (t.swizzleType != SwizzleType.None)
            throw new NotSupportedException("Mip preview for this console layout is not implemented.");

        if (!ConsoleTextureElements.TryGetInfo(format, out int block, out int bytes))
        {
            block = 1;
            bytes = format switch { TextureFormat.RGB24 => 3, TextureFormat.RGB48 => 6,
                TextureFormat.RGBFloat => 12, _ => 0 };
            if (bytes == 0) throw new NotSupportedException($"Mip preview is not implemented for {format}.");
        }
        int offset = 0;
        for (int mip = 0; mip < t.m_MipCount; mip++)
        {
            int w = Math.Max(1, t.m_Width >> mip), h = Math.Max(1, t.m_Height >> mip);
            int dw = checked(((w + block - 1) / block) * block);
            int dh = checked(((h + block - 1) / block) * block);
            int size = checked(dw / block * (dh / block) * bytes);
            byte[] linear;
            if (ps4 != null)
            {
                if (mip == 0) continue;
                linear = ps4.Levels[mip].Deswizzle(raw.AsSpan(ps4.Offsets[mip], ps4.Levels[mip].TiledSize));
            }
            else if (ps5 != null)
            {
                if (mip == 0) continue;
                linear = ps5.DeswizzleLevel(raw, mip);
            }
            else
            {
                if (offset > raw.Length - size) throw new InvalidDataException($"Missing data for mip {mip}.");
                int start = offset; offset = checked(offset + size);
                if (mip == 0) continue;
                linear = raw.AsSpan(start, size).ToArray();
            }
            var decoded = TextureFile.DecodeManagedData(linear, format, dw, dh)
                ?? throw new InvalidDataException($"Could not decode mip {mip}.");
            var cropped = new byte[checked(w * h * 4)];
            for (int y = 0; y < h; y++) Buffer.BlockCopy(decoded, y * dw * 4, cropped, y * w * 4, w * 4);
            yield return (mip, w, h, cropped);
        }
    }
}
