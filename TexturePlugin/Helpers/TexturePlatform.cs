using UABEANext4.Logic.AssetInfo;
using AssetsTools.NET.Texture;

namespace TexturePlugin.Helpers;

public static class TexturePlatform
{
    // Convert encoded elements directly: toggling must not recompress BC data.
    public static void SetPreprocessed(TextureFile texture, uint platform, bool enabled)
    {
        if (texture.m_IsPreProcessed == enabled) return;
        if (platform != (uint)BuildTarget.PS4 && platform != (uint)BuildTarget.Switch)
            throw new NotSupportedException("Preprocessing conversion is supported only for PS4 and Switch.");
        if (texture.m_ImageCount != 1 || texture.m_TextureDimension != 2
            || texture.m_MipCount != 1 || texture.m_MipMap || texture.m_StreamingMipmaps)
            throw new NotSupportedException("Preprocessing conversion requires one non-streaming 2D mip.");
        var format = (TextureFormat)texture.m_TextureFormat;
        bool isSwitch = platform == (uint)BuildTarget.Switch;
        if (isSwitch && enabled && texture.m_PlatformBlob.Length != 0)
            throw new NotSupportedException("Switch texture already has platform metadata despite a false preprocessing flag; its storage is ambiguous.");
        if (isSwitch && !enabled) format = SwitchSwizzle.GetCorrectedSwitchTextureFormat(format);
        if (!isSwitch && !enabled) format = Ps4MortonLayout.GetStorageFormat(format);
        var layout = new Ps4MortonLayout(texture.m_Width, texture.m_Height, format);
        byte[] source = texture.pictureData ?? throw new InvalidDataException("Load the complete texture data before conversion.");
        if (!isSwitch && enabled && format == TextureFormat.RGB24)
        {
            int pixels = checked(texture.m_Width * texture.m_Height);
            if (source.Length != checked(pixels * 3))
                throw new InvalidDataException("Linear RGB24 must contain three bytes per pixel.");
            var rgba = new byte[checked(pixels * 4)];
            for (int i = 0; i < pixels; i++)
            {
                Buffer.BlockCopy(source, i * 3, rgba, i * 4, 3);
                rgba[i * 4 + 3] = 255;
            }
            source = rgba;
        }
        byte[] result;
        byte[] blob = texture.m_PlatformBlob;
        if (!isSwitch)
        {
            int expected = enabled ? layout.LinearSize : layout.TiledSize;
            if (source.Length != expected) throw new InvalidDataException("Texture size does not match the supported PS4 layout.");
            result = enabled ? layout.Swizzle(source, new byte[layout.TiledSize]) : layout.Deswizzle(source);
        }
        else
        {
            var block = SwitchSwizzle.GetTextureFormatBlockSize(format);
            if (block.IsEmpty) throw new NotSupportedException("Unsupported Switch format.");
            if (!enabled && (blob == null || blob.Length < 12))
                throw new InvalidDataException("Switch platform metadata is missing.");
            int gobHeight = enabled ? SwitchSwizzle.GetBlockHeightByBlockSize(block, texture.m_Height)
                : SwitchSwizzle.GetBlockHeightByPlatformBlob(blob);
            var padded = SwitchSwizzle.GetPaddedTextureSize(texture.m_Width, texture.m_Height, block.Width, block.Height, gobHeight);
            int stride = checked(padded.Width / block.Width * 16);
            int storedSize = checked(stride * (padded.Height / block.Height));
            int logicalStride = checked(layout.BlocksWide * layout.BytesPerBlock);
            if (source.Length != (enabled ? layout.LinearSize : storedSize))
                throw new InvalidDataException("Texture size does not match the supported Switch layout.");
            if (enabled)
            {
                var paddedData = new byte[storedSize];
                for (int y = 0; y < layout.BlocksHigh; y++)
                    Buffer.BlockCopy(source, y * logicalStride, paddedData, y * stride, logicalStride);
                result = SwitchSwizzle.Swizzle(paddedData, padded, block, gobHeight);
                var swizzler = new SwitchSwizzle(texture.m_Width, texture.m_Height, ref format, out _, out _);
                blob = swizzler.MakePlatformBlob(new[] { 0 }, (uint)result.Length);
            }
            else
            {
                var paddedData = SwitchSwizzle.Unswizzle(source, padded, block, gobHeight);
                result = new byte[layout.LinearSize];
                for (int y = 0; y < layout.BlocksHigh; y++)
                    Buffer.BlockCopy(paddedData, y * stride, result, y * logicalStride, logicalStride);
                // An empty blob also disables the existing Switch detection path.
                blob = Array.Empty<byte>();
            }
        }
        // Commit only after every validation and conversion has succeeded.
        texture.pictureData = result;
        texture.m_CompleteImageSize = result.Length;
        texture.m_StreamData.path = "";
        texture.m_StreamData.offset = 0;
        texture.m_StreamData.size = 0;
        texture.m_PlatformBlob = blob;
        texture.m_TextureFormat = (int)format;
        texture.m_IsPreProcessed = enabled;
        texture.swizzleType = GetSwizzleType(texture, platform);
    }

    public static SwizzleType GetSwizzleType(TextureFile texture, uint targetPlatform)
    {
        if (targetPlatform == (uint)BuildTarget.PS4 && texture.m_IsPreProcessed)
            return SwizzleType.PS4;
        if (targetPlatform == (uint)BuildTarget.Switch && texture.m_PlatformBlob.Length != 0)
            return SwizzleType.Switch;
        return SwizzleType.None;
    }
}
