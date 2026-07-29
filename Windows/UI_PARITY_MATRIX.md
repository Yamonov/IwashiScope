# macOS v0.9.4 UI parity matrix

Baseline: public tag `v0.9.4`, commit
`343405721f1d1a5184efd32c24054259845d608c`.

This matrix records what the normal Windows UI is allowed to show. The source
order is significant. A value being present in the model does not authorize a
new card, row, tab, badge, or developer control.

User-observed UI overrides source-only code:

- Adobe RGB is not shown by the macOS build in use. Windows therefore hides
  Adobe RGB in every mode, even though `MeasurementDetailsView.swift` contains
  dormant/conditional Adobe RGB code.
- Fake process selection, instrument index editing, protocol probes, and
  explanatory development badges are test mechanisms and are not normal UI.

## Application states

| State | Visible order and relationship | Default |
| --- | --- | --- |
| No mode selected | Centered IwashiScope identity, “Spectral & Color Measurement”, “分光・測色ツール”, mode-selection title/detail, three mode cards in reflectance/ambient/emissive order, spotread availability | This is the startup screen. spotread is not launched yet. |
| Mode selected | Center analysis and bottom history; fixed right control/details pane; “モード選択へ戻る” in the navigation area | Right tab is “測定値”; lighting rendering tab is “CRI”; practical range, D50, and D65 are off. |
| Restored workspace | Same workspace layout and restored mode/right tab/selection; status explains saved-data browsing and offers instrument connection | spotread remains stopped until the user connects. |
| Busy | Workspace disabled under one centered busy overlay with the macOS phase title/detail and force-restart alternative | Only launching/calibrating/measuring/recovering. |

The title mark is the real IwashiScope app icon by explicit user override, not
the `waveform.path.ecg.rectangle` SF Symbol. The same Mac artwork supplies the
Windows EXE/window icon. See `ICON_ASSET_REPORT.md`.

The public reference has no bitmap files for the SF Symbols used by the mode
cards and other controls. Windows does not synthesize Unicode or Windows-stock
substitutes for those symbols. The app icon is not reused as a mode icon.

## Central analysis, per mode

| Order | Reflectance | Ambient | Emissive | Source condition |
| --- | --- | --- | --- | --- |
| 1 | Spectrum | Spectrum | Spectrum | Always |
| 1a | No D50/D65 controls | D50/D65 controls | D50/D65 controls | `mode != .reflectance` |
| 1b | Peak badge, hover point/callout/triangle, endpoint nm | Same | Same | Spectrum data exists |
| 1c | Practical/full displayed range | Same | Same | Right metadata toggle; default off |
| 2 | — | CRI/TM-30 tabs | CRI/TM-30 tabs | `mode != .reflectance`; CRI selected initially |
| 2a | — | CRI R1–R15, Ra R1–R8 bracket | Same | CRI tab |
| 2b | — | TM-30 16-bin vector, CCT/Duv, legend, Rf/Rg, fixed-range Rf–Rg plot, 99 CES chart | Same | TM-30 tab |

The central analysis is one vertical scroll area. CRI and TM-30 are not
independent top-level Windows tabs, and standards evaluation is not a central
top-level tab.

## Right pane measurement values, per mode

Rows/groups retain the following macOS order. Data-optional rows appear only
when their corresponding value exists.

| Order | Reflectance | Ambient | Emissive | Source condition |
| --- | --- | --- | --- | --- |
| 1 | Measurement result header/time | Same | Same | Measurement exists |
| 2 | “測色値”: XYZ, white-point Lab | Same | Same | Corresponding values exist |
| 2a | HEX (sRGB) + independent sRGB gamut warning | — | HEX (sRGB) + warning | User-visible macOS behavior; Adobe RGB is forbidden in all modes |
| 2b | Monochrome Y/L* | Same | Same | Monochrome data exists |
| 3 | Lighting information only if protocol supplied lighting values | Lux/CCT/Duv/EV, closest Planckian/daylight and metric issues | Same | `hasLightingMetrics` |
| 4 | — | JSPST-1998 numeric evaluation | JSPST-1998 light-source numeric evaluation | `mode != .reflectance` |
| 5 | — | ISO 3664:2025 measurable numeric criteria | Same | `mode != .reflectance` |
| 6 | CRI/TLCI summary only if supplied | Same | Same | CRI or TLCI exists |

TM-30 summary is not added to this right-side group. Peak is not added to this
right-side group. Adobe RGB is never added.

## Right pane shell

| Order | Content |
| --- | --- |
| 1 | Calibration/status group: large current mode, phase title/detail, notices, phase-specific controls |
| 2 | Instrument metadata: instrument and serial, wavelength range, data-point count, practical wavelength range, “実用エリアを使用する” |
| 3 | Tabs in this order: “測定値”, “spotread詳細ログ” |
| 4 | Mode-specific export footer |

The detailed log is a normal macOS tab and remains visible. Its controls are
status/mode/path, latest-follow toggle, and clear. Fake/helper configuration is
not part of this pane.

## History and export

| Area | Reflectance | Ambient / emissive |
| --- | --- | --- |
| History card | 110×110 swatch, editable name over swatch, Lab triplet | 110×110 spectrum thumbnail, divider, CRI thumbnail, editable bottom title |
| Selection | exclusive, Ctrl-toggle, Shift-range, Ctrl+Shift additive range, Ctrl+A, Ctrl+D, Delete | Same, mode-local |
| Reorder | selected cards retain presentation order; start/end/before/after/wrap; no cross-mode move | Same |
| External drag | selected swatches as one ASE plus normal exports | Spectrum/CRI/TM-30 PNG and CSV as available |
| Footer | collapsed/adaptive, click expand to 90%, splitter drag, keyboard-adjustable | Same |
| Export options | Swatch, 3,000 px spectrum PNG, spectrum CSV | Spectrum PNG, nested D50/D65 lines, CRI PNG, TM-30 PNG, combined CSV |

## TM-30 layout acceptance

- The TM-30 tab remains inside the central vertical scroll view.
- Every child is arranged from measured available bounds. The vector chart
  keeps a 1:1 aspect ratio; the other plots stretch only inside assigned
  rectangles.
- Vector chart, score summary, Rf–Rg plot, 99-sample chart, CCT/Duv overlay,
  and legend must be contained and clipped.
- Acceptance sizes: 860×620 minimum window, standard window, and enlarged
  window. Horizontal overflow is not accepted at any of these sizes.

Current evidence is in `UI_PARITY_DIFF.md` and
`artifacts/ui-parity/`. The mechanical visible-text scan is 56 PASS / 0 FAIL.
The minimum outer window is 860×620; the saved client capture is 847×613 after
the Windows non-client frame is excluded.

## Localization rule

Japanese source keys and English values come from the exact
`IwashiScope/Localizable.xcstrings` file in the baseline repository. Windows
does not invent bilingual slash labels or shortened explanatory copy in the
workspace.
