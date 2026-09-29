using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;

namespace TexturePlugin.Helpers;

/// <summary>Conservative, read-only duplicate-block heuristic. Not a PS5 storage rule.</summary>
public static class Ps5ResourceCompatibility
{
    public static byte[]? FillPictureData(TextureFile texture, AssetsFileInstance file, AssetsManager manager)
    {
        if (texture.pictureData is { Length: > 0 }) return texture.pictureData;
        if (file.parentBundle == null && file.file.Metadata.TargetPlatform == 44
            && texture.m_IsPreProcessed && texture.m_TextureDimension == 2 && texture.m_ImageCount == 1
            && !string.IsNullOrEmpty(texture.m_StreamData.path) && texture.m_StreamData.size > 0)
        {
            string Resolve(string path) => Path.GetFullPath(Path.IsPathRooted(path)
                ? path : Path.Combine(Path.GetDirectoryName(file.path)!, path));
            string path = Resolve(texture.m_StreamData.path);
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                var ranges = new List<(long Offset, long Size)>();
                bool eligible = true;
                // Inspect every reference to this resource, not texture names or game IDs.
                lock (file.LockReader)
                {
                    foreach (var info in file.file.GetAssetsOfType(AssetClassID.Texture2D)
                        .Concat(file.file.GetAssetsOfType(AssetClassID.Cubemap)))
                    {
                        var t = TextureFile.ReadTextureFile(manager.GetBaseField(file, info));
                        if (t.m_StreamData.size == 0 || string.IsNullOrEmpty(t.m_StreamData.path)
                            || !string.Equals(Resolve(t.m_StreamData.path), path, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!t.m_IsPreProcessed || t.m_TextureDimension != 2 || t.m_ImageCount != 1
                            || t.m_StreamingMipmaps || t.m_StreamData.offset > long.MaxValue)
                        { eligible = false; break; }
                        try
                        {
                            // Unknown storage layouts do not provide enough evidence to shift reads.
                            Ps5MipChain.ForUnity(t.m_Width, t.m_Height, (TextureFormat)t.m_TextureFormat,
                                t.m_MipCount, checked((int)t.m_StreamData.size));
                        }
                        catch (Exception ex) when (ex is NotSupportedException || ex is InvalidDataException
                            || ex is ArgumentException || ex is OverflowException)
                        { eligible = false; break; }
                        ranges.Add(((long)t.m_StreamData.offset, t.m_StreamData.size));
                    }
                }
                if (eligible && TryDetectDuplicate(stream, ranges, out long boundary, out long extra)
                    && texture.m_StreamData.offset <= long.MaxValue
                    && ranges.Contains(((long)texture.m_StreamData.offset, texture.m_StreamData.size)))
                {
                    long offset = (long)texture.m_StreamData.offset;
                    stream.Position = checked(offset + (offset >= boundary ? extra : 0));
                    var data = new byte[texture.m_StreamData.size];
                    stream.ReadExactly(data);
                    texture.pictureData = data;
                    System.Diagnostics.Trace.WriteLine($"PS5 resource anomaly heuristic: duplicate {extra}-byte block at {boundary}: {path}. Origin requires investigation.");
                    return data;
                }
            }
        }
        return texture.FillPictureData(file);
    }

    /// <summary>
    /// Detect a single extra block repeated immediately across a texture boundary.
    /// Requires complete contiguous metadata coverage and exactly one candidate.
    /// A match is evidence, not proof: legitimate repeated data plus unrelated trailing
    /// bytes can be indistinguishable without authoritative resource metadata.
    /// </summary>
    public static bool TryDetectDuplicate(Stream stream, IEnumerable<(long Offset, long Size)> records,
        out long boundary, out long extra)
    {
        boundary = extra = 0;
        if (!stream.CanRead || !stream.CanSeek) return false;
        var ranges = records.Distinct().OrderBy(r => r.Offset).ToArray();
        if (ranges.Length < 2) return false;
        long end = 0;
        foreach (var r in ranges)
        {
            if (r.Offset != end || r.Size <= 0 || r.Size > long.MaxValue - end) return false;
            end += r.Size;
        }
        long excess = stream.Length - end;
        // Bound probing cost; standard PS5 allocations have at least 256-byte alignment.
        if (excess < 256 || excess > 16 * 1024 * 1024 || excess % 256 != 0) return false;
        long position = stream.Position;
        try
        {
            var left = new byte[(int)excess]; var right = new byte[(int)excess];
            long candidate = -1;
            for (int i = 1; i < ranges.Length; i++)
            {
                long p = ranges[i].Offset;
                if (ranges[i - 1].Size < excess) continue;
                stream.Position = p - excess; stream.ReadExactly(left); stream.ReadExactly(right);
                if (!left.AsSpan().SequenceEqual(right)) continue;
                // Uniform padding is especially weak evidence and must never trigger this.
                if (left.All(b => b == left[0])) continue;
                if (candidate >= 0) return false;
                candidate = p;
            }
            if (candidate < 0) return false;
            boundary = candidate; extra = excess; return true;
        }
        finally { stream.Position = position; }
    }
}
