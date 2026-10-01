using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using Avalonia.Platform.Storage;
using System.Text;
using TexturePlugin.Helpers;
using UABEANext4.AssetWorkspace;
using UABEANext4.Plugins;
using UABEANext4.ViewModels.Dialogs;

namespace TexturePlugin;

public class ImportBatchTextureOption : IUavPluginFormatOption
{
    public string Name => "Import Texture2D";

    public string Description => "Imports a folder of png/tga/bmp/jpgs into Texture2Ds";

    public UavPluginMode Options => UavPluginMode.Import;

    public bool SupportsSelection(Workspace workspace, UavPluginMode mode, IList<AssetInst> selection)
    {
        if (mode != UavPluginMode.Import)
        {
            return false;
        }

        var typeId = (int)AssetClassID.Texture2D;
        return selection.All(a => a.TypeId == typeId);
    }

    public IReadOnlyList<string> Extensions { get; } = ["png", "tga", "bmp", "jpg"];

    public Task<bool> Execute(Workspace workspace, IUavPluginFunctions funcs, UavPluginMode mode, IList<AssetInst> selection)
    {
        return Import(workspace, funcs, selection, null);
    }

    public Task<bool> ExecuteFormat(Workspace workspace, IUavPluginFunctions funcs,
        UavPluginMode mode, IList<AssetInst> selection, string extension)
    {
        return Import(workspace, funcs, selection, extension);
    }

    private async Task<bool> Import(Workspace workspace, IUavPluginFunctions funcs,
        IList<AssetInst> selection, string? extension)
    {
        if (selection.Count != 1)
            return await BatchImport(workspace, funcs, selection, extension);

        var extensions = GetExtensions(extension);
        var paths = await funcs.ShowOpenFileDialog(new FilePickerOpenOptions
        {
            Title = "Import texture image",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Texture image")
                { Patterns = extensions.Select(ext => "*." + ext).ToArray() }]
        });
        if (paths.Length == 0)
            return false;

        var asset = selection[0];
        var info = new ImportBatchInfo(asset, asset.FileName, asset.DisplayName, asset.PathId)
        {
            ImportFile = paths[0]
        };
        return await ImportTextures(workspace, funcs, [info]);
    }

    private static List<string> GetExtensions(string? extension) => extension switch
    {
        null => ["bmp", "png", "jpg", "jpeg", "tga"],
        "jpg" => ["jpg", "jpeg"],
        _ => [extension]
    };

    public async Task<bool> BatchImport(Workspace workspace, IUavPluginFunctions funcs,
        IList<AssetInst> selection, string? extension = null)
    {
        var dir = await funcs.ShowOpenFolderDialog(new FolderPickerOpenOptions()
        {
            Title = "Select import directory"
        });

        if (dir == null)
        {
            return false;
        }

        var extensions = GetExtensions(extension);
        var batchInfosViewModel = new BatchImportViewModel(workspace, selection.ToList(), dir, extensions);
        if (batchInfosViewModel.DataGridItems.Count == 0)
        {
            await funcs.ShowMessageDialog("Error", "No matching files found in the directory. Make sure the file names are in UABEA's format.");
            return false;
        }

        var batchInfosResult = await funcs.ShowDialog(batchInfosViewModel);
        if (batchInfosResult == null)
        {
            return false;
        }

        var success = await ImportTextures(workspace, funcs, batchInfosResult);
        return success;
    }

    private async Task<bool> ImportTextures(Workspace workspace, IUavPluginFunctions funcs, List<ImportBatchInfo> infos)
    {
        var errorBuilder = new StringBuilder();
        foreach (var info in infos)
        {
            var asset = info.Asset;
            var errorAssetName = $"{Path.GetFileName(asset.FileInstance.path)}/{asset.PathId}";

            var baseField = workspace.GetBaseField(asset);
            if (baseField == null)
            {
                errorBuilder.AppendLine($"[{errorAssetName}]: failed to read");
                continue;
            }

            var tex = TextureFile.ReadTextureFile(baseField);
            TextureHelper.SwizzleOptIn(tex, asset.FileInstance.file);

            if (info.ImportFile == null || !File.Exists(info.ImportFile))
            {
                errorBuilder.AppendLine($"[{errorAssetName}]: failed to import because {info.ImportFile ?? "[null]"} does not exist.");
                continue;
            }

            var singleMip = tex.m_MipCount == 1;
            var consolePreprocessed = tex.swizzleType is SwizzleType.PS4 or SwizzleType.PS5;
            var mipCount = consolePreprocessed ? tex.m_MipCount
                : singleMip ? 1 : int.MinValue;

            try
            {
                // Keep original console padding available to the swizzler.
                if (consolePreprocessed)
                    TextureHelper.FillPictureData(tex, asset.FileInstance, workspace.Manager);
                tex.EncodeTextureImage(info.ImportFile, mipCount: mipCount);
                tex.WriteTo(baseField);
                asset.UpdateAssetDataAndRow(workspace, baseField);
            }
            catch (Exception e)
            {
                errorBuilder.AppendLine($"[{errorAssetName}]: failed to import: {e}");
            }
        }

        if (errorBuilder.Length > 0)
        {
            string[] firstLines = errorBuilder.ToString().Split('\n').Take(20).ToArray();
            string firstLinesStr = string.Join('\n', firstLines);
            await funcs.ShowMessageDialog("Error", firstLinesStr);
        }

        return true;
    }
}
