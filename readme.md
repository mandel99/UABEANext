## UABEANext

This fork adds experimental **PS4 Morton 8x8 and PS5 GFX10 standard texture support**
to the existing preview, export, import and edit workflows.
See [PS4 layouts and usage](docs/ps4-morton.md),
[PS5 support and limitations](docs/ps5-gfx10.md), and
[our investigation of original texture padding](docs/texture-padding-origin.md).

Download the [Windows x64 PS5 test build](https://github.com/mandel99/UABEANext/releases/tag/ps5-preview-20260929).
This preview requires the .NET 8 x64 runtime. PS5 support currently covers
RGB24 storage and standard mip chains; see the limitations and audit findings
before testing other assets.

UABEA with dock support. When this repo becomes stable and reaches feature parity, the code will be merged into the original UABEA repo.

Upstream nightly builds (do not include this fork's console texture changes):

- https://nightly.link/nesrak1/UABEANext/workflows/build-windows/master/uabea-windows.zip
- https://nightly.link/nesrak1/UABEANext/workflows/build-ubuntu/master/uabea-ubuntu.zip

Report any issues on the original repo: https://github.com/nesrak1/UABEA
