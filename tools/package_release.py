"""Builds the TESL: Everlasting release downloads from an explicit list of files, then privacy-scans them.

  python tools/package_release.py --out <folder> [--names "private,words"]

Makes, in <folder>:
  TESL-Everlasting-<version>.zip               plugin, data and docs (for game folders that already have BepInEx 5)
  TESL-Everlasting-<version>-with-BepInEx.zip  the same plus official BepInEx 5.4.23 (x86) from this game folder
  TESL-Everlasting-<version>-source.zip        the public source (for the GitHub repository): code, data, docs, scripts
  SHA256SUMS.txt
Each zip unpacks into the game folder. Nothing is uploaded or published. Run Build.ps1 first (the DLLs come from build/).
--names: extra private words to search for (the owner's account/real names); they are never written into any file.
"""
from __future__ import annotations

import argparse
import hashlib
import re
import sys
import zipfile
from pathlib import Path

ENGINE = Path(__file__).resolve().parent.parent
GAME = ENGINE.parent.parent
PLUGIN = "BepInEx/plugins/TESLOffline"
PATCHER = "BepInEx/patchers/TESLOffline"
PLAYER_DOCS = ["README.md", "CHANGELOG.md", "LICENSE", "NOTICE.md", "DISCLAIMER.md", "CREDITS.md"]
PLAYER_GUIDES = ["getting-started.md", "f10-menu.md", "faq.md", "troubleshooting.md", "LICENSING.md", "building.md"]
BEPINEX_VERSION = "5.4.23"
LAUNCHER = ("@echo off\r\n"
            "rem TESL: Everlasting - start The Elder Scrolls: Legends offline (against the in-game AI).\r\n"
            "start \"\" \"%~dp0The Elder Scrolls Legends.exe\" --tesl-offline-native\r\n")
# Public source: code and the files needed to build and test it. Internal notes, evidence and binaries stay out.
SOURCE_GLOBS = ["*.cs", "Build.ps1", "Publicize-Reference.ps1", "Enable-Offline.ps1", "Disable-Offline.ps1", "Launch-Offline.ps1", "Run-Audit.ps1",
                "Stop-Diagnostic.ps1", "Save-DiagnosticEvidence.ps1", "Run-NativeDiagnostic.ps1", "Run-VisibleDiagnostic.ps1",
                "data/**/*", "fixtures/**/*", "tools/*.py", "tools/*.sh", "tools/*.md", "tools/*.json", "licenses/*",
                "docs/**/*"] + PLAYER_DOCS
SOURCE_SKIP = re.compile(r"(^|/)(__pycache__|archive)(/|$)|\.pyc$|docs/PUBLIC-RELEASE-LAYOUT\.md$")


def version() -> str:
    m = re.search(r'BepInPlugin\("local\.tesl\.offline", "TESL: Everlasting", "([0-9.]+)"\)', (ENGINE / "OfflineBootstrapPlugin.cs").read_text(encoding="utf-8"))
    if not m:
        raise SystemExit("version not found in OfflineBootstrapPlugin.cs")
    return m.group(1)


def add(z: zipfile.ZipFile, src: Path, arc: str) -> None:
    if not src.is_file():
        raise SystemExit(f"missing: {src}")
    z.write(src, arc)


def player_files(z: zipfile.ZipFile) -> None:
    add(z, ENGINE / "build" / "TESL.Offline.Engine.dll", f"{PLUGIN}/TESL.Offline.Engine.dll")
    add(z, ENGINE / "build" / "TESL.Offline.Access.dll", f"{PATCHER}/TESL.Offline.Access.dll")
    for p in sorted((ENGINE / "data").rglob("*")):
        if p.is_file():
            z.write(p, f"{PLUGIN}/data/{p.relative_to(ENGINE / 'data').as_posix()}")
    for name in PLAYER_DOCS:
        add(z, ENGINE / name, f"{PLUGIN}/{name}")
    for name in PLAYER_GUIDES:
        if (ENGINE / "docs" / name).is_file():
            z.write(ENGINE / "docs" / name, f"{PLUGIN}/docs/{name}")
    for p in sorted((ENGINE / "docs" / "screenshots").glob("*")):   # the README's pictures, so it reads well offline
        if p.is_file():
            z.write(p, f"{PLUGIN}/docs/screenshots/{p.name}")
    for p in sorted((ENGINE / "licenses").glob("*")):
        z.write(p, f"{PLUGIN}/licenses/{p.name}")
    z.writestr("Play TESL Everlasting.cmd", LAUNCHER)


def bepinex_files(z: zipfile.ZipFile) -> None:
    core = GAME / "BepInEx" / "core"
    probe = (core / "BepInEx.dll").read_bytes()
    if b"5.4.23" not in probe and BEPINEX_VERSION.encode("utf-16-le") not in probe:
        print("warning: could not confirm BepInEx 5.4.23 in BepInEx.dll; check the version before publishing")
    for name in ("winhttp.dll", "doorstop_config.ini"):
        add(z, GAME / name, name)
    for p in sorted(core.glob("*")):
        if p.is_file():
            z.write(p, f"BepInEx/core/{p.name}")


def source_files(z: zipfile.ZipFile) -> int:
    seen, n = set(), 0
    for g in SOURCE_GLOBS:
        for p in sorted(ENGINE.glob(g)):
            rel = p.relative_to(ENGINE).as_posix()
            if p.is_file() and rel not in seen and not SOURCE_SKIP.search(rel):
                seen.add(rel); z.write(p, f"tesl-everlasting/{rel}"); n += 1
    return n


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--names", default="", help="extra private words for the privacy scan (not stored)")
    a = ap.parse_args()
    v = version()
    out = Path(a.out) / f"TESL-Everlasting-{v}"
    out.mkdir(parents=True, exist_ok=True)
    made = []
    for suffix, extra in (("", None), ("-with-BepInEx", bepinex_files)):
        target = out / f"TESL-Everlasting-{v}{suffix}.zip"
        with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as z:
            player_files(z)
            if extra:
                extra(z)
        made.append(target)
    target = out / f"TESL-Everlasting-{v}-source.zip"
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as z:
        count = source_files(z)
    made.append(target)
    sums = [f"{hashlib.sha256(p.read_bytes()).hexdigest()}  {p.name}" for p in made]
    (out / "SHA256SUMS.txt").write_text("\n".join(sums) + "\n", encoding="utf-8")
    for p in made:
        print(f"{p.name}: {p.stat().st_size // 1024} KB")
    print(f"source files: {count}")
    sys.path.insert(0, str(Path(__file__).parent))
    import privacy_scan
    args = [str(p) for p in made] + (["--names", a.names] if a.names else [])
    return privacy_scan.main(args)


if __name__ == "__main__":
    sys.exit(main())
