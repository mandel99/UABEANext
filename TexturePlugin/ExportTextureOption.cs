using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using Avalonia.Platform.Storage;
using System.Text;
using TexturePlugin.Helpers;
using TexturePlugin.ViewModels;
using UABEANext4.AssetWorkspace;
using UABEANext4.Logic.Configuration;
using UABEANext4.Plugins;
using UABEANext4.Util;

namespace TexturePlugin;

public class ExportTextureOption : IUavPluginFormatOption
{
    public string Name => "Export Texture2D/Sprite";
    public string Description => "Exports Texture2D/Sprites to png/tga/bmp/jpg";
    public UavPluginMode Options => UavPluginMode.Export;

    public bool SupportsSelection(Workspace workspace, UavPluginMode mode, IList<AssetInst> selection)
    {
        if (mode != UavPluginMode.Export)
        {
            return false;
        }

        var texTypeId = (int)AssetClassID.Texture2D;
        var sprTypeId = (int)AssetClassID.Sprite;
        return selection.All(a => a.TypeId == texTypeId || a.TypeId == sprTypeId);
    }

    public async Task<bool> Execute(Workspace workspace, IUavPluginFunctions funcs, UavPluginMode mode, IList<AssetInst> selection)
    {
        if (selection.Count > 1)
        {
            return await BatchExport(workspace, funcs, selection);
        }
        else
        {
            return await SingleExport(workspace, funcs, selection);
        }
    }

    public IReadOnlyList<string> Extensions { get; } = ["png", "tga", "bmp", "jpg"];

    public Task<bool> ExecuteFormat(Workspace workspace, IUavPluginFunctions funcs,
        UavPluginMode mode, IList<AssetInst> selection, string extension)
    {
        return selection.Count > 1
            ? BatchExport(workspace, funcs, selection, extension)
            : SingleExport(workspace, funcs, selection, extension);
    }

