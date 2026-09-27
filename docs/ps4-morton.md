# PS4 Morton 8x8 textures (experimental)

PS4 textures now use the existing preview, Export Texture2D/Sprite, image
import and Edit workflows. There are no separate PS4 menu entries.
AssetsTools.NET implements the ISwizzler interface used by Switch textures.

Automatic selection requires both serialized-file target platform PS4 (31)
and Texture2D `m_IsPreProcessed = true`. A missing or false flag retains the
standard linear path. PS4 does not require a nonempty `m_PlatformBlob`.
Switch selection still requires its original platform blob.

The Texture2D preview shows **Show original swizzled texture** above the
image when console swizzling is detected. Checking it bypasses deswizzling
for the preview only, showing stored block/pixel order at the logical image
dimensions. It does not change texture bytes, preprocessing flags or exports.
Selecting another asset resets the checkbox to the normal decoded view.

The block size comes from `m_TextureFormat`, not from PNG dimensions:

| Formats | Element size | Bytes per element |
| --- | --- | ---: |
| Alpha8, R8 | 1x1 pixel | 1 |
| RGBA32, ARGB32, BGRA32 | 1x1 pixel | 4 |
| DXT1 / BC4 | 4x4 pixels | 8 |
| DXT3 / DXT5 / BC5 / BC6H / BC7 | 4x4 pixels | 16 |

The flag is a routing condition, not proof of every possible PS4 layout.
This implementation supports row-major 8x8 Morton microtiles only. AMD
macrotiles, alternate pitches, arrays and volumes are not implemented.
Unsupported formats and truncated buffers fail explicitly.

## Padding and orientation

Each tile stores all 64 elements, including incomplete logical edges.
For example, a 1024x684 BC texture uses a 256x171 logical block grid but
256x176 stored blocks (1024x704 pixels). BC1 requires 360448 stored bytes;
BC3 requires 720896. Decoding at logical dimensions before detiling discards
bytes that still contain visible blocks. A PNG made that way cannot restore
them without the original texture data.

The complete inline or external data is read before detiling. The source
index is `((y / 8) * ceil(blocksWide / 8) + x / 8) * 64 + Morton(x % 8, y % 8)`.
Morton interleaves `x0,y0,x1,y1,x2,y2`. Decoding and logical cropping follow.
The usual Unity image orientation is applied by the existing image path.

## Import and mip limits

Edit Texture2D includes **Is preprocessed**. Changing true to false detiles
the encoded data and saves a normal linear texture; changing false to true
tiles it for the serialized file's PS4 or Switch platform. The conversion
does not decode/recompress BC blocks. Switch conversion creates or clears
its platform blob as appropriate. Batch selections support mixed values and
the reset button restores the original selection value.

Conversion currently supports the pixel/BC formats in the table above that
the selected console swizzler supports. It requires one non-streaming 2D
mip and a serialized preprocessing field. Apply image replacement, format
and mip changes separately. Unsupported platforms, ambiguous Switch metadata
and mismatched data sizes are rejected without updating the asset. Edge
padding is discarded when becoming linear and zero-filled when tiling again;
visible encoded elements are preserved, but unused padding need not match.
Legacy Switch detection by nonempty platform blob remains unchanged.

Export/preview decode the top level; trailing mip data is not exported.
Import requires one non-streaming 2D image, one mip, unchanged dimensions
and format, and exactly the expected padded size. Mip-chain import remains
unsupported. This does not claim that PS4 mipmaps are solved.

Import retains padding bytes, preprocessing and platform metadata. The
replacement is stored inline through the standard save workflow; original
external resource bytes are not overwritten. Encoding and swizzling finish
before picture data is replaced. Pixel formats use managed encoding;
BC formats require the native compressor and can be lossy. BC6H PNG export
does not preserve HDR precision.

## Validation

A user-supplied Unity 2020.3.48f1 PS4 bundle contained a 2048x2048 Alpha8
SDF atlas, one mip, `m_IsPreProcessed=true`, an empty platform blob and
4194304 external texture bytes. The reconstructed glyph atlas is readable.
Export to PNG, import, bundle save and reload preserved all 4194304 texture
bytes exactly. This validates that sample; in-game loading and other PS4
hardware layouts have not been verified. Private game assets are not included.

The console tests cover independent Morton fixtures for seven BC formats,
partial tiles, padding, the 1024x684 truncated-tail regression, platform
routing and five byte-exact pixel-format roundtrips at odd dimensions.
Windows tests also exercise native BC encoding, channel order and orientation.
The previous standalone codec is retained only as a test reference.

## Build and test

```sh
git submodule update --init --recursive
dotnet build UABEANext4.sln -c Release
dotnet run --project Tests/Ps4TextureTests -c Release
```

The AssetsTools.NET submodule points to this fork's PS4 implementation.
No separate plugin installation or menu selection is needed.
