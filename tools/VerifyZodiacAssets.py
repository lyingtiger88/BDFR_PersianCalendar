#!/usr/bin/env python3
"""Verify that every packaged zodiac PNG is byte-identical to the tracked source."""
from __future__ import annotations

import argparse
import hashlib
import sys
from pathlib import Path

EXPECTED_COUNT = 120


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def png_dimensions(path: Path) -> tuple[int, int]:
    with path.open("rb") as stream:
        header = stream.read(24)
    if len(header) != 24 or header[:8] != b"\x89PNG\r\n\x1a\n" or header[12:16] != b"IHDR":
        raise ValueError(f"Invalid PNG file: {path}")
    return int.from_bytes(header[16:20], "big"), int.from_bytes(header[20:24], "big")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()

    source_files = {p.name: p for p in args.source.glob("*.png") if p.is_file()}
    output_files = {p.name: p for p in args.output.glob("*.png") if p.is_file()}

    if len(source_files) != EXPECTED_COUNT:
        print(f"ERROR: expected {EXPECTED_COUNT} source PNGs, found {len(source_files)}", file=sys.stderr)
        return 2

    missing = sorted(set(source_files) - set(output_files))
    extra = sorted(set(output_files) - set(source_files))
    mismatched = []
    for name in sorted(set(source_files) & set(output_files)):
        src, dst = source_files[name], output_files[name]
        if sha256(src) != sha256(dst):
            mismatched.append((name, src.stat().st_size, dst.stat().st_size,
                               png_dimensions(src), png_dimensions(dst)))

    if missing or extra or mismatched:
        print("Zodiac asset integrity check FAILED")
        for name in missing:
            print(f"MISSING: {name}")
        for name in extra:
            print(f"EXTRA: {name}")
        for name, src_size, dst_size, src_dim, dst_dim in mismatched:
            print(f"HASH MISMATCH: {name}: source={src_size} bytes {src_dim}, output={dst_size} bytes {dst_dim}")
        return 1

    print(f"PASS: all {len(source_files)} zodiac PNGs match byte-for-byte (SHA-256).")
    for name in ("cute_1_virgo.png", "cute_2_virgo.png", "cute_3_virgo.png"):
        path = source_files.get(name)
        if path:
            print(f"{name}: {path.stat().st_size} bytes, {png_dimensions(path)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
