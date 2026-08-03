#!/usr/bin/env python3
"""Generate Illuminant C / CIE 1931 2-degree data used by Munsell conversion."""

from __future__ import annotations

import csv
import re
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
ILLUMINANT_PATH = REPOSITORY_ROOT / "Argyll_V3.5.0/ref/CIE_C.sp"
OBSERVER_PATH = REPOSITORY_ROOT / "Argyll_V3.5.0/xicc/xspect.c"
OUTPUT_PATH = (
    REPOSITORY_ROOT
    / "IwashiScopePackage/Sources/IwashiScopeFeature/Resources/"
    "MunsellColorimetryCIE1931.csv"
)

OBSERVER_BLOCK_PATTERN = re.compile(
    r"\{\s*471,\s*360\.0,\s*830\.0,.*?\{(.*?)\}\s*\}",
    re.DOTALL,
)
NUMBER_PATTERN = re.compile(r"[-+]?(?:\d+\.\d+|\d+)(?:[eE][-+]?\d+)?")


def load_illuminant_c() -> list[float]:
    text = ILLUMINANT_PATH.read_text(encoding="utf-8")
    match = re.search(r"BEGIN_DATA\s+(.*?)\s+END_DATA", text, re.DOTALL)
    if match is None:
        raise RuntimeError("Could not find Illuminant C data")
    values = [float(value) for value in match.group(1).split()]
    if len(values) != 93:
        raise RuntimeError(f"Expected 93 Illuminant C samples, found {len(values)}")
    return values


def load_cie_1931_observer() -> list[list[float]]:
    text = OBSERVER_PATH.read_text(encoding="utf-8")
    start = text.index("static xspect ob_CIE_1931_2[3]")
    end = text.index("/* Standard CIE 1964 10 degree */", start)
    blocks = OBSERVER_BLOCK_PATTERN.findall(text[start:end])
    channels = [
        [float(value) for value in NUMBER_PATTERN.findall(block)]
        for block in blocks
    ]
    if len(channels) != 3 or any(len(channel) != 471 for channel in channels):
        raise RuntimeError(
            "Expected three 471-sample CIE 1931 observer channels, found "
            f"{[len(channel) for channel in channels]}"
        )
    return channels


def interpolate_illuminant(values: list[float], wavelength: int) -> float:
    position = (wavelength - 320) / 5
    lower_index = int(position)
    fraction = position - lower_index
    if lower_index == len(values) - 1:
        return values[lower_index]
    return (
        values[lower_index] * (1 - fraction)
        + values[lower_index + 1] * fraction
    )


def main() -> None:
    illuminant = load_illuminant_c()
    x_bar, y_bar, z_bar = load_cie_1931_observer()

    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    with OUTPUT_PATH.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.writer(stream, lineterminator="\n")
        writer.writerow(("wavelength", "illuminantC", "xBar", "yBar", "zBar"))
        for wavelength in range(360, 781):
            observer_index = wavelength - 360
            writer.writerow(
                (
                    wavelength,
                    f"{interpolate_illuminant(illuminant, wavelength):.12g}",
                    f"{x_bar[observer_index]:.12g}",
                    f"{y_bar[observer_index]:.12g}",
                    f"{z_bar[observer_index]:.12g}",
                )
            )

    print(f"Generated {OUTPUT_PATH} (421 rows)")


if __name__ == "__main__":
    main()
