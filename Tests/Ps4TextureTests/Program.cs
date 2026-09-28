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

Test("platform routing uses PS4 + preprocessing, without requiring a blob", () =>
{
    var tex = Texture(32,32);
    tex.m_PlatformBlob = [];
    Check(TexturePlatform.GetSwizzleType(tex,31)==SwizzleType.None,"false flag");
    tex.m_IsPreProcessed = true;
    Check(TexturePlatform.GetSwizzleType(tex,31)==SwizzleType.PS4,"PS4 true flag");
    Check(TexturePlatform.GetSwizzleType(tex,5)==SwizzleType.None,"non PS4");
    Check(TexturePlatform.GetSwizzleType(tex,38)==SwizzleType.None,"empty Switch blob");
    tex.m_PlatformBlob = new byte[96];
    Check(TexturePlatform.GetSwizzleType(tex,38)==SwizzleType.Switch,"Switch retained");
});

foreach(var format in new[]{TextureFormat.Alpha8,TextureFormat.R8,TextureFormat.RGBA32,TextureFormat.ARGB32,TextureFormat.BGRA32,TextureFormat.RGB24})
Test($"integrated {format} odd-size export/import is byte exact", () =>
{
    var tex=Texture(35,19,format);
    var layout=new Ps4MortonLayout(35,19,format);
    byte[] linear=new byte[layout.LinearSize];new Random(113).NextBytes(linear);
    byte[] original=layout.Swizzle(linear,new byte[layout.TiledSize]);
    tex.pictureData=original;
    tex.m_IsPreProcessed=true;
    tex.swizzleType=TexturePlatform.GetSwizzleType(tex,31);
    using var png=new MemoryStream();
    Check(tex.DecodeTextureImage(original,png,ImageExportType.Png),"export failed");
    png.Position=0;
    tex.EncodeTextureImage(png,1);
    Check(tex.pictureData.SequenceEqual(original),"integrated roundtrip bytes changed");
    Check(tex.m_Width==35 && tex.m_Height==19 && tex.m_IsPreProcessed,"metadata changed");
    tex.m_MipCount=2;
    png.Position=0;
    Reject<NotSupportedException>(()=>tex.EncodeTextureImage(png,1));
    Check(tex.pictureData.SequenceEqual(original),"failed import mutated data");
});

