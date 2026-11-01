# Naqsh — Feature Specification

Every feature lists acceptance criteria (Accept). A feature is done only when its
acceptance criteria and matching checks in docs/QA.md pass.

## 0. Formats and timing
- Project formats: 720p, 1080p, 1080i (output only), 2160p at 25, 29.97, 30, 50, 59.94, 60.
  Default for new projects: 1080p50 (changeable on first run by region).
- Interlaced output (1080i50 / 1080i59.94) for SDI and recording by field interleaving.
- Input frame-rate conversion: nearest-frame by default, optional blending; 29.97/30 and
  59.94/60 mismatches handled with a fractional accumulator (no periodic stutter).
- Time-of-day timecode on Program and all recordings; optional LTC input.
- Accept: a 25p camera and a 59.94 file in a 50p project run 30 minutes with no drift
  and no audible clicks.

## 1. Language and localization
- UI languages: Persian (fa-IR, RTL) and English (en-US, LTR). Chosen on the first-run
  screen and changeable anytime in Settings and from a language button in the title bar.
- Switching language happens at runtime without restarting and without interrupting
  Program output: FlowDirection flips (RTL ↔ LTR), all strings, menus, tooltips, dialogs,
  shortcut labels and the web apps update.
- The video monitor row (Preview | transitions | Program) keeps LTR physical order in both
  languages, as broadcast operators expect.
- Independent settings (default follows language): digits (Persian ۰–۹ / Latin 0–9),
  calendar (Jalali / Gregorian), time format (24 h default).
- Fonts: Vazirmatn for Persian and English UI (it includes Latin); Lalezar for Persian
  brand titles. Both bundled (OFL).
- Graphics templates, rundown items and captions have their own language, independent of
  the UI language (a Persian operator can air English graphics and vice versa).
- Web apps (controller, audio mixer, shading, tally) have their own language selector.
- Logs, file formats and API names stay English.
- Accept: switching language while streaming and recording causes 0 dropped frames; a test
  verifies every key exists in both languages; no hard-coded UI strings (analyzer/test).

## 2. Inputs
- Cameras, camcorders, cinema cameras, webcams, capture cards in SD/HD/4K with resolution,
  frame-rate and pixel-format selection; show detected signal format.
- Capture hardware: Blackmagic (DeckLink SDK), AJA (plugin), Magewell, Elgato, UVC over
  PCIe, Thunderbolt, USB; signals HDMI, SDI (HD/3G/12G), Component, S-Video, Composite as
  provided by the card. Hot-plug: unplug shows "No Signal" slate; replug resumes < 3 s.
- IP cameras: RTSP/RTMP/SRT/HLS/MJPEG with exponential-backoff reconnect and latency.
- Video files: MP4, MOV/QuickTime (ProRes 4444 alpha), MKV, AVI, MXF, WMV, MPEG-1/2, DV,
  HDV, FLV. Play/pause/seek/loop, mark in/out, auto-play on Program, auto-pause/rewind when
  leaving Program, end-of-clip warning (gold at 10 s, red at 5 s).
- Images (PNG alpha, JPG, BMP, TIFF, GIF), image sequences, PSD.
- PowerPoint via COM in an isolated STA worker with a hidden window (never steals focus);
  fallback to exported images; next/previous controls.
- Audio files (MP3, WAV, AAC, FLAC); audio devices (USB, ASIO, WASAPI, embedded).
- Playlist; desktop/window capture; CefSharp browser input with transparency and audio;
  NDI receive; solid color; color bars + 1 kHz tone.
- Relative media paths; a relink dialog for missing files; a project never fails to load
  because of a missing file.
- Per input: name, crop, position/zoom/rotation, opacity, deinterlace, sharpen, audio delay,
  color-space tag, "Safe to air" flag.

