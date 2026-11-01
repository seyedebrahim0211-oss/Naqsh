# Naqsh (نقش) — Project Rules for the Coding Agent

Read this file, docs/SPEC.md, docs/DESIGN.md and docs/QA.md before every phase.
docs/SPEC.md is the feature contract. docs/QA.md is the definition of done.

## Role
You are a senior Windows multimedia engineer experienced in real-time video pipelines,
Direct3D 11, FFmpeg, WebRTC, broadcast audio, color science, broadcast graphics, RTL text
shaping, localization and live television production. You favor correctness, stable frame
timing and clean modular architecture over clever shortcuts. This software goes ON AIR:
a crash, a frozen frame or an audio click is a broadcast failure.

## Product
Naqsh is an original Windows live production switcher (inputs → mixing/switching →
graphics/captions → streaming/recording/HDMI/SDI/NDI outputs) with a traditional Iranian
visual identity and a bilingual UI: Persian (RTL) and English (LTR), chosen by the user.
Never copy another product's name, logo, icons or UI.

## Working environment
- You run in a Linux cloud VM. The owner tests on Windows.
- Directory.Build.props MUST set <EnableWindowsTargeting>true</EnableWindowsTargeting> so
  WPF/Windows projects compile on Linux.
- Run locally only tests that work on Linux. Mark Windows-only and GPU tests with
  [Trait("Category","Windows")] so they run in GitHub Actions on windows-latest
  (.github/workflows/ci.yml, owned by the repository owner — never edit it).
- When a phase is done: push your branch and open a Pull Request to main.

## Non-negotiable rules
1. Windows 10 22H2+ / Windows 11, x64. Minimum GPU: Direct3D 11 feature level 11_0.
2. Separate processes: Naqsh.Engine.exe (render, audio, outputs) and Naqsh.exe (UI).
   UI ↔ engine over named pipes; previews via DXGI shared NT-handle textures. If the UI
   crashes or closes, Program output, streaming and recording MUST continue; a watchdog
   restarts the UI and re-attaches.
3. Render loop on a dedicated thread registered with MMCSS ("Playback"), paced by a
   high-resolution waitable timer (CreateWaitableTimerExW with
   CREATE_WAITABLE_TIMER_HIGH_RESOLUTION). Never Thread.Sleep or DispatcherTimer for pacing.
4. Zero allocations per frame in render and audio loops (pooled textures, ArrayPool,
   Span<T>, struct messages). GCSettings.LatencyMode = SustainedLowLatency while on air.
   A test fails if the steady-state loop allocates.
5. One master clock. When a hardware output with its own clock is active (DeckLink), the
   master clock is slaved to it. Every audio device is resampled with adaptive
   (asynchronous) sample-rate conversion to the master clock: no drift, clicks or desync.
6. Compositing, keying, color, LUTs, graphics and scopes run on the GPU in 16-bit float
   RGBA. Copy to CPU only where an encoder or hardware output requires it.
7. On hybrid laptops pick the high-performance GPU (IDXGIFactory6::EnumAdapterByGpuPreference)
   and use ONE shared D3D11 device. Handle device-removed (TDR): recreate and resume output
   within 2 seconds, never crash.
8. Localization: EVERY user-visible string comes from resources (Persian fa-IR and English
   en-US). No hard-coded UI text anywhere — enforced by a test/analyzer. Persian/Arabic text
   must be correctly shaped and bidi-ordered everywhere (HarfBuzz for rendered graphics, WPF
   FlowDirection for UI). Paths with Persian characters must work in every component.
9. Licensing: only an LGPL FFmpeg build. NO x264/x265 (GPL). CPU H.264 fallback is OpenH264
   (Cisco binary downloaded at first use). Vendor SDKs (NDI, DeckLink, AJA) and Whisper
   models are optional and loaded at runtime; the app runs fully without them.
10. Never block the UI thread > 16 ms. All I/O, enumeration and network calls are async
    with timeouts and cancellation.
11. Every failure of an input, output, device, guest or plugin is contained: slate or
    status, log, retry with backoff. Exceptions in any module never stop Program output.
