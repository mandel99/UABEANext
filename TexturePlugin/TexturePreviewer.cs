using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using AssetsTools.NET.Texture.TextureDecoders.CrnUnity;
using Avalonia.Media.Imaging;
using TexturePlugin.Helpers;
using UABEANext4.AssetWorkspace;
using UABEANext4.Logic.Mesh;
using UABEANext4.Plugins;

namespace TexturePlugin;
public class TexturePreviewer : IUavPluginPreviewer, IUavRawTexturePreviewer
{
    public string Name => "Preview Texture2D";
    public string Description => "Preview Texture2Ds";

    public UavPluginPreviewerType SupportsPreview(Workspace workspace, AssetInst selection)
    {
        var previewType = selection.Type == AssetClassID.Texture2D
            ? UavPluginPreviewerType.Image
            : UavPluginPreviewerType.None;

        return previewType;
    }

    public (Bitmap?, int) ExecuteImage(Workspace workspace, IUavPluginFunctions funcs, AssetInst selection, out string? error)
        => ExecuteImage(workspace, funcs, selection, false, out error);

    public bool SupportsRawPreview(Workspace workspace, AssetInst asset)
    {
        var field = TextureHelper.GetByteArrayTexture(workspace, asset);
        if (field == null) return false;
        var texture = TextureFile.ReadTextureFile(field);
        return TexturePlatform.GetSwizzleType(texture, asset.FileInstance.file.Metadata.TargetPlatform) != SwizzleType.None;
    }

    public (Bitmap?, int) ExecuteImage(Workspace workspace, IUavPluginFunctions funcs, AssetInst selection,
        bool showSwizzled, out string? error)
    {
        try
        {
            var image = TextureLoader.GetTexture2DBitmap(workspace, selection, out TextureFormat format, showSwizzled);
            if (image != null)
            {
                error = null;
                return (image, (int)format);
            }
            else
            {
                error = $"Texture failed to decode. The image format may not be supported or the texture is not valid. ({format})";
                return (null, (int)format);
            }
        }
        catch (Exception ex)
        {
            error = $"Texture failed to decode due to an error. Exception:\n{ex}";
            return (null, -1);
        }
    }

    public MeshObj? ExecuteMesh(Workspace workspace, IUavPluginFunctions funcs, AssetInst selection, out string? error)
        => throw new InvalidOperationException();

    public string? ExecuteText(Workspace workspace, IUavPluginFunctions funcs, AssetInst selection, out string? error)
        => throw new InvalidOperationException();

    public void Cleanup() { }
}
