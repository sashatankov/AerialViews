# Apple catalog maintenance

Run `python scripts/import_apple_catalog.py` from the Windows project directory
to rebuild `Data/apple.json` and `Data/apple_strings.json`. Python 3.10+ is
required; no extra packages are needed. Use `--check` to compare a fresh import
with the bundled files without writing them. Importing requires network access;
the application reads only the bundled JSON and never fetches manifests.

The importer merges by Apple's asset ID, preserves legacy encodes and metadata,
and imports English names and POI translations from Apple's binary property
lists. New footage uses Apple's category metadata with reviewed scene/time
overrides in the script. Sources, archive checksums, input counts, and addition
counts are recorded in the generated manifest.

| Source | Input assets | Added assets |
| --- | ---: | ---: |
| Existing tvOS 15 catalog | 114 | 114 |
| Sonoma (`resources-14-0-10.tar`) | 134 | 20 |
| Sequoia (`resources-15-0-2.tar`) | 137 | 3 |
| Tahoe (`resources-26-0-1.tar`) | 152 | 15 |
| Golden Gate asset snapshot | 4 | 4 |
| Combined unique videos | | 156 |

Golden Gate definitions are copied as data from the source linked in the script;
the importer does not execute downloaded code. Its advertised macOS 27 manifest
was unavailable when this snapshot was created. All videos remain on Apple's CDN.
The HTTP manifest URLs match the existing workaround for that CDN's certificate
chain; TLS verification is not disabled.

## Verification

Run `dotnet build`, then
`dotnet test ../AerialViewsWin.Tests/AerialViewsWin.Tests.csproj`.
The tests check catalog integrity, preserved legacy settings and URL selection,
4K fallback, filters, translated POIs, cache filenames, and matching playback and
download lists. They do not download the videos.

For manual Windows verification:

- Run `dotnet run -- /c`. Confirm the Apple group shows 156 videos and the 4K
  fallback note fits. Change quality/filters and verify settings persist.
- In the Apple cache tab, start a download and cancel it. Confirm cancellation
  finishes and no partial file is treated as a cached video. Avoid clearing an
  existing user cache just to test this feature.
- Run `dotnet run -- /s`. Check footage from each new collection, location
  descriptions, transitions, and exit with mouse/keyboard input. Check streaming
  and cached playback; use multiple displays when available.
- Inspect `%AppData%\AerialViews\debug.log` for failures introduced by the run.

Some new scenes offer only 4K SDR HEVC. These use that encode for every quality
setting while older videos retain their existing quality selection.