## 3. Keying
- Chroma key with one-click Auto Green / Auto Blue, picker, tolerance, softness, spill
  suppression, edge choke, 4-point garbage matte; colour key; luma key; alpha passthrough;
  DeckLink key/fill. Accept: a well-lit green screen keys cleanly after one click.

## 4. Color correction and monitoring
- Primary wheels (lift/gamma/gain/offset), exposure, contrast + pivot, temperature/tint,
  saturation, hue, white-balance picker.
- Curves: master, R/G/B, hue-vs-sat, hue-vs-hue, luma-vs-sat.
- Secondary: HSL qualifier with soft matte, shape mask with feather, inside/outside grades.
- .cube LUTs 17/33/65 (input log→Rec.709 and creative output with mix); HDR (HLG/PQ) → SDR.
- Camera matching from a 24-patch chart; grade presets, copy/paste, grade groups,
  before/after split; broadcast legalizer with illegal-pixel overlay.
- Scopes: waveform (Y, RGB), parade, vectorscope with skin line, histogram, false color,
  zebra, focus peaking, safe areas.
- Web shading panel (CCU-style) incl. VISCA paint for PTZ cameras.
- Accept: a 33-point LUT on 8 HD inputs costs < 1 ms GPU per frame (GTX 1660 class).

## 5. Virtual sets
- Background + optional foreground + 1–4 presenter slots; 4 animated angles per set;
  3 original built-in sets with Iranian architectural motifs; user sets as folder + JSON.

## 6. Switcher and director safety
- Preview (left) / Program (right); thumbnail grid with tally borders and labels.
- Transitions: Cut, Cross Fade, Wipe, Slide, Zoom, 3D Zoom, Fly, Cube, Merge, Stinger.
  4 transition buttons with durations, T-bar, Fade To Black.
- A transition requested during another completes the running one first (queued); T-bar
  and auto transitions share one state (no jumps).
- On-Air lock (toggle, shortcut, automatic when streaming/recording) disables
  double-click-to-Program, Quick Play and input deletion. Stopping stream/record needs
  hold-to-confirm (1 s). Undo for the last 20 non-live edits.
- Preview overlays: graphics can be checked on Preview and taken with the cut.
- Accept: a 1-second Fade has no dropped frames with 4 HD inputs (GTX 1660 class).

## 7. Layers and multiview
- Up to 10 layers per input; layer designer with snap guides and presets.

## 8. Overlay channels
- 8 channels with own transition, position, border; never routed to the clean feed.

## 9. Audio mixer
- Strip per input: fader, mute, solo, pan, gain, PFL, phase invert, Follow.
- Chain: high-pass, RNNoise (wet/dry), gate/expander, 4-band parametric EQ, de-esser,
  compressor, delay — each bypassable.
- Auto lip-sync; ducking; automixer (gain sharing with priority); master multiband
  compressor and true-peak limiter.
- BS.1770-4 / EBU R128 meters (M, S, I, LRA, TP); targets -23 LUFS, -24 LKFS, -16/-14 LUFS;
  optional normalization bounded to ±6 dB and ≤ 1 dB per 10 s.
- Master + 4 aux buses, routing matrix, automatic mix-minus per guest.
- Meters ≥ 30 Hz with peak hold; snapshots with crossfade; multitrack BWF recording.
- OSC (Behringer X32/M32 first); MIDI with motor-fader feedback.
- Web remote audio mixer with live meters, multiple operators with permissions, optional
  WebRTC PFL listen-back.
- Device unplug affects only its channel; resumes on replug.
- Accept: 0 clicks at 10 ms buffer under load; loudness within ±0.1 LU of EBU reference
  files; web mixer reflects moves < 100 ms on LAN.

## 10. Broadcast graphics and Adobe integration
- Template engine: text, image, shape, video, Lottie layers; keyframes; In/Loop/Out.
- Persian/RTL: HarfBuzz shaping, bidi, Persian digits option, kashida justification,
  Jalali date/clock, mirrored layouts. Accept: "پخش زنده ۱۴۰۵ - Live" renders correctly.