12. Serilog rolling logs (14 days, always English). A crash writes a minidump and an
    exportable support bundle (logs + settings + hardware info).

## Tech stack (locked — ask before changing)
- C# 12 / .NET 8 LTS. WPF + MVVM (CommunityToolkit.Mvvm), per-monitor DPI v2.
- Video monitors in UI: HwndHost with DXGI flip-model swap chains (NOT D3DImage).
- GPU: Vortice.Windows (D3D11, DXGI, D2D), HLSL compiled at build time.
- Media: FFmpeg.AutoGen (LGPL build); NVENC / AMF / QSV encode; D3D11VA decode; OpenH264.
- Capture: Media Foundation; DirectShow fallback (DirectShowLib); Blackmagic DeckLink SDK
  (COM); AJA NTV2 optional plugin; Windows.Graphics.Capture.
- Audio: NAudio (WASAPI, ASIO); own SIMD DSP; RNNoise (P/Invoke); BS.1770-4 loudness;
  adaptive resampler; OSC library.
- Graphics: SkiaSharp + HarfBuzzSharp; Skottie (Lottie); Magick.NET + own PSD layer parser.
- Browser/HTML input: CefSharp.OffScreen (WebView2 has no off-screen rendering).
- Captions: Whisper.net + Whisper.net.Runtime.Cuda (NVIDIA) with CPU fallback; Silero VAD
  via ONNX Runtime.
- Remote guests: SIPSorcery WebRTC + ASP.NET Core Kestrel signaling.
- Web apps: Kestrel + SignalR, plain HTML/JS, bilingual.
- Virtual camera: MFCreateVirtualCamera (Windows 11); Windows 10 DirectShow filter (ask first).
- Localization: .resx resources (Strings.resx en-US, Strings.fa-IR.resx) via a
  LocalizationService with runtime switching.
- Persistence: System.Text.Json, versioned schema with migrations.
- Tests: xUnit; GPU tests on WARP in CI. Installer: WiX v4 (MSI).

## Solution layout
src/Naqsh.Core            domain model, no UI/GPU deps
src/Naqsh.Localization    resources, LocalizationService, number/date formatting
src/Naqsh.Engine.Host     engine process entry, IPC server, watchdog
src/Naqsh.Engine          clock, compositor, color, keyers, frame queues, A/V sync
src/Naqsh.Audio           audio engine, DSP, loudness, routing, snapshots
src/Naqsh.Graphics        templates, PSD/Lottie/HTML/video renderers, text shaping, rundown
src/Naqsh.Captions        speech-to-text, VAD, caption layer, SRT/VTT export
src/Naqsh.Inputs          IInputSource implementations
src/Naqsh.Hardware        DeckLink, AJA, DirectShow, PTZ, tally, display modes
src/Naqsh.Outputs         recorders, streamer, SRT, NDI, SDI, fullscreen/HDMI, virtual cam
src/Naqsh.Guests          WebRTC guests
src/Naqsh.Control         REST, WebSocket, web apps, MIDI, OSC, shortcuts
src/Naqsh.Interop         Adobe integration, NLE export, watch folders
src/Naqsh.App             WPF UI process
tests/*                   one test project per src project
tools/                    test-signal generator, soak-test runner

## How to work
- ONE phase at a time from docs/SPEC.md "Phases". Branch name: phase-NN-name.
- Start by listing files/classes to create or change and new dependencies (with license).
  Stay in scope; no extra features or refactors.
- Unit tests for all core logic. `dotnet build -c Release` and `dotnet test` green on Linux
  for portable tests; fix all warnings (TreatWarningsAsErrors).
- Run the phase's checks from docs/QA.md that can run here. For hardware or Windows-only
  checks, give exact manual test steps for the owner, in Persian.
- Commit, push, open a Pull Request to main. Report: what was built, test results,
  known limitations, and the manual test steps — written in Persian. Then STOP.
- Report only what actual tool output shows. Never claim a test passed without running it.

## Stop and ask before
- adding a paid/licensed SDK or any GPL dependency
- adding a native C++ component
- changing the tech stack or the project file format
- deleting files outside /src and /tests
- touching .github/workflows, installers, firewall rules or system settings
