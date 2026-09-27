using UABEANext4.Logic.AssetInfo;
using AssetsTools.NET.Texture;

namespace TexturePlugin.Helpers;

public static class TexturePlatform
{
    public static SwizzleType GetSwizzleType(TextureFile texture, uint targetPlatform)
    {
        if (targetPlatform == (uint)BuildTarget.PS4 && texture.m_IsPreProcessed)
            return SwizzleType.PS4;
        if (targetPlatform == (uint)BuildTarget.Switch && texture.m_PlatformBlob.Length != 0)
            return SwizzleType.Switch;
        return SwizzleType.None;
    }
}
