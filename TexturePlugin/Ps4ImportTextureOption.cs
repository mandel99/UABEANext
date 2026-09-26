using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using Avalonia.Platform.Storage;
using TexturePlugin.Helpers;
using UABEANext4.AssetWorkspace;
using UABEANext4.Plugins;
using UABEANext4.ViewModels.Dialogs;

namespace TexturePlugin;

public class Ps4ImportTextureOption : IUavPluginOption
{
    public string Name => "Import Texture2D image (PS4 Morton 8x8)";
    public string Description => "Encode and swizzle a replacement image; same dimensions/format, one mip only.";
    public UavPluginMode Options => UavPluginMode.Import;

    public bool SupportsSelection(Workspace workspace, UavPluginMode mode, IList<AssetInst> selection)
        => mode == Options && selection.Count > 0 && selection.All(a => a.Type == AssetClassID.Texture2D);

    public async Task<bool> Execute(Workspace workspace, IUavPluginFunctions funcs, UavPluginMode mode, IList<AssetInst> selection)
    {
        if (!SupportsSelection(workspace, mode, selection))
            return false;

        var replacements = new List<(AssetInst Asset, string Path)>();
        if (selection.Count == 1)
        {
            var paths = await funcs.ShowOpenFileDialog(new FilePickerOpenOptions
            {
                Title = "Import PS4 Morton 8x8 texture (one mip)",
                AllowMultiple = false,
                FileTypeFilter = [new("Images") { Patterns = ["*.png", "*.tga", "*.bmp", "*.jpg", "*.jpeg"] }]
            });
            if (paths.Length == 0)
                return false;
            replacements.Add((selection[0], paths[0]));
        }
        else
        {
            var folder = await funcs.ShowOpenFolderDialog(new FolderPickerOpenOptions { Title = "Import PS4 Morton 8x8 textures" });
            if (folder is null)
                return false;
            var dialog = new BatchImportViewModel(workspace, selection.ToList(), folder, ["png", "tga", "bmp", "jpg", "jpeg"]);
            if (dialog.DataGridItems.Count == 0)
            {
                await funcs.ShowMessageDialog("PS4 import", "No matching images found. Use the names produced by texture export.");
                return false;
            }
            var infos = await funcs.ShowDialog(dialog);
            if (infos is null)
                return false;
            foreach (var info in infos)
                if (info.ImportFile is not null)
                    replacements.Add((info.Asset, info.ImportFile));
        }

        var errors = new List<string>();
        foreach (var (asset, path) in replacements)
        {
            try
            {
                var field = TextureHelper.GetByteArrayTexture(workspace, asset)
                    ?? throw new InvalidDataException("Failed to read Texture2D.");
                var texture = TextureFile.ReadTextureFile(field);
                var original = texture.FillPictureData(asset.FileInstance);
                Ps4TextureCodec.ValidateImport(texture, original);
                using var stream = File.OpenRead(path);
                Ps4TextureCodec.ImportImage(texture, original, stream);
                texture.WriteTo(field);
                asset.UpdateAssetDataAndRow(workspace, field);
            }
            catch (Exception ex)
            {
                errors.Add($"[{asset.AssetName}/{asset.PathId}]: {ex.Message}");
            }
        }
        if (errors.Count > 0)
            await funcs.ShowMessageDialog("PS4 import", string.Join('\n', errors.Take(20)));
        return replacements.Count > 0 && errors.Count == 0;
    }
}
