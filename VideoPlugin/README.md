# Video export

Select a `VideoClip` (or legacy `MovieTexture`), then **Export → Video (original format)**.
Multiple selected clips export to a folder. No external tools or video codecs are needed.

The exporter copies the original bytes, without conversion. Modern clips use
`m_ExternalResources` (source, offset, size); legacy movies contain `m_MovieData`.
Resources can be adjacent files or entries in the loaded asset bundle.

File signatures identify MP4/M4V/3GP, QuickTime MOV, WebM/Matroska, Ogg,
AVI, ASF/WMV, MPEG, MPEG transport streams, DV and IVF/VP8 containers.
The original extension is a fallback; unidentified data exports as `.bin`.
This exports the stored format, regardless of whether the computer can play its codec.
Files referenced only by a VideoPlayer URL or placed in StreamingAssets are not embedded VideoClip assets.

Import and transcoding are not included.

References:
- [AssetRipper VideoClip export](https://github.com/AssetRipper/AssetRipper/blob/master/Source/AssetRipper.Export.UnityProjects/Miscellaneous/VideoClipExporter.cs)
- [AssetRipper streamed resource handling](https://github.com/AssetRipper/AssetRipper/blob/master/Source/AssetRipper.SourceGenerated.Extensions/StreamedResourceExtensions.cs)
- [Unity video file compatibility](https://docs.unity3d.com/2021.3/Documentation/Manual/VideoSources-FileCompatibility.html)
