"""Rebuild the bundled Apple catalog using Python 3's standard library.

Run from any directory: python scripts/import_apple_catalog.py [--check]
Only manifests are fetched; videos stay on Apple's CDN. HTTP matches the
application's existing workaround for sylvan.apple.com's certificate chain.
The pinned releases make the import independent of Apple's moving latest feed.
"""

import argparse
import hashlib
import io
import json
from pathlib import Path
import plistlib
import tarfile
import urllib.request


DATA = Path(__file__).resolve().parents[1] / "Data"
MANIFEST_BASE = (
    "http://sylvan.apple.com/itunes-assets/Aerials126/v4/82/2e/34/"
    "822e344c-f5d2-878c-3d56-508d5b09ed61/"
)
RELEASES = [
    ("Sonoma", "resources-14-0-10.tar", 134),
    ("Sequoia", "resources-15-0-2.tar", 137),
    ("Tahoe", "resources-26-0-1.tar", 152),
]
GOLDEN_GATE_SOURCE = (
    "https://github.com/chen86860/golden-gate-wallpapers/blob/"
    "main/install.py"
)
GOLDEN_GATE_CDN = (
    "https://sylvan.apple.com/itunes-assets/Aerials116/v4/cb/5b/50/"
    "cb5b5035-6701-619f-9065-3d7d0e5fbef4/"
)
# Snapshot of Apple's IDs, shot IDs and filenames published by the source above.
# No remote Python code is executed during import.
GOLDEN_GATE = [
    ("4207734D-74FE-4F92-B5E1-6EC8DEE24A15", "Golden Gate Sunset", "GG_A_SUNSET",
     "GG_A_SUNSET_MarshallsBeach_c28_v5_Final01_HFR_HEVC.mov", "sunset"),
    ("6511D2B5-E185-4886-9505-B4004E920D27", "Golden Gate Day", "GG_A_DAY",
     "GG_A_DAY_Bluffs_C35_V25_HFR_HEVC.mov", "day"),
    ("3FD9FD5C-6DC5-4362-9932-4087C942F1C5", "Golden Gate Evening", "GG_A_EVENING",
     "GG_A_EVENING_TorpedoWharf_C35_v22_HFR_HEVC.mov", "sunset"),
    ("86E89C23-C39B-44C8-A985-E56EEA6456FE", "Golden Gate Night", "GG_A_NIGHT",
     "GG_A_NIGHT_BatterySpencer_c9_v8_Final01_HFR_HEVC.mov", "night"),
]
CATEGORY_SCENES = {
    "A33A55D9-EDEA-4596-A850-6C10B54FBBB5": "nature",
    "5EF41171-4862-4F93-800C-AD86CE5E6891": "city",
    "8BE8B524-6EAE-43F5-A3E8-01DCFA1BCD4B": "sea",
    "55B7C95D-CEAF-4FD8-ADEF-F5BC657D8F6D": "space",
}
# Reviewed exceptions to the broad Landscape category. New shots default to day;
# named morning scenes are day, and evening scenes use the existing Sunset filter.
SCENE_OVERRIDES = {
    "W010_C003_F01": "countryside", "S013_C001_F01": "countryside",
    "S011_C003_F01": "countryside", "S009_C018_F01": "countryside",
    "S005_C013_F01": "countryside", "W014_C018_F01": "countryside",
    "W015_C010_F01": "countryside", "W015_C006_F01": "countryside",
    "R006_C013_S05": "beach", "R004_C012_F01": "beach",
    "ANN0010": "countryside", "ANN0040": "countryside",
    "ANN0060": "beach", "ANN0080": "beach",
    "ANN0100": "countryside", "ANN0130": "countryside",
}
TIME_OVERRIDES = {
    "S013_C001_F01": "sunset", "R006_C013_S05": "sunset",
    "M012_C065_S04": "sunset", "A007_C0001": "sunrise",
    "A018_C0002": "night", "TA_D_001": "sunset", "TA_D_002": "night",
}


