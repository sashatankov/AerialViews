---
name: verify
description: Build, run, and observe the AerialViews screensaver to verify playback/transition changes end-to-end via debug.log.
---

# Verifying AerialViews changes

Build: `dotnet build`, run: `bin\Debug\net8.0-windows\AerialViews.exe /s` (fullscreen, no windowed mode). The app exits on >5px mouse movement, so launch it with `Start-Process`, `Start-Sleep` for the observation window, then `Stop-Process -Name AerialViews -Force` if still running.

**Evidence is `%AppData%\AerialViews\debug.log`** — `ScreensaverWindow.SetState` logs every transition (`Loading → FadingIn → Playing → FadingOut → Stopping`), and `StopPlayersThen` logs stop timing. Record the log's line count before the run (`(Get-Content $log | Measure-Object -Line).Lines`), then `Select-Object -Skip $n` after.

Fast transition cycles: back up `%AppData%\AerialViews\settings.json`, set `Playlist.MaxVideoLengthSeconds = 10`, restore afterwards. A healthy cycle is ~11 s (7 s play + 2 s fade + ~0.3 s stop + 1–3 s stream load).

Gotchas:
- **Screenshots via `CopyFromScreen` do not capture the fade** — the black `FadeOverlay` lives in a transparent layered `OverlayWindow`, which `SourceCopy` skips. A screenshot showing video while the log says `Stopping` is expected, not a bug.
- Simulate exit-on-input with `user32 SetCursorPos` (define your own `PT` struct in `Add-Type`; `System.Drawing.Point` fails type-forwarding in pwsh 7). Expect a `CloseScreensaver reason=CursorPoll` log line and process exit within ~1 s.
- A hang symptom (state log stops advancing, e.g. stuck after `Stopping`) historically meant a UI-thread ↔ VLC deadlock: never call or read `MediaPlayer` members on the UI thread while a background `Stop()` may be running.
- Loop mode (`Playlist.LoopUntilSkipped = true`): expect exactly one `Loading/FadingIn/Playing` and then silence — VLC loops natively via `:input-repeat`.
