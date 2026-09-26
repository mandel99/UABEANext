using System.Buffers.Binary;
using AssetsTools.NET.Texture;
using StbImageSharp;
using TexturePlugin.Helpers;

int passed = 0;
void Test(string name, Action test)
{
    test();
    Console.WriteLine($"PASS {name}");
    passed++;
}
void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
}
void Reject<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
TextureFile Texture(int width, int height, TextureFormat format = TextureFormat.DXT1) => new()
{
    m_Width = width, m_Height = height, m_TextureFormat = (int)format,
    m_ImageCount = 1, m_TextureDimension = 2, m_MipCount = 1,
    m_PlatformBlob = [1, 2, 3, 4],
    m_StreamData = new() { path = "original.resS", offset = 128, size = 4096 }
};

// Independent reference: enumerate complete tiles, then decode each Morton
// index, including invalid edge coordinates. Never compact the edge tiles.
byte[] ReferenceTile(byte[] linear, int width, int height, int stride)
{
    int bw = (width + 3) / 4, bh = (height + 3) / 4;
    byte[] result = new byte[((bw + 7) / 8) * ((bh + 7) / 8) * 64 * stride];
    Array.Fill(result, (byte)0xA5);
    int offset = 0;
    for (int ty = 0; ty < bh; ty += 8)
    for (int tx = 0; tx < bw; tx += 8)
    for (int z = 0; z < 64; z++, offset += stride)
    {
        int x = tx, y = ty;
        for (int bit = 0; bit < 3; bit++)
        {
            x += ((z >> (2 * bit)) & 1) << bit;
            y += ((z >> (2 * bit + 1)) & 1) << bit;
        }
        if (x < bw && y < bh)
            linear.AsSpan((y * bw + x) * stride, stride).CopyTo(result.AsSpan(offset, stride));
    }
    return result;
}

Test("known Morton address order", () =>
{
    var layout = new Ps4MortonLayout(32, 32, TextureFormat.DXT1);
    byte[] tiled = new byte[512];
    for (int i = 0; i < 64; i++) Array.Fill(tiled, (byte)i, i * 8, 8);
    byte[] result = layout.Deswizzle(tiled);
    int[] row0 = [0, 1, 4, 5, 16, 17, 20, 21];
    int[] row7 = [42, 43, 46, 47, 58, 59, 62, 63];
    for (int x = 0; x < 8; x++)
    {
        Check(result[x * 8] == row0[x], "first row");
        Check(result[(56 + x) * 8] == row7[x], "last row");
    }
});

foreach (var format in new[] { TextureFormat.DXT1, TextureFormat.DXT3, TextureFormat.DXT5,
    TextureFormat.BC4, TextureFormat.BC5, TextureFormat.BC6H, TextureFormat.BC7 })
foreach (var (width, height) in new[] { (1024, 684), (2048, 1024), (36, 52), (1, 1), (35, 19) })
Test($"{format} {width}x{height}: reference mapping, inverse, padding", () =>
{
    var layout = new Ps4MortonLayout(width, height, format);
    byte[] linear = new byte[layout.LinearSize];
    new Random(1701).NextBytes(linear);
    byte[] expected = ReferenceTile(linear, width, height, layout.BytesPerBlock);
    byte[] original = new byte[layout.TiledSize];
    Array.Fill(original, (byte)0xA5);
    Check(layout.Swizzle(linear, original).SequenceEqual(expected), "swizzle vs independent reference");
    Check(layout.Deswizzle(expected).SequenceEqual(linear), "deswizzle vs reference");
    Check(layout.Swizzle(layout.Deswizzle(expected), expected).SequenceEqual(expected), "padding must survive");
    Check(original.All(b => b == 0xA5), "must not mutate input");
});

Test("1024x684 missing-tail regression and PNG orientation", () =>
{
    var texture = Texture(1024, 684);
    var layout = new Ps4MortonLayout(1024, 684, TextureFormat.DXT1);
    Check(layout.LinearSize == 350208 && layout.TiledSize == 360448, "BC1 storage sizes");
    byte[] linear = new byte[layout.LinearSize];
    // Every BC1 block is opaque solid red/blue, with asymmetric rows/columns.
    for (int y = 0; y < layout.BlocksHigh; y++)
    for (int x = 0; x < layout.BlocksWide; x++)
        BinaryPrimitives.WriteUInt16LittleEndian(linear.AsSpan((y * layout.BlocksWide + x) * 8),
            (ushort)((y % 3 == 0 && x % 5 != 0) ? 0xF800 : 0x001F));
    var tiled = ReferenceTile(linear, 1024, 684, 8);
    var png = Ps4TextureCodec.ExportPng(texture, tiled);
    var image = ImageResult.FromMemory(png, ColorComponents.RedGreenBlueAlpha);
    var expected = TextureFile.DecodeManagedData(linear, TextureFormat.DXT1, 1024, 684, false);
    TextureOperations.FlipBGRA32VerticallyInplace(expected, 1024, 684);
    Check(image.Width == 1024 && image.Height == 684, "logical PNG dimensions");
    Check(image.Data.SequenceEqual(expected), "all pixels, including top edge and orientation");
    // The old export would retain only this prefix. Never silently make holes.
    Reject<InvalidDataException>(() => Ps4TextureCodec.ExportPng(texture, tiled[..layout.LinearSize]));
    var extraMips = tiled.Concat(new byte[2048]).ToArray();
    texture.m_MipCount = 2;
    Check(Ps4TextureCodec.ExportPng(texture, extraMips).SequenceEqual(png), "top-level export ignores later mips");
});

