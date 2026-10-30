# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

AerialViews is a Windows screensaver (`.scr`) built on .NET 8 / WPF that plays Apple TV / Fire TV style aerial
drone footage as a screensaver, with configurable clock/date/location text overlays. It is a Windows port of an
Android/Fire TV "Aerial Views" app — the video manifests (`Data/*.json`) come from those platforms' catalogs.

## Commands

```
dotnet build                                          # debug build
dotnet publish -c Release -r win-x64 --self-contained false   # produces ./publish (framework-dependent, requires .NET 8 Desktop Runtime on target machine)
```

There is no test project in this repo and no lint/format tooling configured.

### Installing as a screensaver

After publishing, copy `publish\AerialViews.exe` to `publish\AerialViews.scr` (same binary, renamed) and place
it where Windows screensavers are installed, or right-click → Install. The app follows the standard Windows
screensaver CLI contract, parsed in `Program.cs` (`ParseMode`):
- `/s` — run the screensaver (`MultiMonitorManager.LaunchScreensaver`)
- `/p` — preview mode (currently just exits; no embedded preview panel is implemented)
- `/c` or no args — open `SettingsWindow` (configuration UI)

### Debugging

The app has no console; a plain-text log is written to `%AppData%\AerialViews\debug.log` (see `Program.cs` and
`ScreensaverWindow`'s private `Log`) — check this first when diagnosing screensaver launch/lifecycle issues,
since crashes here are otherwise silent (screensavers run detached from a terminal).

## Architecture

### Entry point is not the usual WPF entry point

`Program.cs` is a custom `Main` (`StartupObject` in the csproj, `EnableDefaultApplicationDefinition=false`)
instead of the default WPF `App.xaml` startup URI. This is required so the same executable can behave
differently depending on the `/s`, `/c`, `/p` argument Windows passes it (see above).

### Data flow: JSON manifests → AerialMedia → playlist → playback

1. **`Data/*.json`** — per-source video catalogs (`tvos15` = Apple, `fireos8` = Amazon Fire TV, `comm1`/`comm2` =
   community sources), each with a companion `*_strings.json` for translating point-of-interest (POI) codes into
   display text. These are embedded resources (see `<EmbeddedResource Include="Data\**\*.json" />` in the
   csproj), not loose files — `VideoProvider.LoadEmbeddedResource` pulls them via reflection at runtime.
2. **`Models/VideoAsset.cs`** — deserialized shape of a manifest entry (raw JSON). `VideoAsset.GetUrl` resolves
   the right URL per `VideoQuality` and `AerialMediaSource`, with source-specific quirks (e.g. Apple URLs are
   forced from `https://` to `http://` because Apple's CDN cert is invalid for LibVLC; Comm2 remaps its "HDR"
   field to SDR because it's just 10-bit color, not real HDR).
3. **`Providers/VideoProvider.cs`** — turns a manifest + `SourceSettings` (enabled scenes/times-of-day/quality
   filters) into a flat list of `AerialMedia` (the playback-ready model) plus a `metadata` dict keyed by
   filename-without-extension for POI lookups.
4. **`PlaylistManager`** (same file) — aggregates all four sources into one playlist, applies dedup/shuffle, and
   exposes `GetNext`/`GetPrevious`. `GetDescription` picks the POI text whose timestamp is closest to (but not
   after) the video's current playback position, falling back to the asset's title/accessibility label.
5. **`Models/ScreensaverSettings.cs`** — all user-configurable settings (`SourceSettings` × 4, `OverlaySettings`,
   `AppearanceSettings`, `PlaylistSettings`), persisted as JSON to `%AppData%\AerialViews\settings.json` via
   `Load()`/`Save()`. `SettingsWindow` reads/writes this; `ScreensaverWindow` only reads it at startup.

### Playback: LibVLC + multi-monitor sync

- Video rendering uses **LibVLCSharp** (bundled `VideoLAN.LibVLC.Windows` native libs), not `MediaElement`.
- `Services/VideoCacheManager.cs` optionally caches videos locally under `%AppData%\AerialViews\Cache\<Source>`;
  `ResolvePlaybackUri` transparently prefers the cached file over streaming when present. Downloads are
  triggered manually per-source from `SettingsWindow` with progress/cancellation support.
- **Multi-monitor is coordinated by `Services/MultiMonitorManager.cs`** (static class): the primary display gets
  a full `UI/ScreensaverWindow` (overlays, progress bar, input detection); every other display gets a lightweight
  `UI/SecondaryScreensaverWindow` (video only). `MultiMonitorManager` drives all secondary windows to mirror the
  primary's media, fades, and appearance settings in lockstep (`PlayOnSecondaries`, `FadeInSecondaries`, etc.),
  including sharing the same random start offset so multi-monitor playback stays in sync.
- Secondary windows are positioned with raw Win32 `SetWindowPos` (P/Invoke) rather than WPF's `Left`/`Top`,
  because WPF's DPI-scaled coordinates don't line up correctly across monitors with different DPI settings.
- **`UI/OverlayWindow`** is a separate transparent, click-through-less window layered on top of the video window
  on both primary and secondary screens. It owns the black fade overlay (crossfade between videos), brightness
  dimming, top/bottom gradients, the progress bar, and (primary only) the 8 text overlay slots. Fades are driven
  by `DoubleAnimation`s on `FadeOverlay.Opacity`, not by touching the video itself.
- `ScreensaverWindow` pre-emptively starts the fade-out **before** the video actually ends (when remaining time
  ≤ fade-out duration), so the transition plays out over live video instead of freezing on the last frame; the
  `OnMediaEndReached` handler is only a fallback for when that pre-emptive fade didn't get a chance to trigger.
- Screensaver exit-on-input is polled via Win32 `GetCursorPos` on a timer (`_mouseCheckTimer`), not just WPF
  mouse events — LibVLC's native video surface can capture the HWND and make WPF pointer events unreliable.
- VLC teardown (`mediaPlayer.Stop()`/`Dispose()`) is always pushed onto `Task.Run` before shutting down the
  `Application`, because `Stop()` blocks on VLC's own threads which in turn need the UI thread's message pump —
  calling it synchronously on the UI thread deadlocks.

### Overlay text slots

`OverlaySettings` defines 8 independent slots (`SlotTopLeft1/2`, `SlotTopRight1/2`, `SlotBottomLeft1/2`,
`SlotBottomRight1/2`), each assignable to any `OverlayType` (Clock, Date, Location, 4 custom messages,
Countdown, or Empty). `ScreensaverWindow.GetOverlayContent` is the single dispatch point mapping a slot's type
to its rendered text; add new overlay types there plus `Models/Enums.cs` and `SettingsWindow`'s
`PopulateOverlayComboBoxes`.