    public async Task<bool> BatchExport(Workspace workspace, IUavPluginFunctions funcs, IList<AssetInst> selection, string? extension = null)
    {
        string fileExtension;
        ImageExportType exportType;
        if (extension != null)
        {
            fileExtension = "." + extension;
            exportType = ExportTypeFromFileName(fileExtension);
        }
        else
        {
            var optionsRes = await funcs.ShowDialog(new ExportBatchOptionsViewModel());
            // Yield between dialogs on Windows.
            await Task.Yield();
            if (optionsRes == null)
                return false;
            fileExtension = optionsRes.Value.Extension;
            exportType = optionsRes.Value.ImageType;
        }

        var dir = await funcs.ShowOpenFolderDialog(new FolderPickerOpenOptions()
        {
            Title = "Select export directory"
        });

        if (dir == null)
        {
            return false;
        }

        TextureLoader texLoader = new TextureLoader();
        StringBuilder errorBuilder = new StringBuilder();
        int emptyTextureCount = 0;

        bool exportJustNames = ConfigurationManager.Settings.ExportImportJustNames;
        foreach (AssetInst asset in selection)
        {
            var errorAssetName = $"{Path.GetFileName(asset.FileInstance.path)}/{asset.PathId}";
            if (asset.Type == AssetClassID.Texture2D)
            {
                // we don't need any processing, use assetstools.net.texture to export

                var texBaseField = TextureHelper.GetByteArrayTexture(workspace, asset);
                if (texBaseField == null)
                {
                    errorBuilder.AppendLine($"[{errorAssetName}]: failed to read");
                    continue;
                }

                var texFile = TextureFile.ReadTextureFile(texBaseField);
                TextureHelper.SwizzleOptIn(texFile, asset.FileInstance.file);

                // 0x0 texture, usually called like Font Texture or something
                if (texFile.m_Width == 0 && texFile.m_Height == 0)
                {
                    emptyTextureCount++;
                    continue;
                }

                string assetName = PathUtils.ReplaceInvalidPathChars(asset.AssetName ?? "Texture2D");
                string filePath = AssetNamer.GetAssetFileName(asset, assetName, fileExtension, exportJustNames);

                try
                {
                    WriteTextureImage(workspace, texFile, asset, Path.Combine(dir, filePath), exportType);
                }
                catch (Exception ex)
                {
                    errorBuilder.AppendLine($"[{errorAssetName}]: {ex.Message}");
                }
            }
            else if (asset.Type == AssetClassID.Sprite)
            {
                // need to do crop processing, use TextureLoader

                bool fullCrop = ConfigurationManager.Settings.FullCropSprites;
                byte[]? decTextureData = texLoader.GetSpriteRawBytes(workspace, asset, fullCrop, out var _, out var width, out var height);
                if (decTextureData == null)
                {
                    errorBuilder.AppendLine($"[{errorAssetName}]: failed to decode (missing resS, invalid texture format, invalid sprite, etc.)");
                    continue;
                }

                string assetName = PathUtils.ReplaceInvalidPathChars(asset.AssetName ?? "Sprite");
                string filePath = AssetNamer.GetAssetFileName(asset, assetName, fileExtension, exportJustNames);

                // SKBitmap is RGBA32 but StbIws expects BGRA32. swap R and B.
                TextureOperations.SwapRBComponentsInplace(decTextureData);

                // image is also upside down. flip it (normally assetstools.net.texture handles this)
                TextureOperations.FlipBGRA32VerticallyInplace(decTextureData, width, height);

                using FileStream outputStream = File.OpenWrite(Path.Combine(dir, filePath));
                if (!TextureOperations.WriteRawImage(decTextureData, width, height, outputStream, exportType))
                {
                    errorBuilder.AppendLine($"[{errorAssetName}]: failed to write image to disk");
                }
            }
        }

        if (emptyTextureCount == selection.Count)
        {
            await funcs.ShowMessageDialog("Error", "All textures are empty. No textures were exported.");
            return false;
        }

        if (errorBuilder.Length > 0)
        {
            string[] firstLines = errorBuilder.ToString().Split('\n').Take(20).ToArray();
            string firstLinesStr = string.Join('\n', firstLines);
            await funcs.ShowMessageDialog("Error", firstLinesStr);
        }

        return true;
    }

    public Task<bool> SingleExport(Workspace workspace, IUavPluginFunctions funcs, IList<AssetInst> selection, string? extension = null)
    {
        AssetInst asset = selection[0];
        if (asset.Type == AssetClassID.Texture2D)
            return SingleExportTexture2D(workspace, funcs, asset, extension);
        else if (asset.Type == AssetClassID.Sprite)
            return SingleExportTextureSprite(workspace, funcs, asset, extension);
        else
            return Task.FromResult(false);
    }

    private async Task<bool> SingleExportTexture2D(Workspace workspace, IUavPluginFunctions funcs, AssetInst asset, string? extension)
    {
        AssetTypeValueField? texBaseField = TextureHelper.GetByteArrayTexture(workspace, asset);
        if (texBaseField == null)
        {
            await funcs.ShowMessageDialog("Error", "Failed to read texture.");
            return false;
        }
        TextureFile texFile = TextureFile.ReadTextureFile(texBaseField);
        TextureHelper.SwizzleOptIn(texFile, asset.FileInstance.file);

        // 0x0 texture, usually called like Font Texture or something
        if (texFile.m_Width == 0 && texFile.m_Height == 0)
        {
            await funcs.ShowMessageDialog("Error", "Texture size is 0x0 which is not exportable.");
            return false;
        }

        string assetName = PathUtils.ReplaceInvalidPathChars(asset.AssetName ?? "Texture2D");
        var filePath = await ShowImageSaveFileDialog(funcs, asset, assetName, extension);
        if (filePath == null)
        {
            return false;
        }

        ImageExportType exportType = ExportTypeFromFileName(extension == null ? filePath : "." + extension);

        try
        {
            WriteTextureImage(workspace, texFile, asset, filePath, exportType);
        }
        catch (Exception ex)
        {
            string errorAssetName = $"{Path.GetFileName(asset.FileInstance.path)}/{asset.PathId}";
            await funcs.ShowMessageDialog("Error", $"[{errorAssetName}]: {ex.Message}");
            return false;
        }

        return true;
    }

