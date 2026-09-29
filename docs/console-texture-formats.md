# Additional console deswizzle formats

PS4 Morton and PS5 standard layouts now share an encoded-element description.
The existing managed texture decoder handles the resulting linear data. This
adds preview/export and lossless encoded rearrangement; it does not assert that
every listed format is used or sampled natively by a PS4/PS5 game.

| Additional formats | Pixels per element | Bytes per element |
| --- | --- | ---: |
| ARGB4444, RGB565, R16, RGBA4444, RG16, RHalf | 1 x 1 | 2 |
| RGHalf, RFloat, RGB9e5Float, RG32 | 1 x 1 | 4 |
| RGBAHalf, RGFloat, RGBA64 | 1 x 1 | 8 |
| RGBAFloat | 1 x 1 | 16 |
| ETC_RGB4, ETC2_RGB4, ETC2_RGBA1, EAC_R, EAC_R_SIGNED, ATC_RGB4 | 4 x 4 | 8 |
| ETC2_RGBA8, EAC_RG, EAC_RG_SIGNED, ATC_RGBA8 | 4 x 4 | 16 |
| ASTC_RGB / ASTC_RGBA, LDR | 4, 5, 6, 8, 10 or 12 square | 16 |

PS4 also accepts the legacy BGRA32Old identifier, already supported on PS5.
All previously supported Alpha8/R8, RGB24-expanded-to-RGBA32, 32-bit color and
BC formats remain supported. Format geometry follows the decoder contracts;
AMD's [element library](https://github.com/GPUOpen-Drivers/pal/blob/c5e800072a32f68b6ccc4422936d96167c6e0728/src/core/imported/addrlib/src/core/addrelemlib.cpp#L1260-L1495)
also describes integer/float, BC, ETC2 and ASTC element dimensions and sizes.

## Boundaries

- The original surface-layout restrictions still apply: PS4 Morton 8x8 and
  PS5 standard non-XOR 256 B / 4 KiB / 64 KiB. A decoder's format support does
  not identify a game's actual platform storage or tile mode.
- Newly added formats support encoded swizzle/deswizzle, including mip chains
  and preprocessing conversion. Image replacement remains restricted to the
  previously supported encoders and rejects unsupported formats before mutation.
- Preview/PNG output is 8-bit color; it does not preserve HDR or 16/32-bit channel
  precision. Raw rearrangement preserves all bits, including floating-point data.
- RGB48 and RGBFloat remain rejected: their 6/12-byte nominal pixels require
  verified platform storage rules. Do not assume the RGB24 expansion applies.
- YUY2 requires packed non-square element handling. PVRTC has its own layout
  and decoder requirements. Crunch is a compressed container, not a tiled block
  array. None is enabled by this change.
- ASTC HDR and other formats absent from the managed decoder remain unsupported.

## Validation

Local tests cover 37 format identifiers (36 newly enabled on both platforms,
plus BGRA32Old on PS4). Each is checked against independent Morton addressing
and published AMD swizzle-pattern tables for all three PS5 tile modes. Decoded
pixels match the normal linear decoder after cropping odd-size edges. Complete
odd-size mip chains retain raw data and padding, with no PS5 address overlaps.
Unsupported image imports leave their source data intact. Six unsupported
storage cases are explicitly rejected: 191 checks in total.

The existing 38 PS5 and 77 PS4/Switch checks also pass. These additional formats
were tested using synthetic fixtures, not newly supplied console game samples.
No game files were modified and local tests/game data are not distributed.
