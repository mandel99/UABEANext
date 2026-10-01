using AssetsTools.NET;
using AssetsTools.NET.Extra;
using UABEANext4.AssetWorkspace;

namespace VideoPlugin;

public static class VideoAsset
{
    public static AssetTypeValueField Read(Workspace workspace, AssetInst asset)
    {
        if (asset.Type == AssetClassID.MovieTexture)
        {
            // Avoid allocating one field object per byte for large legacy embedded videos.
            var template = workspace.GetTemplateField(asset);
            var movie = template.Children.FirstOrDefault(f => f.Name == "m_MovieData");
            if (movie is { IsArray: true })
                movie.ValueType = AssetValueType.ByteArray;
            else if (movie?.Children.FirstOrDefault() is { IsArray: true } array)
                array.ValueType = AssetValueType.ByteArray;
            lock (asset.FileInstance.LockReader)
                return template.MakeValue(asset.FileReader, asset.AbsoluteByteStart);
        }
        return workspace.GetBaseField(asset) ?? throw new InvalidDataException("Failed to deserialize the video asset.");
    }

    public static Stream Open(Workspace workspace, AssetInst asset, AssetTypeValueField field)
    {
        if (asset.Type == AssetClassID.MovieTexture)
        {
            var data = field["m_MovieData"];
            if (data.IsDummy) throw new InvalidDataException("MovieTexture has no embedded movie data.");
            if (!data.TemplateField.IsArray && data.TemplateField.ValueType != AssetValueType.ByteArray)
                data = data["Array"];
            if (data.IsDummy) throw new InvalidDataException("MovieTexture has no embedded movie data array.");
            var bytes = data.TemplateField.ValueType == AssetValueType.ByteArray
                ? data.AsByteArray : data.Children.Select(c => c.AsByte).ToArray();
            return new MemoryStream(bytes, false);
        }
        var resource = field["m_ExternalResources"];
        if (resource.IsDummy) throw new NotSupportedException("This VideoClip has no streamed resource descriptor.");
        return VideoResource.Open(asset.FileInstance, resource["m_Source"].AsString,
            resource["m_Offset"].AsULong, resource["m_Size"].AsULong);
    }

    public static string Extension(Workspace workspace, AssetInst asset, AssetTypeValueField field)
    {
        using var input = Open(workspace, asset, field);
        return VideoContainer.Detect(input, asset.Type == AssetClassID.MovieTexture ? "movie.ogv" : field["m_OriginalPath"].AsString);
    }

    public static void Export(Workspace workspace, AssetInst asset, string destination)
    {
        using var input = Open(workspace, asset, Read(workspace, asset));
        if (input.Length == 0) throw new InvalidDataException("The video is empty.");
        // Avoid truncating an existing export if the source is missing or ends prematurely.
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
                input.CopyTo(output);
            File.Move(temp, destination, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

}
