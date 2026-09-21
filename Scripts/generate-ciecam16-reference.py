#!/usr/bin/env python3
"""Print reference vectors; validation only, never a runtime dependency.

Requires colour-science==0.4.7 (BSD-3-Clause) and NumPy. Uses its CIECAM16,
not its CAM16 implementation. See COLOR_APPEARANCE_VALIDATION.md.
"""
import json
import warnings

warnings.filterwarnings("ignore")
import colour
import numpy as np
from colour.appearance import ciecam16

assert colour.__version__ == "0.4.7", "Use the pinned reference version."
cases = [
    ("published-example", [19.01, 20, 21.78], [95.05, 100, 108.88], 318.31, 20, None),
    ("dark-response-extension", [0.0019, 0.002, 0.0021], [95.05, 100, 108.88], 20, 20, None),
    ("red", [41.24, 21.26, 1.93], [95.05, 100, 108.88], 20, 20, None),
    ("blue", [18.05, 7.22, 95.05], [95.05, 100, 108.88], 20, 20, None),
    ("d50-white", [96.42, 100, 82.49], [96.42, 100, 82.49], 20, 20, None),
    ("warm-white", [109.85, 100, 35.585], [109.85, 100, 35.585], 20, 20, None),
    ("upper-response-extension", [250, 200, 300], [95.05, 100, 108.88], 20, 20, None),
    ("no-adaptation", [20, 15, 4], [109.85, 100, 35.585], 20, 20, 0),
    ("half-adaptation", [20, 15, 4], [109.85, 100, 35.585], 20, 20, 0.5),
    ("full-adaptation", [20, 15, 4], [109.85, 100, 35.585], 20, 20, 1),
    ("dim-field", [12, 15, 22], [95.05, 100, 108.88], 1, 10, None),
]
original_degree = ciecam16.degree_of_adaptation
rows = []
for name, xyz, white, la, yb, override in cases:
    # Only the explicitly named D input is changed for the three override fixtures.
    ciecam16.degree_of_adaptation = (
        original_degree if override is None else lambda *_args: np.asarray(override)
    )
    result = colour.XYZ_to_CIECAM16(xyz, white, la, yb)
    expected = [float(getattr(result, key)) for key in ("J", "Q", "C", "M", "s", "h")]
    rows.append(dict(name=name, xyz=xyz, white=white, la=la, yb=yb, override=override, expected=expected))
ciecam16.degree_of_adaptation = original_degree
print(json.dumps(rows, indent=2, allow_nan=False))
