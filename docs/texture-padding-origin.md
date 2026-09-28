# Where the original texture padding comes from

Investigation date: 2026-09-28. This finding explains why recreating a tiled
texture from its logical image can preserve the picture while changing bytes
outside it. It does not change the texture address equations.

## PS4: padding traced to another texture

We searched 795 BC1/BC3 textures in the available game assets. The BC1 texture
EO_015 is 1024 x 684, with storage for 1024 x 704. Its 10240 padding bytes contain
1280 compressed blocks, of which 1148 are distinct.

Every one of those 10240 bytes matches EO_141, a 684 x 1024 photograph of
apricots, at the **same physical buffer offsets**. Of those offsets, 9840 bytes
belong to EO_141's visible image and 400 bytes to its own padding. EO_015 shows
peas; none of its 1280 padding blocks occurs in its own logical image.

The same 10240 bytes also occur at those offsets in the padding of EO_083,
EO_120, EO_074 and EO_084.

We reproduced the complete original EO_015 by using EO_141's raw buffer as the
destination and overwriting only EO_015's valid blocks through the normal
swizzler. All **360448 bytes** matched, including padding.

This is strong evidence of a reused working buffer whose unused regions were
not cleared during asset preparation. The data does not establish the exact
Unity/Sony function, allocator or processing order. In particular, EO_141 need
not have been the immediately preceding operation: its contents could have
survived several subsequent textures.

## What the public AMD code establishes

These are AMD PAL/AddrLib implementations for related GPU architectures, not
the source of Sony's console drivers or Unity's console preprocessing tools.

- Legacy GCN surface calculations compute alignment and return padded pitch,
  height and allocation size. `PadDimensions` changes dimensions; it does not
  fill image memory with pixel values.
  [AMD legacy surface calculation](https://github.com/GPUOpen-Drivers/pal/blob/bf91cb29233ffceff95eb9ad16c6dc00b2029541/src/core/imported/addrlib/src/r800/egbaddrlib.cpp#L351-L446)
- GFX10 `ComputeSurfaceInfoMacroTiled` aligns dimensions to tile dimensions.
  `HwlCopyMemToSurface` passes the caller's copy extent to the CPU swizzler.
  [AMD GFX10 layout and copy](https://github.com/GPUOpen-Drivers/pal/blob/c5e800072a32f68b6ccc4422936d96167c6e0728/src/core/imported/addrlib/src/gfx10/gfx10addrlib.cpp#L3802-L4168)
- `Copy2DSliceUnaligned` copies elements only within `origin + extent`; it does
  not clear the surrounding padding. A caller copying only the logical image
  into a reused destination therefore leaves its previous padding intact.
  [AMD CPU swizzler](https://github.com/GPUOpen-Drivers/pal/blob/c5e800072a32f68b6ccc4422936d96167c6e0728/src/core/imported/addrlib/src/core/addrswizzler.cpp#L296-L378)

The observed asset contents are consistent with this mechanism. They are
already present in the stored asset, so UABEANext and the current GPU driver
do not generate them when exporting the image.

## PS5: the exact origin remains unconfirmed

The supplied 640 x 360 BC1 sample contains 7680 padding bytes, including 5105
nonzero bytes. Its padding has 960 compressed blocks and 70 distinct block
values. Only 31 blocks also occur in the logical image. None matches the block
in the same column of the last logical row.

A reused buffer is compatible with the public GFX10 copy implementation, but
we have only one PS5 sample and have not identified another source image.
The PS4 finding must not be presented as proof of the PS5 sample's origin.

## Consequences for import and roundtrip

Original padding cannot generally be reconstructed from PNG pixels. It is
historical data outside the logical image, not an extra swizzle rule.

Normal image replacement retains the source buffer's padding. A raw
deswizzle/swizzle roundtrip with that buffer can be byte exact. PNG replacement
may recompress BC blocks and therefore need not preserve logical encoded bytes.

Changing IsPreProcessed from true to false discards padding. Changing it back
creates new padding initialized to zero; it does not recover the historical
bytes or necessarily the original PS5 tile mode. A byte-exact roundtrip through
that conversion would require separately preserving both original padding and
layout information.

The game assets, images and local test harnesses are not distributed with this
documentation or the preview build.
