# Changelog

## 1.0.0 - 2026-07-12

- Parse VapourSynth and AviSynth Trim ranges without executing scripts.
- Generate MeGUI-compatible CLT files with explicit framerate handling.
- Cut AAC, AC3, DTS, MP2, MP3, PCM, and WAV through the MeGUI BeSplit workflow.
- Read audio tracks from TS, M2TS, MKV, MP4, and other ffmpeg-compatible media.
- Prefer eac3to for direct container extraction with ffmpeg fallback.
- Adaptively stream-copy aligned unsupported codecs or precisely cut and re-encode to the original codec.
- Detect ffmpeg beside the app, through MeGUI, through PATH, or by manual selection.
- Provide self-contained and .NET 8 framework-dependent Windows builds.
