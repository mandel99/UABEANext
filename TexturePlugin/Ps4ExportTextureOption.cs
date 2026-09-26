using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using Avalonia.Platform.Storage;
using TexturePlugin.Helpers;
using UABEANext4.AssetWorkspace;
using UABEANext4.Logic.Configuration;
using UABEANext4.Plugins;
using UABEANext4.Util;

namespace TexturePlugin;

public class Ps4ExportTextureOption : IUavPluginOption
{
    public string Name => "Export Texture2D PNG (PS4 Morton 8x8)";
    public string Description => "Deswizzle BC blocks before decoding; export the full-resolution mip to PNG.";
    public UavPluginMode Options => UavPluginMode.Export;

    public bool SupportsSelection(Workspace workspace, UavPluginMode mode, IList<AssetInst> selection)
        => mode == Options && selection.Count > 0 && selection.All(a => a.Type == AssetClassID.Texture2D);

    public async Task<bool> Execute(Workspace workspace, IUavPluginFunctions funcs, UavPluginMode mode, IList<AssetInst> selection)
    {
        if (!SupportsSelection(workspace, mode, selection))
            return false;

        bool single = selection.Count == 1;
        string? destination = single
            ? await funcs.ShowSaveFileDialog(new FilePickerSaveOptions
            {
                Title = "Export PS4 Morton 8x8 texture",
                FileTypeChoices = [new("PNG image") { Patterns = ["*.png"] }],
                DefaultExtension = "png",
                SuggestedFileName = FileName(selection[0])
            })
            : await funcs.ShowOpenFolderDialog(new FolderPickerOpenOptions { Title = "Export PS4 Morton 8x8 textures" });
        if (destination is null)
            return false;

        var errors = new List<string>();
        foreach (var asset in selection)
        {
            try
            {
                var field = TextureHelper.GetByteArrayTexture(workspace, asset)
                    ?? throw new InvalidDataException("Failed to read Texture2D.");
                var texture = TextureFile.ReadTextureFile(field);
                byte[] png = Ps4TextureCodec.ExportPng(texture, texture.FillPictureData(asset.FileInstance));
                // Decode completely before opening an existing destination file.
                await File.WriteAllBytesAsync(single ? destination : Path.Combine(destination, FileName(asset)), png);
            }
            catch (Exception ex)
            {
                errors.Add($"[{asset.AssetName}/{asset.PathId}]: {ex.Message}");
            }
        }
        if (errors.Count > 0)
            await funcs.ShowMessageDialog("PS4 export", string.Join('\n', errors.Take(20)));
        return errors.Count == 0;
    }

    private static string FileName(AssetInst asset) => AssetNamer.GetAssetFileName(asset,
        PathUtils.ReplaceInvalidPathChars(asset.AssetName ?? "Texture2D"), ".png",
        ConfigurationManager.Settings.ExportImportJustNames);
}
