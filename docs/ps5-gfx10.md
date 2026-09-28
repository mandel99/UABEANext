# PS5 standard texture layouts

The existing preview, export, batch import and Edit Texture2D paths now route
PS5 assets with `m_IsPreProcessed = true` through a GFX10 standard-layout
swizzler. The original-swizzled preview option still displays the source
representation. A false preprocessing flag uses the ordinary texture path.

This integrates the supplied PS5 overlay with additional validation and the
existing controlled console image encoder. It is a limited implementation,
not a general decoder for every PS5 GPU surface.

## Supported scope

- Non-streaming Texture2D, one image and exactly one mip.
- Standard non-XOR 256B_S (1), 4KB_S (5) and 64KB_S (9) layouts.
- Alpha8, R8, RGBA32, ARGB32, BGRA32/BGRA32Old, DXT1/3/5 and BC4/5/6H/7
  encoded elements. Image recompression also requires a format supported by
  the installed native encoder; Alpha8 and the supported byte color formats
  use managed encoding.
- Image replacement retains the original dimensions, format and mip count.
  Bytes outside the logical image retain the original padding.

The mode is inferred from exact payload length **within these three standard
families**. A missing, mismatched or ambiguous size fails explicitly. Size
alone cannot establish that an unknown PS5 texture uses a standard layout:
display/XOR modes may share sizes and are not detected by this implementation.
The preprocessing flag identifies platform processing, not an AMD tile mode.
No undocumented platform-blob interpretation is invented here.

Changing IsPreProcessed to false converts encoded elements without BC
recompression. Changing it to true creates a 4 KiB standard layout only if
that layout is unambiguous on reload. The toggle preserves logical encoded
elements, but discards old padding and initializes new padding to zero.
An existing texture with a different original mode is not guaranteed to regain
that mode after toggling off and on. Normal image replacement retains it.

PS5 mip tails, mip chains, arrays, cubemaps, display/XOR layouts and RGB24
storage are not implemented. Existing PS4 mip-chain handling is unchanged.

## Review and validation

Address equations were independently compared at every logical element of
529 x 277 test images, for all three modes and element sizes 1, 4, 8 and 16,
against [AMD PAL's GFX10 swizzle pattern tables](https://github.com/GPUOpen-Drivers/pal/blob/dev/src/core/imported/addrlib/src/gfx10/gfx10SwizzlePattern.h).
These public GPU tables validate the address equations, not all Unity PS5
export conventions.

The supplied 640 x 360 BC1 sample has target platform 44, one mip,
IsPreProcessed true, an empty platform blob and 122880 stored bytes.
Its logical 160 x 90 BC blocks occupy a 160 x 96 padded surface in 4KB_S.
Raw deswizzle/swizzle preserved all 122880 bytes including padding.
PNG replacement preserved padding; the resulting bundle was saved and
reopened with identical imported bytes and texture metadata. PNG replacement
recompresses BC1 and is not expected to preserve the original compressed
bytes. The sample's average decoded channel difference was 0.009 / 255.

Local validation: 31 PS5 checks and 77 existing PS4/Switch checks passed.
PS5 checks include independent AMD addresses, padding, managed format
orientation/channels, native BC imports with partial blocks, overflow,
rejected metadata, failed conversions without mutation and bundle roundtrip.
Release solution build succeeded. Tests and supplied game data are kept
outside the published repository.
