# Unity PS5 standard texture layouts

The existing preview, export, batch import and Edit Texture2D paths route PS5
assets with `m_IsPreProcessed = true` through the GFX10 standard-layout
swizzler. A false flag uses ordinary linear decoding. The original-swizzled
preview corrects RGB24 storage to RGBA32 on its temporary texture object only.

## Supported scope

- Non-streaming 2D textures with one image and complete mip chains.
- Standard non-XOR 256B_S (1), 4KB_S (5) and 64KB_S (9) layouts.
- Alpha8, R8, RGB24, RGBA32, ARGB32, BGRA32/BGRA32Old, DXT1/3/5 and
  BC4/5/6H/7 encoded elements. Image recompression also requires an encoder
  supporting the requested format.
- Additional integer/half/float pixels and ETC/EAC, ATC and ASTC LDR blocks
  support deswizzle/export and encoded conversion. See the
  [extended format table and limitations](console-texture-formats.md);
  their game-specific storage has not been validated with real console samples.
- Unity PS5 RGB24 preprocessing uses four-byte RGBA storage in the examined
  assets. Image import retains serialized RGB24 while writing this storage
  representation. Disabling preprocessing writes honest RGBA32 metadata;
  enabling it on linear RGB24 expands RGB to RGBA with opaque alpha.
- Large mip levels occupy allocations in reverse order, with a shared tile at
  the start for the smallest levels. Logical mip dimensions are floor-shifted;
  AMD physical allocation is ceil-shifted in elements. This distinction matters
  for non-power-of-two textures such as 264 x 126.
- Image replacement retains original dimensions, format and mip count, and
  preserves bytes outside every logical level, including unused mip-tail space.
  Lower mips are regenerated from the edited image; original authored mip
  values are retained by raw mip APIs, not by image re-encoding.

## Choosing a layout

Payload length alone does not identify an AMD tile mode. The first preview
rejected all equal-size candidates; real assets show that this rejects many
valid Unity textures, including power-of-two icons.

The Unity PS5 profile now selects 4KB_S when its complete mip-chain size matches
the stored payload. This profile was checked against all 413 preprocessed
textures in a Unity 2020.3.7f1 PS5 asset set and the earlier PS5 sample. It is an
observed platform convention, not universal mode detection. The low-level
`Ps5GfxLayout.InferTileMode` still rejects ambiguous sizes.
If the 4 KiB chain does not fit, only a unique 256 B / 64 KiB size match is
accepted. Other modes can share a size, so unknown games still require visual
verification or explicit platform metadata.

Display/XOR layouts, arrays, cubemaps, 3D textures and streaming mip chains are
not implemented. None was required by the examined preprocessed Texture2D set.
The implementation does not invent undocumented platform-blob fields.

Changing IsPreProcessed converts all encoded levels without BC recompression.
Turning it off discards padding; turning it on creates a 4 KiB standard chain
with zero-filled padding. It does not recover historical padding or necessarily
an earlier non-4-KiB mode. See [the padding investigation](texture-padding-origin.md).

## Evidence and limits

Mip placement follows
[AMD PAL Gfx10Lib::ComputeSurfaceInfoMacroTiled](https://github.com/GPUOpen-Drivers/pal/blob/c5e800072a32f68b6ccc4422936d96167c6e0728/src/core/imported/addrlib/src/gfx10/gfx10addrlib.cpp#L3802-L4025).
Single-level element addresses were independently checked against
[AMD's GFX10 swizzle tables](https://github.com/GPUOpen-Drivers/pal/blob/c5e800072a32f68b6ccc4422936d96167c6e0728/src/core/imported/addrlib/src/gfx10/gfx10SwizzlePattern.h).
These are public AMD references, not Sony's console SDK.

Audit of the supplied PPSA03596 asset set, including three bundles:

- 499 texture records: 484 with data, 15 empty placeholders.
- 413 preprocessed textures, all matching the complete 4 KiB profile, with
  504 mip levels. Every raw chain roundtripped exactly, with no overlapping
  logical addresses. All 484 nonempty textures exported without exceptions.
- All 10 preprocessed RGB24 records passed structural decoding and raw roundtrip.
- Contact sheets were inspected for all nonempty records, plus mip previews.
  A resource-data anomaly involving a duplicated block and inconsistent texture
  offsets was observed. Its origin and general handling require deeper investigation.
- 17 real image imports covered RGB24, RGBA32, BC3, BC7 and mip chains. Padding,
  metadata and preprocessing conversions were checked; three written asset
  files reloaded with identical imported data. Original files were not modified.
- 38 local PS5 checks and all 77 PS4/Switch regression checks passed. Tests cover
  AMD address tables, known mip-offset vectors, odd-size physical allocation,
  lossless color-channel/orientation roundtrips and native BC mip regeneration.

The earlier 640 x 360 BC1 sample also retains its exact raw roundtrip and
bundle import/save/reload checks. Game assets, exported images and local test
harnesses are not distributed. No in-game runtime validation has been performed.