if (OperatingSystem.IsWindows())
Test("integrated BC1 path retains colors and orientation", () =>
{
    var tex=Texture(64,36);
    var layout=new Ps4MortonLayout(64,36,TextureFormat.DXT1);
    var linear=new byte[layout.LinearSize];
    for(int b=0;b<linear.Length/8;b++) BinaryPrimitives.WriteUInt16LittleEndian(linear.AsSpan(b*8),(ushort)(b%3==0?0xF800:0x001F));
    tex.pictureData=layout.Swizzle(linear,new byte[layout.TiledSize]);
    tex.m_IsPreProcessed=true;tex.swizzleType=TexturePlatform.GetSwizzleType(tex,31);
    using var png=new MemoryStream();tex.DecodeTextureImage(tex.pictureData,png,ImageExportType.Png);
    var expected=ImageResult.FromMemory(png.ToArray(),ColorComponents.RedGreenBlueAlpha);
    png.Position=0;tex.EncodeTextureImage(png,1);
    using var again=new MemoryStream();tex.DecodeTextureImage(tex.pictureData,again,ImageExportType.Png);
    var actual=ImageResult.FromMemory(again.ToArray(),ColorComponents.RedGreenBlueAlpha);
    Check(expected.Data.SequenceEqual(actual.Data),"BC1 integrated roundtrip");
});
foreach (uint platform in new uint[] { 31, 38 })
foreach (var format in new[] { TextureFormat.Alpha8, TextureFormat.R8, TextureFormat.RGBA32, TextureFormat.DXT1, TextureFormat.DXT5, TextureFormat.BC7 })
Test($"preprocessing toggle {platform} {format} preserves encoded elements", () =>
{
    var tex = Texture(35, 19, format);
    var layout = new Ps4MortonLayout(35, 19, format);
    byte[] original = new byte[layout.LinearSize];
    new Random(82).NextBytes(original);
    tex.pictureData = original;
    tex.m_PlatformBlob = [];
    TexturePlatform.SetPreprocessed(tex, platform, true);
    Check(tex.m_IsPreProcessed && tex.swizzleType != SwizzleType.None, "swizzle state");
    Check(tex.m_Width == 35 && tex.m_Height == 19, "dimensions");
    var tiled = tex.pictureData.ToArray();
    TexturePlatform.SetPreprocessed(tex, platform, false);
    Check(!tex.m_IsPreProcessed && tex.swizzleType == SwizzleType.None, "linear state");
    Check(tex.pictureData.SequenceEqual(original), "encoded bytes changed");
    Check(tex.m_CompleteImageSize == original.Length && tex.m_StreamData.path == "", "storage metadata");
    TexturePlatform.SetPreprocessed(tex, platform, true);
    Check(tex.pictureData.SequenceEqual(tiled), "repeat conversion differs");
});
Test("invalid preprocessing conversion leaves data and metadata intact", () =>
{
    var tex = Texture(35,19);
    tex.pictureData = new byte[3];
    var original = tex.pictureData;
    Reject<NotSupportedException>(() => TexturePlatform.SetPreprocessed(tex, 5, true));
    Reject<InvalidDataException>(() => TexturePlatform.SetPreprocessed(tex, 31, true));
    tex.m_MipCount = 2;
    Reject<InvalidDataException>(() => TexturePlatform.SetPreprocessed(tex, 31, true));
    Check(ReferenceEquals(tex.pictureData, original) && !tex.m_IsPreProcessed && tex.m_StreamData.path == "original.resS", "failed conversion mutated texture");
});
Test("PS4 RGB24 preprocessing expands storage and disabling writes honest RGBA32 metadata", () =>
{
 var tex=Texture(13,9,TextureFormat.RGB24);
 var source=new byte[13*9*3];new Random(7).NextBytes(source);tex.pictureData=source;
 TexturePlatform.SetPreprocessed(tex,31,true);
 Check(tex.m_TextureFormat==3 && tex.pictureData.Length==16*16*4,"preprocessed RGB24 storage");
 TexturePlatform.SetPreprocessed(tex,31,false);
 Check(tex.m_TextureFormat==(int)TextureFormat.RGBA32,"linear format must match four-byte storage");
 for(int i=0;i<13*9;i++){
  Check(tex.pictureData.AsSpan(i*4,3).SequenceEqual(source.AsSpan(i*3,3)),"RGB values");
  Check(tex.pictureData[i*4+3]==255,"expanded alpha");
 }
});
Test("PS4 mip chain offsets and independent per-level mapping", () =>
{
 var chain=new Ps4MipChain(35,19,TextureFormat.DXT1,6);
 Check(chain.Offsets.SequenceEqual(new[]{0,1024,1536,2048,2560,3072}),"mip offsets");
 Check(chain.TiledSize==3584 && chain.LinearSize==520,"chain sizes");
 var original=new byte[chain.TiledSize];new Random(19).NextBytes(original);
 var mips=chain.Deswizzle(original);
 Check(chain.Swizzle(mips,original).SequenceEqual(original),"full chain padding roundtrip");
 for(int i=0;i<6;i++){
  var reference=ReferenceTile(mips[i],Math.Max(1,35>>i),Math.Max(1,19>>i),8);
  // Independent helper has fixed padding; compare only real elements via layout.
  Check(chain.Levels[i].Deswizzle(reference).SequenceEqual(mips[i]),"independent level mapping");
 }
 Reject<InvalidDataException>(()=>chain.Deswizzle(original[..^1]));
 Reject<ArgumentOutOfRangeException>(()=>new Ps4MipChain(35,19,TextureFormat.DXT1,7));
 var tex=Texture(35,19);tex.m_MipCount=6;tex.m_MipMap=true;tex.m_IsPreProcessed=true;tex.pictureData=original;
 TexturePlatform.SetPreprocessed(tex,31,false);
 Check(tex.pictureData.Length==520 && tex.m_MipCount==6,"linear mip chain");
 TexturePlatform.SetPreprocessed(tex,31,true);
 var returned=chain.Deswizzle(tex.pictureData);
 Check(returned.Zip(mips,(a,b)=>a.SequenceEqual(b)).All(x=>x),"toggle preserves all visible mip blocks");
});
foreach(var format in new[]{TextureFormat.Alpha8,TextureFormat.RGBA32,TextureFormat.DXT1,TextureFormat.DXT5,TextureFormat.BC7})
if(OperatingSystem.IsWindows() || format==TextureFormat.Alpha8 || format==TextureFormat.RGBA32)
Test($"PS4 {format} image import rebuilds every mip and retains padding",()=>
{
 const int w=35,h=19,count=6;
 var chain=new Ps4MipChain(w,h,format,count);
 var original=new byte[chain.TiledSize];Array.Fill(original,(byte)0xA5);
 var tex=Texture(w,h,format);tex.m_MipCount=count;tex.m_MipMap=true;tex.pictureData=original;tex.swizzleType=SwizzleType.PS4;tex.m_IsPreProcessed=true;
 var rgba=new byte[w*h*4];
 for(int i=0;i<w*h;i++){rgba[i*4]=255;rgba[i*4+3]=255;}
 tex.EncodeTextureRaw(rgba,w,h,mipCount:count,useBgra:false);
 Check(tex.m_MipCount==count && tex.m_MipMap && tex.pictureData.Length==chain.TiledSize,"mip metadata");
 var encoded=chain.Deswizzle(tex.pictureData);
 Check(chain.Swizzle(encoded,original).SequenceEqual(tex.pictureData),"padding changed");
 for(int i=0;i<count;i++){
  int mw=Math.Max(1,w>>i),mh=Math.Max(1,h>>i);
  var level=tex.pictureData.AsSpan(chain.Offsets[i],chain.Levels[i].TiledSize).ToArray();
  var decoded=TextureFile.DecodeManagedData(level,format,mw,mh,false,new Ps4Swizzle(mw,mh,format));
  for(int j=0;j<decoded.Length;j+=4){
   Check(decoded[j+3]>=247,"mip alpha");
   if(format!=TextureFormat.Alpha8)Check(decoded[j]>=247 && decoded[j+1]<=8 && decoded[j+2]<=8,"mip channel order");
  }
 }
});
foreach(var format in new[]{TextureFormat.RGBA32,TextureFormat.DXT1,TextureFormat.DXT5,TextureFormat.BC7})
if(OperatingSystem.IsWindows() || format==TextureFormat.RGBA32)
Test($"PS4 {format} mip regeneration preserves vertical orientation",()=>
{
 const int size=32,count=6;
 var chain=new Ps4MipChain(size,size,format,count);
 var tex=Texture(size,size,format);tex.m_MipCount=count;tex.m_MipMap=true;tex.pictureData=new byte[chain.TiledSize];tex.swizzleType=SwizzleType.PS4;
 var rgba=new byte[size*size*4];
 for(int y=0;y<size;y++)for(int x=0;x<size;x++){
  int i=(y*size+x)*4;rgba[i+(y<size/2?0:2)]=255;rgba[i+3]=255;
 }
 tex.EncodeTextureRaw(rgba,size,size,mipCount:count,useBgra:false);
 for(int mip=0;mip<count-1;mip++){
  int side=size>>mip;
  var data=tex.pictureData.AsSpan(chain.Offsets[mip],chain.Levels[mip].TiledSize).ToArray();
  var dec=TextureFile.DecodeManagedData(data,format,side,side,false,new Ps4Swizzle(side,side,format));
  int top=(side-1)*side*4;
  Check(dec[2]>dec[0]+100 && dec[top]>dec[top+2]+100,"red top / blue bottom mip "+mip+" bottom="+string.Join(",",dec.Take(4))+" top="+string.Join(",",dec.Skip(top).Take(4)));
 }
});
Console.WriteLine($"{passed} tests passed.");