def read_release(filename):
    with urllib.request.urlopen(MANIFEST_BASE + filename, timeout=60) as response:
        raw = response.read()
    with tarfile.open(fileobj=io.BytesIO(raw)) as archive:
        # Read members in memory, without extracting archive paths onto disk.
        entries = next(m for m in archive if m.name.endswith("/entries.json"))
        strings = next(m for m in archive if m.name.endswith("en.lproj/Localizable.nocache.strings"))
        manifest = json.load(archive.extractfile(entries))
        translations = plistlib.loads(archive.extractfile(strings).read())
    return manifest, translations, hashlib.sha256(raw).hexdigest()


def build_catalog():
    legacy = json.loads((DATA / "tvos15.json").read_text(encoding="utf-8"))
    strings = json.loads((DATA / "tvos15_strings.json").read_text(encoding="utf-8"))
    assets = {asset["id"]: asset for asset in legacy["assets"]}
    sources = [{"name": "tvOS 15", "url": "Data/tvos15.json", "assetCount": len(assets), "addedCount": len(assets)}]
    for name, filename, expected_count in RELEASES:
        manifest, translations, checksum = read_release(filename)
        incoming = manifest["assets"]
        if len(incoming) != expected_count:
            raise ValueError(f"{name}: expected {expected_count} assets, got {len(incoming)}")
        before = len(assets)
        for asset in incoming:
            asset_id = asset["id"]
            if asset_id in assets:
                # Preserve legacy URLs, POI and classification; add macOS encodes.
                for key, value in asset.items():
                    if key.startswith("url-") and value:
                        assets[asset_id][key] = value
                continue
            shot = asset["shotID"]
            scene = SCENE_OVERRIDES.get(shot)
            if scene is None:
                # Tahoe's four category UUIDs are absent from the category table.
                scene = "nature" if shot.startswith("TA_") else next(
                    CATEGORY_SCENES[c] for c in asset["categories"] if c in CATEGORY_SCENES
                )
            title = translations.get(asset.get("localizedNameKey")) or asset.get("accessibilityLabel")
            if not title:
                raise ValueError(f"Missing English title for {asset_id}")
            assets[asset_id] = {
                "id": asset_id, "shotID": shot, "title": title,
                "accessibilityLabel": asset.get("accessibilityLabel") or title,
                "scene": scene, "timeOfDay": TIME_OVERRIDES.get(shot, "day"),
                "categories": asset["categories"],
                "pointsOfInterest": asset.get("pointsOfInterest", {}),
                **{k: v for k, v in asset.items() if k.startswith("url-")},
            }
        # Keep translations associated with legacy footage unchanged.
        for key, value in translations.items():
            strings.setdefault(key, value)
        sources.append({"name": name, "url": MANIFEST_BASE + filename,
                        "sha256": checksum, "assetCount": len(incoming), "addedCount": len(assets) - before})

    for asset_id, title, shot, filename, time in GOLDEN_GATE:
        assets[asset_id] = {
            "id": asset_id, "shotID": shot, "title": title, "accessibilityLabel": title,
            "scene": "countryside", "timeOfDay": time,
            "pointsOfInterest": {"0": shot + "_0"},
            "url-4K-SDR-240FPS": GOLDEN_GATE_CDN + filename,
        }
        strings[shot + "_0"] = title
    sources.append({"name": "Golden Gate", "url": GOLDEN_GATE_SOURCE, "assetCount": 4, "addedCount": 4})
    if len(assets) != 156:
        raise ValueError(f"Expected 156 unique assets, got {len(assets)}")
    for asset in assets.values():
        for key in asset.get("pointsOfInterest", {}).values():
            if not strings.get(key):
                raise ValueError(f"Missing POI translation: {key}")
    return {"version": 1, "initialAssetCount": len(assets), "sources": sources,
            "assets": list(assets.values())}, strings


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="verify bundled files without writing")
    args = parser.parse_args()
    catalog, strings = build_catalog()
    for filename, content in [("apple.json", catalog), ("apple_strings.json", strings)]:
        rendered = json.dumps(content, ensure_ascii=False, indent=2) + "\n"
        path = DATA / filename
        if args.check:
            if path.read_text(encoding="utf-8") != rendered:
                raise ValueError(f"{filename} is out of date; rerun the importer")
        else:
            path.write_text(rendered, encoding="utf-8", newline="\n")
    print("Apple catalog: 156 unique videos (114 existing + 42 additions)")


if __name__ == "__main__":
    main()
