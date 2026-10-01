using AssetsTools.NET.Extra;

namespace VideoPlugin;

public static class VideoResource
{
    /// <summary>Opens exactly the requested resource range, from disk or a bundle.</summary>
    public static Stream Open(AssetsFileInstance file, string source, ulong offset, ulong size)
    {
        if (offset > long.MaxValue || size > long.MaxValue || size == 0)
            throw new InvalidDataException("Invalid or empty streamed resource range.");
        var normalized = source.Replace('\\', '/');
        var name = normalized.Split('/').Last();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException("The streamed resource has no source file.");

        if (file.parentBundle is { } bundle)
        {
            var info = bundle.file.BlockAndDirInfo.DirectoryInfos.FirstOrDefault(i => i.Name == normalized)
                ?? bundle.file.BlockAndDirInfo.DirectoryInfos.FirstOrDefault(i => i.Name.Replace('\\', '/').Split('/').Last() == name);
            if (info != null)
            {
                if (info.IsReplacerPreviewable)
                    return new ResourceSlice(info.Replacer.GetPreviewStream(), (long)offset, (long)size, false);
                if ((long)offset > info.DecompressedSize || (long)size > info.DecompressedSize - (long)offset)
                    throw new InvalidDataException("Video range exceeds its bundle resource entry.");
                return new ResourceSlice(bundle.file.DataReader.BaseStream,
                    checked(info.Offset + (long)offset), (long)size, false);
            }
        }

        var directory = Path.GetDirectoryName(file.parentBundle?.path ?? file.path)!;
        var path = Path.Combine(directory, name);
        // Retain relative subdirectories, but do not follow archive URIs or paths outside the asset directory.
        if (!normalized.Contains(':') && !Path.IsPathRooted(normalized))
        {
            var relative = Path.GetFullPath(Path.Combine(directory, normalized));
            if (relative.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && File.Exists(relative))
                path = relative;
        }
        var stream = File.OpenRead(path);
        try { return new ResourceSlice(stream, (long)offset, (long)size, true); }
        catch { stream.Dispose(); throw; }
    }

    private sealed class ResourceSlice : Stream
    {
        private readonly Stream _source;
        private readonly long _offset;
        private readonly bool _ownsSource;
        public ResourceSlice(Stream source, long offset, long length, bool ownsSource)
        {
            if (offset < 0 || offset > source.Length || length < 0 || length > source.Length - offset)
                throw new InvalidDataException("Video resource range is outside the source file.");
            _source = source;
            _offset = offset;
            Length = length;
            _ownsSource = ownsSource;
        }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length { get; }
        public override long Position { get; set; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (Position < 0 || Position > Length) throw new IOException("Invalid resource position.");
            if (count == 0) return 0;
            lock (_source)
            {
                _source.Position = _offset + Position;
                var read = _source.Read(buffer, offset, (int)Math.Min(count, Length - Position));
                Position += read;
                if (read == 0 && Position < Length) throw new EndOfStreamException("Truncated video resource.");
                return read;
            }
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            var position = checked(offset + (origin switch
            {
                SeekOrigin.Begin => 0,
                SeekOrigin.Current => Position,
                SeekOrigin.End => Length,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            }));
            if (position < 0 || position > Length) throw new IOException("Invalid resource position.");
            return Position = position;
        }
        protected override void Dispose(bool disposing) { if (disposing && _ownsSource) _source.Dispose(); base.Dispose(disposing); }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
