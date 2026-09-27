using AssetsTools.NET.Texture;
using StbImageSharp;

namespace TexturePlugin.Helpers;

public static class Ps4TextureCodec
{
    private static Ps4MortonLayout GetLayout(TextureFile texture)
    {
        if (texture.m_ImageCount != 1 || texture.m_TextureDimension != 2)
            throw new NotSupportedException("PS4 Morton supports single 2D textures only.");
        return new Ps4MortonLayout(texture.m_Width, texture.m_Height, (TextureFormat)texture.m_TextureFormat);
    }

    public static byte[] ExportPng(TextureFile texture, byte[]? original)
    {
        var layout = GetLayout(texture);
        if (original is null)
            throw new InvalidDataException("Texture data is missing. Check the external .resS file.");
        // Reorder compressed blocks BEFORE the decoder discards the padded tail.
        var linear = layout.Deswizzle(original);
        // Some BC decoders require complete 4x4 output blocks (notably BC7).
        // Decode whole blocks, crop in texture coordinates, then flip for PNG.
        int decodedWidth = checked(layout.BlocksWide * 4);
        int decodedHeight = checked(layout.BlocksHigh * 4);
        var decoded = TextureFile.DecodeManagedData(linear, (TextureFormat)texture.m_TextureFormat,
            decodedWidth, decodedHeight, false);
        if (decoded is null)
            throw new InvalidDataException("Failed to decode the PS4 texture.");
        var pixels = new byte[checked(texture.m_Width * texture.m_Height * 4)];
        for (int y = 0; y < texture.m_Height; y++)
            decoded.AsSpan(y * decodedWidth * 4, texture.m_Width * 4)
                .CopyTo(pixels.AsSpan(y * texture.m_Width * 4));
        TextureOperations.FlipBGRA32VerticallyInplace(pixels, texture.m_Width, texture.m_Height);
        using var output = new MemoryStream();
        if (!TextureOperations.WriteRawImage(pixels, texture.m_Width, texture.m_Height, output, ImageExportType.Png))
            throw new InvalidDataException("Failed to write the PS4 texture as PNG.");
        return output.ToArray();
    }

    public static void ValidateImport(TextureFile texture, byte[]? original)
    {
        var layout = GetLayout(texture);
        if (texture.m_MipCount != 1 || texture.m_MipMap || texture.m_StreamingMipmaps)
            throw new NotSupportedException("PS4 import currently supports textures with one mip only. Mip chains are not modified.");
        if (original is null || original.Length != layout.TiledSize)
            throw new InvalidDataException($"PS4 import requires exactly {layout.TiledSize} original bytes; unknown layouts are not modified.");
    }

    public static void ImportImage(TextureFile texture, byte[] original, Stream input)
    {
        ValidateImport(texture, original);
        var image = ImageResult.FromStream(input, ColorComponents.RedGreenBlueAlpha);
        if (image.Width != texture.m_Width || image.Height != texture.m_Height)
            throw new InvalidDataException($"Replacement must be {texture.m_Width} x {texture.m_Height}; resizing is not supported.");

        if (!TextureEncoderWrapper.NativeLibrariesSupported())
            throw new NotSupportedException("PS4 BC import requires UABEA's native texture encoder libraries.");
        // The bundled native buffer loader handles vertical orientation and
        // expects BGRA bytes. Do not also flip the image in managed code.
        TextureOperations.SwapRBComponentsInplace(image.Data);
        var mips = TextureEncoderWrapper.ConvertImage(image.Data, 1,
            (TextureFormat)texture.m_TextureFormat, image.Width, image.Height, 3);
        if (mips is null || mips.Length != 1)
            throw new InvalidDataException("The texture encoder did not return exactly one mip.");
        // Only mutate the destination after validation and encoding succeed.
        ReplaceEncodedTopLevel(texture, original, mips[0]);
    }

    public static void ReplaceEncodedTopLevel(TextureFile texture, byte[] original, byte[] linear)
    {
        ValidateImport(texture, original);
        int width = texture.m_Width, height = texture.m_Height;
        var tiled = GetLayout(texture).Swizzle(linear, original);
        texture.SetPictureData(tiled, width, height, (TextureFormat)texture.m_TextureFormat, 1);
        // SetPictureData rounds BC dimensions to 4; Unity's logical dimensions
        // must remain unchanged. The storage padding is represented by the bytes.
        texture.m_Width = width;
        texture.m_Height = height;
    }
}
