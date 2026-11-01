# Naqsh — Visual Identity and UI

## Concept
Traditional Iranian architecture as a calm, dark broadcast tool: night-lapis surfaces
(dome interiors), Persian turquoise for interaction (tilework), saffron gold for brand and
primary actions (gilded calligraphy), girih geometry as quiet ornament. Ornament appears
only in the title bar, dialog headers, splash and empty states — never inside monitors,
meters or controls. Original artwork only.

## Color tokens (dark theme, default)
| Token            | Hex     | Use |
|------------------|---------|-----|
| bg.base          | #0B1626 | window background |
| bg.panel         | #10203A | panels, header, footer |
| bg.raised        | #1C3352 | buttons, cards |
| bg.monitor       | #050B14 | video wells |
| border           | #2A4A6E | dividers, idle borders |
| text.primary     | #F1E8D6 | text |
| text.secondary   | #B7C3D4 | labels |
| accent.turquoise | #2BB3A6 | selection, active toggles, focus ring |
| accent.gold      | #E0A93B | brand, Cut/Take, headings |
| tally.program    | #C8323F (fill #B3263A) | Program / on air / recording |
| tally.preview    | #3FAE6B (fill #2E8A55) | Preview |
Program vs Preview also differ by label and position, never by color alone. Text ≥ 4.5:1
contrast. Light theme for daylight venues: same hues on warm paper #F4EEE2.

## Typography
- UI: Vazirmatn 400/500/700/800 (bundled), base 14 px, for both languages.
- Brand/display: Lalezar (bundled) for the Persian logo/splash/dialog titles; in English
  the logo word "Naqsh" uses Vazirmatn 800.
- Timecode and technical values: tabular figures; LTR runs isolated inside RTL text.

## Ornament
- Girih band (8-point star + rotated square) at 14–18 % opacity in the title bar.
- Logo: «نقش» / "Naqsh" in gold beside a khatam mark (two overlapping squares) with a
  turquoise center.
- Splash: girih field + logo. First-run screen: language (فارسی / English), region
  (sets 25/50 vs 29.97/59.94 defaults), performance tier.
- Eslimi flourish only in About and empty states.

## Main window
- Header: logo, workspace tabs (Director/کارگردانی, Audio/صدا, Color/رنگ,
  Graphics/گرافیک), Simple/Pro toggle, language button (FA | EN), status buttons
  (Record, Stream, Multicorder, HDMI Out), on-air clock with date (Jalali or Gregorian).
- Monitor row, always LTR: Preview | transition column (Cut, Fade, Stinger, T-bar, FTB) |
  Program.
- Overlay channel buttons 1–8 under the monitors.
- Input grid tiles: thumbnail, number, name, tally badge, Preview and Direct buttons.
- Side panel (Pro mode): audio mixer with LUFS, graphics rundown, live captions queue.
- Footer: fps, dropped frames, CPU, GPU, encoder sessions, project format.
- In Persian the layout mirrors (side panel on the left, text right-aligned); in English
  it is the standard LTR layout. Only the monitor row never mirrors.
- Simple mode hides side panel, scopes and advanced menus; big tiles; one-click
  stream/record. Pro mode shows everything.
- Minimum window 1280×720; dockable panels; layouts saved per workspace.
- Touch targets ≥ 32 px (≥ 44 px in Simple mode). Every action has a shortcut shown in
  its tooltip.

## Fullscreen/HDMI settings window
Profile tabs per output; cards: Display & source, Video format, Aspect & scaling,
Color & signal, Geometry, On-output extras; side column: live output preview, multiview
layout picker, test tools.

## Motion
150 ms ease-out for panels; no animation on monitors, tally or meters (tally is instant).
