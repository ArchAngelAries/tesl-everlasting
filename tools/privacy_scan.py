"""Privacy scan for release packages: finds personal data in every file of a folder or .zip (text and binaries, read as
ASCII/UTF-8 and UTF-16). Exit code 1 when anything is found.

Allowed on purpose: the owner's public handle ArchAngelAries and the support links (Ko-fi, Patreon).
Usage:  python privacy_scan.py <folder-or-zip> [...]   (add --names "Name1,Name2" to search for more private words)
"""
from __future__ import annotations

import io
import re
import sys
import zipfile
from pathlib import Path

# Private words: the owner's account and real names are passed with --names (never written in a released file).
PATTERNS = [
    ("Windows user folder", re.compile(rb"[A-Za-z]:[\\/]+Users[\\/]+(?!Public\b|Default\b|<|%|\{|you\b|YourName\b|USERNAME\b)[A-Za-z0-9._ -]+", re.I)),
    ("local drive path", re.compile(rb"\b[D-Zd-z]:[\\/]+(?:Gaming|Games|Steam\d|SteamLibrary)[\\/]", re.I)),
    ("e-mail address", re.compile(rb"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")),
    ("Steam account id", re.compile(rb"\b7656119\d{10}\b")),
    ("debug symbol path", re.compile(rb"[A-Za-z]:[\\/][^\x00\r\n\"<>|]{0,200}\.pdb", re.I)),
    ("home folder", re.compile(rb"/(?:home|Users)/(?!runner\b|<)[A-Za-z0-9._-]+/", re.I)),
]
ALLOWED_EMAILS = re.compile(rb"(noreply@|no-reply@|@users\.noreply\.github\.com|@example\.(com|org)|\.invalid$|git@github\.com|^sales@fmod\.com$)", re.I)   # FMOD's public contact in the third-party notice
TEXT_EXT = {".md", ".txt", ".json", ".tsv", ".csv", ".cs", ".py", ".js", ".html", ".css", ".cmd", ".ps1", ".ini", ".cfg", ".xml", ".svg", ".toml", ".yml", ".yaml"}


def _variants(data: bytes):
    yield data
    try:
        yield data.decode("utf-16-le", errors="ignore").encode("utf-8", errors="ignore")
    except Exception:
        pass


def scan_bytes(name: str, data: bytes, extra: list[bytes]) -> list[str]:
    found = []
    binary_image = name.lower().endswith((".png", ".jpg", ".jpeg", ".gif", ".webp", ".ico", ".ogg", ".wav", ".mp3"))
    for blob in _variants(data):
        for label, rx in PATTERNS:
            for m in rx.finditer(blob):
                hit = m.group(0)
                if label == "e-mail address" and (ALLOWED_EMAILS.search(hit) or binary_image):
                    continue
                if label == "debug symbol path" and not re.search(rb"Users|Gaming|Steam\d", hit, re.I):
                    continue   # build paths inside third-party DLLs (BepInEx, Mono.Cecil, MonoMod) are their builders', not ours
                found.append(f"{name}: {label}: {hit[:80].decode('utf-8', 'replace')}")
        low = blob.lower()
        for word in extra:
            i = low.find(word.lower())
            while i >= 0:
                # whole-word only, so a public handle that merely contains a private word is not a hit
                before = low[i - 1:i] if i else b" "
                after = low[i + len(word):i + len(word) + 1]
                if not before.isalnum() and not after.isalnum():
                    found.append(f"{name}: private word: {blob[max(0, i - 30):i + len(word) + 30].decode('utf-8', 'replace')!r}")
                i = low.find(word.lower(), i + 1)
    return sorted(set(found))


def scan(path: Path, extra: list[bytes]) -> list[str]:
    out = []
    if path.suffix.lower() == ".zip":
        with zipfile.ZipFile(path) as z:
            for info in z.infolist():
                if not info.is_dir():
                    out += scan_bytes(f"{path.name}!{info.filename}", z.read(info), extra)
                    if info.filename.lower().endswith(".zip"):
                        inner = zipfile.ZipFile(io.BytesIO(z.read(info)))
                        for i2 in inner.infolist():
                            out += scan_bytes(f"{path.name}!{info.filename}!{i2.filename}", inner.read(i2), extra)
    elif path.is_dir():
        for p in sorted(path.rglob("*")):
            if p.is_file():
                out += scan_bytes(str(p.relative_to(path)), p.read_bytes(), extra)
    else:
        out += scan_bytes(path.name, path.read_bytes(), extra)
    return out


def main(argv: list[str]) -> int:
    extra: list[bytes] = []
    paths = []
    it = iter(argv)
    for a in it:
        if a == "--names":
            extra += [w.strip().encode() for w in next(it).split(",") if w.strip()]
        else:
            paths.append(Path(a))
    problems = []
    for p in paths:
        problems += scan(p, extra)
    for line in problems:
        print(line)
    print(f"privacy scan: {len(problems)} finding(s) in {len(paths)} item(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
