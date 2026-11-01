# Naqsh — Quality Plan (definition of done)

No software is literally bug-free; this plan makes on-air failures rare, contained and
recoverable. A phase is done only when its rows pass and are reported with evidence.

## Automated (every push, GitHub Actions on windows-latest)
- Release build with TreatWarningsAsErrors; nullable enabled; .NET analyzers recommended.
- Unit tests: clock math (fractional rates), resampler drift, BS.1770 loudness vs EBU
  reference files, DSP filters, LUT/color math, keyers, HarfBuzz shaping + bidi on a
  Persian corpus, Jalali conversion, PTZ/OSC encoding, EDL/XML export, schema migrations.
- Localization: every resource key exists in fa-IR and en-US; no hard-coded UI strings;
  pseudo-localization run (longer strings) shows no clipped controls.
- GPU golden-image tests on WARP for compositor, transitions, keyers.
- Allocation test: steady-state render and audio loops allocate 0 bytes over 1,000 ticks.

## Phase checks on the owner's Windows PC
- Frame timing: 30 min 1080p50, 4 inputs: 0 dropped Program frames, jitter < 1 ms.
- A/V sync with flash+beep generator through every input/output: ≤ 1 frame over 2 hours.
- Audio: 10 ms buffer, full load, 2 hours: 0 clicks; two USB audio devices: no drift.
- Recording: kill engine mid-record → file plays; normal stop → MP4 opens in Premiere Pro
  and DaVinci Resolve.
- Kill the UI while streaming → stream/record continue; UI re-attaches.
- Unplug/replug camera, capture card, audio interface, HDMI display → only that item
  affected; recovers < 3 s.
- Simulated GPU device removal → output resumes < 2 s.
- Internet drop 30 s while streaming to 3 destinations → reconnects; recording unaffected.
- Language: switch FA ↔ EN while streaming → 0 dropped frames, whole UI flips correctly,
  monitor row unchanged; Persian paths/names/titles/captions correct.
- Fullscreen/HDMI: 50 Hz matching, full vs limited range with bars, rotation/mirror/
  keystone, hot-plug, two outputs at once.
- DPI 100/150/200 % and mixed-DPI monitors.

## Soak test before each release
8 hours: 8 inputs, streaming + recording + ISO + 2 HDMI outputs + captions: 0 crashes,
0 dropped Program frames, memory growth < 5 %, stable handle count.

## Hardware matrix (before release)
Basic: Intel Iris Xe laptop 8 GB — 1080p30, 4 inputs, stream + record.
Standard: GTX 1660 Super / RTX 3050, 16 GB — 1080p50, 8 inputs, all features.
Pro: RTX 3060 12 GB+, 32 GB — 4K or 12 HD inputs, ISO, Whisper large.
Hybrid laptop Intel + NVIDIA — engine on NVIDIA, desktop capture works.
Windows 10 22H2 and Windows 11 24H2+.

## Pre-flight checklist (built into the app)
All inputs "Safe to air"; disk space ≥ 2× show length; stream test to a private target;
loudness target set; clock synced (NTP); notifications suppressed; High performance power
plan; laptop on AC; backup recording on.
