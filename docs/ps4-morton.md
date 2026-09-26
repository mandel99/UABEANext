# PS4 Morton 8x8 textures (experimental)

This fork adds two explicit TexturePlugin actions:

- **Export Texture2D PNG (PS4 Morton 8x8)**: export one texture or a selection
  to PNG, deswizzling compressed blocks before decoding the top mip.
- **Import Texture2D image (PS4 Morton 8x8)**: replace one texture from an
  image, or a selection using the existing batch filename matching dialog.
  Save the modified asset/bundle using UABEA's normal save workflow.

Use these actions only for textures known to use row-major 8x8 Morton
microtiles of compressed 4x4 blocks. They do not detect PS4 layouts and do
not implement AMD macrotiles, alternate pitches, arrays, volumes, or mip
tails. Standard export/import and the automatic preview remain unchanged;
use the exported PNG to inspect the deswizzled result. Do not use the normal
image import action for a tiled texture.

Supported block formats: DXT1 (BC1), DXT3 (BC2), DXT5 (BC3), BC4, BC5, BC6H,
and BC7. Crunch-compressed data and uncompressed pixel formats are rejected.
PNG export is an 8-bit image, so it does not preserve BC6H HDR precision.
Image import uses the bundled native compressor and is lossy according to
the original format. This is an image editing path, not lossless BC storage
roundtripping. Encoder availability may vary by platform and format.

## Why the padded tail matters

Each tile stores all 64 blocks, even at an incomplete logical edge. A
1024x684 texture has a logical grid of 256x171 BC blocks, but storage is
256x176 blocks (1024x704 pixels):

| Format | Logical bytes | Padded top-level bytes |
| --- | ---: | ---: |
| BC1 / BC4 | 350208 | 360448 |
| BC2 / BC3 / BC5 / BC6H / BC7 | 700416 | 720896 |

Decoding the tiled stream at the logical dimensions first drops bytes that
still contain visible blocks. Rearranging the resulting PNG cannot recover
them. The PS4 export instead reads the asset's complete texture data (inline
or external), detiles BC blocks, then decodes only the logical image. It
rejects a short top-level buffer rather than inserting transparent holes.
Mips following a complete top level are ignored during export.

For block coordinates x,y the source block index is
`((y / 8) * ceil(blocksWide / 8) + x / 8) * 64 + Morton(x % 8, y % 8)`.
Morton interleaves `x0,y0,x1,y1,x2,y2`. There is no compact edge traversal.

## Import limits

Import currently requires one non-streaming mip, one 2D image, the same
logical dimensions and format, and exactly the expected padded byte size.
Unknown layouts and mip chains are rejected before the asset is changed.
Existing padding blocks and platform metadata are retained. The replacement
is stored inline, as with the standard UABEA import, with a complete padded
image size. Original external resource bytes are not edited.

The importer performs compression in a temporary buffer and only commits
after all checks and swizzling succeed. The bundled native buffer loader
expects BGRA input and handles vertical orientation itself; the plugin
converts RGBA to BGRA explicitly. A native encoder regression test covers
both channel order and vertical orientation.

This implementation is verified with synthetic buffers and native encoder
roundtrips. It has not yet been validated against the original EO_015 asset
or by loading an edited asset in a PS4 game. That original raw asset was not
available; no game images are included in this repository.

## Build and test

```sh
git submodule update --init --recursive
dotnet build UABEANext4.sln -c Release
dotnet run --project Tests/Ps4TextureTests -c Release
```

Use a .NET SDK supported by upstream. The fork replaces one upstream
null-conditional assignment with an equivalent null check so the solution
also compiles with the installed .NET 9 SDK without preview language flags.
The AssetsTools.NET submodule remains at its upstream revision.

The console test project exits nonzero on failure. It covers known Morton
addresses, independently generated tiles in all seven formats, complete
and partial tiles, odd logical dimensions, padding preservation, the
1024x684 missing-tail regression, PNG pixel order, metadata preservation,
rejected imports, and native BC1/BC3/BC7 image import on Windows, including
odd dimensions. Other platforms
run the managed tests and explicitly skip the Windows native encoder test.
