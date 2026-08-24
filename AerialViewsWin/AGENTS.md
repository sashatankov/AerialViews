# Repository Guidelines

## Project Structure & Module Organization

This directory contains the .NET 8 Windows port of Aerial Views. `Program.cs` implements the screensaver entry-point contract; `App.xaml` defines shared WPF resources. Keep domain types and settings in `Models/`, catalog and playlist logic in `Providers/`, and playback, caching, or monitor coordination in `Services/`. WPF windows belong in `UI/`, with `.xaml` and `.xaml.cs` paired. `Data/` holds embedded manifests and localized strings. Treat `bin/`, `obj/`, and `publish/` as generated output.

## Build, Test, and Development Commands

- `dotnet restore` — restore LibVLCSharp, LibVLC, and Newtonsoft.Json packages.
- `dotnet build` — compile the default Debug configuration and surface compiler warnings.
- `dotnet run -- /c` — launch the settings UI (`/c` is also the default mode).
- `dotnet run -- /s` — run the full-screen, multi-monitor screensaver; be prepared for mouse movement or keyboard input to exit it.
- `dotnet publish -c Release -r win-x64 --self-contained false -o publish` — create a framework-dependent Windows release bundle. Rename/copy `AerialViews.exe` to `AerialViews.scr` when validating installation behavior.

## Coding Style & Naming Conventions

Follow the existing C# style: four-space indentation, braces on new lines, namespace blocks, and nullable-reference annotations enabled. Use `PascalCase` for types, methods, properties, and XAML element names; use `camelCase` for parameters and locals, and `_camelCase` for private fields. Keep XAML indentation at four spaces and place substantial behavior in code-behind or a service. Run `dotnet format` before broad formatting changes, and do not mix reformatting with feature work.

## Testing Guidelines

There is currently no automated test project or coverage threshold. Every change must at least pass `dotnet build`. Manually exercise the affected `/c` or `/s` path; playback changes should cover video transitions and shutdown, and monitor changes should be checked on multiple displays when available. Verify persisted settings and diagnostics under `%AppData%\AerialViews\`; runtime failures are recorded in `debug.log`. If adding tests, create a separate `AerialViewsWin.Tests` project and name cases `Method_Scenario_ExpectedResult`.

## Commit & Pull Request Guidelines

Recent history favors short imperative subjects, sometimes using Conventional Commit prefixes such as `feat:`. Prefer focused messages like `fix: prevent VLC shutdown deadlock`; avoid vague subjects such as `misc`. Pull requests should explain user-visible behavior, list build/manual verification, and link relevant issues. Include screenshots for settings or overlay changes, and note Windows version, display configuration, or VLC-related assumptions when relevant.
