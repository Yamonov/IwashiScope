#!/usr/bin/env python3
"""Generate the bundled Munsell renotation CSV from Colour 0.4.7."""

from __future__ import annotations

import csv
import hashlib
import re
import urllib.request
from pathlib import Path


SOURCE_URL = (
    "https://raw.githubusercontent.com/colour-science/colour/v0.4.7/"
    "colour/notation/datasets/munsell/all.py"
)
SOURCE_SHA256 = "fbf9a52ce4946ae01944bd89ec112b3f345ccaf037b1e5a4b564357732bb432b"
EXPECTED_ROW_COUNT = 4_995
OUTPUT_PATH = (
    Path(__file__).resolve().parents[1]
    / "IwashiScopePackage/Sources/IwashiScopeFeature/Resources/MunsellRenotationAll.csv"
)
ROW_PATTERN = re.compile(
    r'\(\("(?P<hue>[0-9.]+(?:BG|GY|YR|RP|PB|B|G|Y|R|P))",\s*'
    r'(?P<value>-?[0-9.]+),\s*(?P<chroma>-?[0-9.]+)\),\s*'
    r'\[(?P<x>-?[0-9.]+),\s*(?P<y>-?[0-9.]+),\s*(?P<Y>-?[0-9.]+)\]\),'
)


def main() -> None:
    source = urllib.request.urlopen(SOURCE_URL, timeout=30).read()
    actual_hash = hashlib.sha256(source).hexdigest()
    if actual_hash != SOURCE_SHA256:
        raise RuntimeError(
            f"Unexpected Colour dataset SHA-256: {actual_hash}"
        )

    text = source.decode("utf-8")
    rows = [match.groupdict() for match in ROW_PATTERN.finditer(text)]
    if len(rows) != EXPECTED_ROW_COUNT:
        raise RuntimeError(
            f"Expected {EXPECTED_ROW_COUNT} Munsell rows, found {len(rows)}"
        )

    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    with OUTPUT_PATH.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.writer(stream, lineterminator="\n")
        writer.writerow(("hue", "value", "chroma", "x", "y", "Y"))
        for row in rows:
            writer.writerow(
                (
                    row["hue"],
                    row["value"],
                    row["chroma"],
                    row["x"],
                    row["y"],
                    row["Y"],
                )
            )

    print(f"Generated {OUTPUT_PATH} ({len(rows)} rows)")


if __name__ == "__main__":
    main()
