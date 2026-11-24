#!/usr/bin/env python3
"""Create a source-only review ZIP from a specific tracked Git tree.
Wallpaper media are omitted; application code, JSON, shaders and all build
metadata are preserved. Never publish this archive as an installer.
"""
import argparse
import hashlib
import io
import json
import pathlib
import subprocess
import tarfile
import zipfile

MEDIA = {".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".avif", ".tiff",
         ".mp4", ".webm", ".mov", ".mkv", ".avi", ".wmv", ".m4v", ".hdr", ".exr"}
PREFIXES = ("src/widgets/wallpaper/", "wallpapers/")

def omitted(path):
    low = path.lower()
    return low.startswith(PREFIXES) and pathlib.PurePosixPath(low).suffix in MEDIA

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True)
    parser.add_argument("--ref", default="HEAD")
    args = parser.parse_args()
    commit = subprocess.check_output(["git", "rev-parse", "--verify", args.ref + "^{commit}"], text=True).strip()
    raw = subprocess.check_output(["git", "archive", "--format=tar", commit])
    output = pathlib.Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    included, excluded = [], []
    with tarfile.open(fileobj=io.BytesIO(raw), mode="r:") as archive, zipfile.ZipFile(
        output, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6
    ) as result:
        for member in sorted(archive.getmembers(), key=lambda x: x.name):
            if not member.isfile():
                if member.isdir():
                    continue
                raise ValueError("Unexpected non-regular tracked file: " + member.name)
            path = member.name
            parts = pathlib.PurePosixPath(path).parts
            if not parts or path.startswith("/") or ".." in parts or path in ("SOURCE-MANIFEST.json", "SOURCE-ONLY-README.txt"):
                raise ValueError("Unexpected archive path: " + path)
            stream = archive.extractfile(member)
            if stream is None:
                raise ValueError("Missing tracked content: " + path)
            data = stream.read()
            record = {"path": path, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}
            if omitted(path):
                excluded.append(record)
            else:
                included.append(record)
                entry = zipfile.ZipInfo(path, (2026, 11, 24, 0, 0, 0))
                entry.compress_type = zipfile.ZIP_DEFLATED
                entry.external_attr = (member.mode & 0o777) << 16
                result.writestr(entry, data)
        required = {"Opsis.csproj", "Version.props", "global.json", "CLAUDE.md",
                    "docs/OPSIS-P0-P1-UNIFIED-CAPABILITIES.md",
                    "src/Widgets/wallpaper/scenes/capitals.json", "src/Widgets/wallpaper/scenes/islands.json"}
        if not required.issubset({x["path"] for x in included}):
            raise ValueError("Required source files missing.")
        manifest = {"schema": 1, "commit": commit, "targetVersion": "3.0.0.3200",
                    "release": False, "included": included, "omittedWallpaperMedia": excluded}
        result.writestr("SOURCE-MANIFEST.json", json.dumps(manifest, ensure_ascii=False, indent=2))
        result.writestr("SOURCE-ONLY-README.txt", "OPSIS 3.0.0.3200 RC source review only.\n"
            "Commit: " + commit + "\nWallpaper media intentionally omitted; restore them from the exact Git commit before visual testing or installer production.\n"
            "No .git history, compiled installer or claim of production readiness.\n"
            "See CLAUDE.md and the current readiness manifest.\n")
    with zipfile.ZipFile(output) as check:
        if check.testzip() is not None:
            raise ValueError("CRC verification failed.")
        for record in included:
            if hashlib.sha256(check.read(record["path"])).hexdigest() != record["sha256"]:
                raise ValueError("Hash mismatch: " + record["path"])
        if any(omitted(path) for path in check.namelist()):
            raise ValueError("Unexpected wallpaper media in review ZIP.")
    digest = hashlib.sha256(output.read_bytes()).hexdigest()
    output.with_suffix(output.suffix + ".sha256").write_text(digest + "  " + output.name + "\n", encoding="utf-8")
    print(f"PASS source-only ZIP: {len(included)} tracked files, {len(excluded)} omitted media files, commit={commit}, sha256={digest}")

if __name__ == "__main__":
    main()