- Photoshop PSD: layers kept; text layers → editable fields with font/size/color;
  "#name" → data fields; "@name" → replaceable images; re-import keeps bindings; missing
  fonts reported with a substitution dialog.
- After Effects: Lottie (Bodymovin) with replaceable text and in/loop/out markers; video
  packages (In/Loop/Out with alpha) with overlaid live text.
- Premiere/After Effects preview via NDI as an input.
- Post-production export: FCP XML and CMX3600 EDL of all Program switches referencing ISO
  and Program recordings, graphics as markers.
- Watch folders: files load only after they stop growing for 2 s.
- HTML5 graphics with JS API update(data), play(), next(), stop().
- Built-in TV pack in Persian and English: lower thirds ×4, name supers, full-screen title,
  breaking news, ticker, clock (Gregorian + Jalali), countdown, scoreboards (football,
  basketball, volleyball, wrestling), weather, results bars, social card, logo bug,
  LIVE/REPLAY badges.
- Data: CSV, Excel, JSON, XML, Google Sheets URL, RSS/Atom, REST JSON; on failure keep the
  last good data and warn (never blank on air).
- Rundown with Next/Take/Out, next-item preview, linked audio snapshots.
- Title designer with timeline, safe areas, live preview.

## 11. Live captions
- Whisper on NVIDIA CUDA by default, CPU fallback; Persian and English; model by VRAM
  (large-v3-turbo on ≥ 8 GB, smaller otherwise).
- Silero VAD gates input so silence/music never produces text.
- Modes: Automatic, Operator-approve (default on air), Manual typing.
- Word list, profanity filter, styling, per-output enable, SRT/VTT export.
- Accept: clear Persian speech captioned within 3 s on an RTX 3060-class GPU.

## 12. Remote guests
- Up to 8 browser guests via password links (guest page in Persian or English); waiting
  room; return feed; automatic mix-minus; talkback; 360p–1080p; STUN/TURN; quality meter.
- Accept: 4 guests, separate networks, 30 minutes, < 500 ms latency on broadband.

## 13. PTZ
- VISCA IP/serial, ONVIF, NDI; joystick, zoom, focus, speed; 16 presets per camera;
  the cut waits for a preset move to finish (configurable maximum).

## 14. Outputs
- Recording: MKV or fragmented MP4 while recording, auto-remux to standard MP4/MOV on stop;
  crash leaves a playable file; disk-space warning at 20 min left; optional split to a
  second drive.
- Multicorder ISO with shared timecode.
- Streaming to 3 destinations with stats, auto-reconnect, optional 0–30 s broadcast delay
  with dump button.
- SRT out, NDI out, DeckLink SDI/HDMI out (key/fill, interlaced), virtual camera, replay
  buffer.
- Every output chooses: Program, Clean Program (no overlays/graphics/captions), Preview,
  Multiview, or any input; plus its own audio bus.
- Tracks available NVENC sessions and warns before exceeding them.

## 15. Fullscreen / HDMI outputs
- Up to 4 named profiles, each bound to a display by EDID identity (not Windows index).
- Source: Multiview, Program, Clean Program, Preview, any input; HDMI audio bus or none.
- Format: 720p/1080p/1440p/2160p or custom; 25/29.97/30/50/59.94/60.
- Refresh matching: switch the display to the program rate (e.g. 50 Hz) and restore the
  original mode on stop, exit, or after a crash (watchdog).
- Borderless flip-model by default; optional exclusive fullscreen; no tearing.
- Aspect: 16:9, 4:3, 21:9, 32:9, 9:16, 1:1, custom. Scaling: fit with bars (bar color),
  fill/crop, stretch, pixel-to-pixel, region crop for video walls with bezel compensation.
- Color: auto/full/limited range; Rec.709 8/10-bit; Rec.2020 HLG/PQ when Windows HDR allows
  (otherwise disabled with an explanation); calibration LUT.
