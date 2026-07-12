# Changelog

## 1.0.2 - 2026-07-12

- Use one native combo-box implementation for framerate, transition, and audio-track selectors while keeping only the framerate field editable.
- Match option-row button heights to the system-calculated combo-box height.
- Add zero-tolerance UI smoke checks for framerate, transition, and label geometry.
- Add a multi-resolution application icon for the window and executable.

## 1.0.1 - 2026-07-12

- Keep the audio track selector, Analyze Media button, and Tools button aligned on one responsive row.
- Replace the wrapping options flow with a stable two-row table layout.
- Vertically center the framerate, transition selector, and Parse Script button.
- Rename the UI label from "CLT style" to the more accurate "Transition".

## 1.0.0 - 2026-07-12

- Parse VapourSynth and AviSynth Trim ranges without executing scripts.
- Generate MeGUI-compatible CLT files with explicit framerate handling.
- Cut AAC, AC3, DTS, MP2, MP3, PCM, and WAV through the MeGUI BeSplit workflow.
- Read audio tracks from TS, M2TS, MKV, MP4, and other ffmpeg-compatible media.
- Prefer eac3to for direct container extraction with ffmpeg fallback.
- Adaptively stream-copy aligned unsupported codecs or precisely cut and re-encode to the original codec.
- Detect ffmpeg beside the app, through MeGUI, through PATH, or by manual selection.
- Provide self-contained and .NET 8 framework-dependent Windows builds.