Test("import commit preserves metadata, padding and logical odd dimensions", () =>
{
    var texture = Texture(35, 19, TextureFormat.BC7);
    var layout = new Ps4MortonLayout(35, 19, TextureFormat.BC7);
    byte[] original = new byte[layout.TiledSize];
    Array.Fill(original, (byte)0xA5);
    byte[] linear = new byte[layout.LinearSize];
    new Random(123).NextBytes(linear);
    Ps4TextureCodec.ReplaceEncodedTopLevel(texture, original, linear);
    Check(texture.pictureData.SequenceEqual(ReferenceTile(linear, 35, 19, 16)), "padded output");
    Check(texture.m_Width == 35 && texture.m_Height == 19, "logical dimensions retained");
    Check(texture.m_TextureFormat == (int)TextureFormat.BC7 && texture.m_MipCount == 1, "format/mips retained");
    Check(texture.m_PlatformBlob.SequenceEqual(new byte[] { 1, 2, 3, 4 }), "platform metadata retained");
    Check(texture.m_CompleteImageSize == layout.TiledSize, "full stored byte count");
    Check(texture.m_StreamData.path == "" && texture.m_StreamData.size == 0 && texture.m_StreamData.offset == 0, "inline replacement");
});

Test("unsupported and incomplete imports do not mutate texture", () =>
{
    var texture = Texture(32, 32);
    byte[] original = new byte[512], linear = new byte[512];
    texture.pictureData = original;
    texture.m_MipCount = 2;
    Reject<NotSupportedException>(() => Ps4TextureCodec.ReplaceEncodedTopLevel(texture, original, linear));
    texture.m_MipCount = 1;
    Reject<InvalidDataException>(() => Ps4TextureCodec.ReplaceEncodedTopLevel(texture, original[..511], linear));
    Reject<InvalidDataException>(() => Ps4TextureCodec.ReplaceEncodedTopLevel(texture, new byte[1024], linear));
    Reject<InvalidDataException>(() => Ps4TextureCodec.ReplaceEncodedTopLevel(texture, original, linear[..511]));
    texture.m_TextureDimension = 3;
    Reject<NotSupportedException>(() => Ps4TextureCodec.ValidateImport(texture, original));
    Check(ReferenceEquals(texture.pictureData, original) && texture.m_StreamData.path == "original.resS", "failed import mutated texture");
    Reject<NotSupportedException>(() => new Ps4MortonLayout(32, 32, TextureFormat.DXT1Crunched));
    Reject<ArgumentOutOfRangeException>(() => new Ps4MortonLayout(0, 32, TextureFormat.DXT1));
    Reject<OverflowException>(() => new Ps4MortonLayout(int.MaxValue, 32, TextureFormat.DXT1));
});

if (OperatingSystem.IsWindows())
foreach (var format in new[] { TextureFormat.DXT1, TextureFormat.DXT5, TextureFormat.BC7 })
foreach (var (width, height) in new[] { (64, 36), (35, 19) })
Test($"PNG -> native {format} encoder -> swizzle -> PNG ({width}x{height})", () =>
{
    var texture = Texture(width, height, format);
    var layout = new Ps4MortonLayout(width, height, TextureFormat.DXT1);
    byte[] linear = new byte[layout.LinearSize];
    for (int b = 0; b < linear.Length / 8; b++)
        BinaryPrimitives.WriteUInt16LittleEndian(linear.AsSpan(b * 8), (ushort)(b % 3 == 0 ? 0xF800 : 0x001F));
    byte[] png = Ps4TextureCodec.ExportPng(Texture(width, height), ReferenceTile(linear, width, height, 8));
    byte[] original = new byte[new Ps4MortonLayout(width, height, format).TiledSize];
    Array.Fill(original, (byte)0xA5);
    using var input = new MemoryStream(png);
    Ps4TextureCodec.ImportImage(texture, original, input);
    var result = ImageResult.FromMemory(Ps4TextureCodec.ExportPng(texture, texture.pictureData), ColorComponents.RedGreenBlueAlpha);
    var expected = ImageResult.FromMemory(png, ColorComponents.RedGreenBlueAlpha);
    var differences = result.Data.Zip(expected.Data, (a, b) => Math.Abs(a - b)).ToArray();
    Check(result.Width == width && result.Height == height, "native roundtrip dimensions");
    Check(differences.Max() <= 8, $"native roundtrip changed pixels (max channel delta {differences.Max()})");
    var wrongSize = Texture(32, 32);
    using var wrongInput = new MemoryStream(png);
    Reject<InvalidDataException>(() => Ps4TextureCodec.ImportImage(wrongSize, new byte[512], wrongInput));
});
else
    Console.WriteLine("SKIP native image import test: Windows encoder binaries required.");

Console.WriteLine($"{passed} tests passed.");