- Geometry: overscan 0–10 %, rotation 0/90/180/270, mirror (teleprompter), 4-corner keystone.
- Extras: clock (Jalali/Gregorian), countdown, tally borders/labels, meters + LUFS, safe
  areas, label — in the selected UI language.
- Signal loss: freeze, logo slate, or black.
- Multiview layouts: 2+8, 2×2, 3×3, 4×4, 1+5, custom editor.
- Tools: color bars, identify display, lip-sync test.
- Hot-plug: pauses only that output; resumes on reconnect to the same EDID.
- While active: prevent sleep/screen saver, suppress toasts, topmost on its display only,
  hidden cursor.
- Accept: 50p program on a 60 Hz TV switches to 50 Hz and runs a ticker 10 minutes without
  judder; HDMI unplug/replug resumes < 3 s.

## 16. Instant replay
- Up to 4 inputs; Mark In/Out; 5/10/20 s; 25/50/100 % speed; event list; highlights.

## 17. Remote control
- REST + WebSocket for every action; bilingual web controller; MIDI/OSC learn; shortcuts;
  tally API and phone tally page.
- LAN-only by default, password, HTTPS with generated certificate, rate limiting; internet
  access only via explicit opt-in tunnel.

## 18. Automation
- Shortcut sequences with delays; sandboxed scripts; scheduled actions by clock time.

## 19. Projects and settings
- JSON projects; autosave every 2 minutes and on every on-air state change; crash recovery
  restores inputs, outputs and on-air state.
- Starter templates: Single webcam, Podcast/talk show, Religious ceremony, Sports, Esports,
  Multi-camera event, TV news studio.
- Settings import/export; schema migrations with backup.

## 20. Performance tiers
- First run detects GPU, VRAM, encoders, CPU, RAM, displays and picks:
  Basic (integrated GPU, 8 GB): 1080p30, 4 inputs, lower preview fps, scopes off, small
  caption model on CPU. Standard (GTX 1660/RTX 3050, 16 GB): 1080p50/60, 8 inputs, all
  features. Pro (RTX 3060+, 32 GB): 4K or many HD inputs, ISO, Whisper large.
- Performance panel shows per-module cost and suggestions.
- UI previews drop to 25 fps under load; Program never drops first.

## Phases
Phase 1 — Foundation: solution, Directory.Build.props, localization infrastructure (fa-IR +
  en-US, runtime switching, first-run language screen), IPC engine ↔ UI, render clock,
  D3D11 device selection, HwndHost monitors, color/image/video inputs, Cut and Fade,
  MKV→MP4 recording, Iranian theme resources from docs/DESIGN.md.
Phase 2 — Inputs (section 2).
Phase 3 — Audio core: engine, adaptive resampling, strips, DSP chain, RNNoise, buses,
  routing, meters, A/V sync, auto lip-sync.
Phase 4 — Broadcast audio: loudness, ducking, automixer, master dynamics, snapshots,
  multitrack recording.
Phase 5 — Keying, color pipeline, scopes, grades, camera matching.
Phase 6 — Transitions, stingers, T-bar, on-air safety, layers, overlays, preview overlays,
  virtual sets.
Phase 7 — Graphics engine, Persian shaping, Jalali clock, data sources, rundown, designer,
  built-in TV pack.
Phase 8 — Adobe integration: PSD, Lottie, video packages, HTML5 graphics, watch folders.
Phase 9 — Outputs: streaming + delay, SRT, clean feed, Multicorder, replay buffer, virtual
  camera, NLE export.
Phase 10 — Fullscreen/HDMI outputs and multiview (section 15).
Phase 11 — Remote control and web apps, MIDI, OSC.
Phase 12 — Live captions.
Phase 13 — DeckLink in/out, NDI in/out, PTZ with paint, interlaced output.
Phase 14 — Remote guests and instant replay.
Phase 15 — Hardening: performance tiers, crash recovery, soak tests, installer, release.