    private async Task<bool> SingleExportTextureSprite(Workspace workspace, IUavPluginFunctions funcs, AssetInst asset, string? extension)
    {
        bool fullCrop = ConfigurationManager.Settings.FullCropSprites;

        string assetName = PathUtils.ReplaceInvalidPathChars(asset.AssetName ?? "Sprite");
        var filePath = await ShowImageSaveFileDialog(funcs, asset, assetName, extension);
        if (filePath == null)
        {
            return false;
        }

        ImageExportType exportType = ExportTypeFromFileName(extension == null ? filePath : "." + extension);
        string errorAssetName = $"{Path.GetFileName(asset.FileInstance.path)}/{asset.PathId}";

        TextureLoader texLoader = new TextureLoader();
        byte[]? decTextureData = texLoader.GetSpriteRawBytes(workspace, asset, fullCrop, out var _, out var width, out var height);
        if (decTextureData == null)
        {
            await funcs.ShowMessageDialog("Error", $"[{errorAssetName}]: failed to decode (missing resS, invalid texture format, invalid sprite, etc.)");
            return false;
        }

        // SKBitmap is RGBA32 but StbIws expects BGRA32. swap R and B.
        TextureOperations.SwapRBComponentsInplace(decTextureData);

        // image is also upside down. flip it (normally assetstools.net.texture handles this)
        TextureOperations.FlipBGRA32VerticallyInplace(decTextureData, width, height);

        using FileStream outputStream = File.OpenWrite(filePath);
        if (!TextureOperations.WriteRawImage(decTextureData, width, height, outputStream, exportType))
        {
            await funcs.ShowMessageDialog("Error", $"[{errorAssetName}]: failed to write image to disk");
            return false;
        }

        return true;
    }

    private static void WriteTextureImage(Workspace workspace, TextureFile texture, AssetInst asset, string path, ImageExportType type)
    {
        // Decode first so a failed export leaves the existing file alone.
        using var output = new MemoryStream();
        byte[]? data = TextureHelper.FillPictureData(texture, asset.FileInstance, workspace.Manager);
        if (!texture.DecodeTextureImage(data, output, type))
            throw new InvalidDataException("Failed to decode texture (missing resS or unsupported format).");
        File.WriteAllBytes(path, output.ToArray());
    }

    private static Task<string?> ShowImageSaveFileDialog(IUavPluginFunctions funcs, AssetInst asset, string assetName, string? extension)
    {
        bool exportJustNames = ConfigurationManager.Settings.ExportImportJustNames;
        return funcs.ShowSaveFileDialog(new FilePickerSaveOptions()
        {
            Title = "Save texture",
            FileTypeChoices = extension != null
                ? new FilePickerFileType[] { new(extension.ToUpperInvariant() + " image") { Patterns = ["*." + extension] } }
                :
            [
                new("PNG file") { Patterns = ["*.png"] },
                new("BMP file") { Patterns = ["*.bmp"] },
                new("JPG file") { Patterns = ["*.jpg", "*.jpeg"] },
                new("TGA file") { Patterns = ["*.tga"] },
            ],
            SuggestedFileName = AssetNamer.GetAssetFileName(asset, assetName, string.Empty, exportJustNames),
            DefaultExtension = extension ?? "png"
        });
    }

    private static ImageExportType ExportTypeFromFileName(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".bmp" => ImageExportType.Bmp,
            ".png" => ImageExportType.Png,
            ".jpg" or ".jpeg" => ImageExportType.Jpg,
            ".tga" => ImageExportType.Tga,
            _ => ImageExportType.Png
        };
    }
}
