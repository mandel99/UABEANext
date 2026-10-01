using AssetsTools.NET.Extra;
using Avalonia.Platform.Storage;
using UABEANext4.AssetWorkspace;
using UABEANext4.Logic.Configuration;
using UABEANext4.Plugins;
using UABEANext4.Util;

namespace VideoPlugin;

public class ExportVideoOption : IUavPluginOption
{
    public string Name => "Video (original format)";
    public string Description => "Exports the original video bytes without conversion.";
    public UavPluginMode Options => UavPluginMode.Export;

    public bool SupportsSelection(Workspace workspace, UavPluginMode mode, IList<AssetInst> selection) =>
        mode == Options && selection.Count > 0 && selection.All(a => a.Type is AssetClassID.VideoClip or AssetClassID.MovieTexture);

    public async Task<bool> Execute(Workspace workspace, IUavPluginFunctions funcs, UavPluginMode mode, IList<AssetInst> selection)
    {
        string? directory = null;
        if (selection.Count > 1)
        {
            directory = await funcs.ShowOpenFolderDialog(new FolderPickerOpenOptions { Title = "Export videos" });
            if (directory == null) return false;
        }
        var errors = new List<string>();
        foreach (var asset in selection)
        {
            try
            {
                var field = VideoAsset.Read(workspace, asset);
                var extension = VideoAsset.Extension(workspace, asset, field);
                var name = AssetNamer.GetAssetFileName(workspace, asset, "." + extension,
                    ConfigurationManager.Settings.ExportNameLength, ConfigurationManager.Settings.ExportImportJustNames);
                var destination = directory != null ? Path.Combine(directory, name)
                    : await funcs.ShowSaveFileDialog(new FilePickerSaveOptions
                    {
                        Title = "Export video (original format)", SuggestedFileName = name, DefaultExtension = extension,
                        FileTypeChoices = [new FilePickerFileType(extension.ToUpperInvariant() + " video") { Patterns = ["*." + extension] }]
                    });
                if (destination != null)
                    await Task.Run(() => VideoAsset.Export(workspace, asset, destination));
            }
            catch (Exception ex) { errors.Add($"{asset.DisplayName} ({asset.PathId}): {ex.Message}"); }
        }
        if (errors.Count > 0) await funcs.ShowMessageDialog("Video export", string.Join('\n', errors.Take(20)));
        return false; // Export does not modify the selected assets.
    }
}
