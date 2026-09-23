# Windows display color management

## Supported reference condition

Compare the reflection sample under D50 illumination with a calibrated, profiled
display. The ICC must describe the actual display preset, white point, luminance,
TRCs and gamut. Match display white and white luminance to the observation setup.
Avoid Night light, adaptive brightness, vendor color enhancement and direct light
on the screen. The application never changes these settings, ICC associations or
calibration curves. It cannot determine whether calibration is still valid.

## Color path

Measured D50 Lab → Little CMS 2.19.1 ICC transform → containing-monitor device RGB
→ WPF numeric pixel transport. No clipped sRGB/Display P3 intermediate is used for
measured patches, history patches, appearance-comparison patches or xy markers.
D65 Lab (where supplied by a measurement mode) is Bradford-adapted once to D50
before the ICC transform. CIE2015 xy_F coordinates are not used as CIE1931 XYZ.

Transforms use relative colorimetric intent, with black-point compensation off.
Float/double values are retained until output; only the target device codes are
clipped. This is white-relative display matching, not absolute luminance matching.
The display must have an appropriate white/luminance calibration. ICC v2/v4
matrix/TRC and LUT profiles are supported by the CMM; arbitrary profiles are not
reduced to a primary matrix. A profile is not assumed to be a calibration proof.

Lab-plane colors go directly through the ICC transform. Spectrum/CRI/TM-30 chart
illustration colors and the already approved xy background/confusion palette are
authored in sRGB and converted to the display. Their underlying scientific data,
geometries and relative-brightness models are unchanged. The xy illustration is
still reference coloring, not a spectral characterization of the actual display.

## Monitor and OS state

`MonitorFromWindow` selects the monitor containing the largest area of the window.
Use one monitor for a color-critical comparison; a window spanning differently
profiled monitors cannot be corrected for both with this single WPF surface.
`GetICMProfileW` on that monitor's new DC obtains the effective application profile.
Windows may supply a synthetic compatibility profile with Advanced Color.
Never substitute the native/EXTENDED profile behind Windows' back: doing so would
double-convert ordinary WPF output. DisplayConfig reports SDR/WCG/HDR (or unknown).

The displayed status and tooltip report the profile, monitor and mode. Polling
every two seconds, window movement, activation and display/DPI/settings messages
refresh the profile. Content hashes also detect replacement under the same name.
The watcher's lifetime is that of the window; no profile or transform is global.

For the baseline matching workflow, use SDR with the measured display profile.
With ACM/HDR, Windows may expose only sRGB unless the executable's **Use legacy
display ICC color management** compatibility option is enabled. The application
does not enable this option or disable ACM/HDR. The effective profile is respected;
wide gamut is not claimed when Windows only exposes sRGB. No profile/unusable ICC
results in explicitly labeled sRGB reference display, not an unreported native-RGB
fallback. HDR absolute-luminance matching and an FP16 DXGI presentation path are
not implemented by this change.

## Data and exports

Measurements, history/workspace values, Lab/XYZ, CAM16/Bradford calculations,
reported sRGB/Adobe RGB/Display P3 encodings, ASE and PNG/CSV exports are unchanged.
Exporters never read the per-window display context. A screenshot is not a portable
colorimetric export; use the regular export function.

## Validation boundary

Tests use synthetic ICC v2/v4 matrix/TRC and LUT profiles, an in-display-gamut color
outside sRGB, profile changes, WPF numeric transport, alpha preservation, and export
isolation. Offscreen tests verify software behavior, not a physical display's color.

For physical acceptance, display representative patches, measure them in emission
mode, and compare them with the target in a common white-relative D50 Lab convention.
Set reference white consistently before evaluating ΔE00. Evaluate in-gamut colors
separately from colors the display cannot reproduce. No universal ΔE guarantee is
made: profile quality, display uniformity, instrument error, observer metamerism,
fluorescence, gloss and viewing conditions remain relevant.

## Build and sources

MSVC x64 and a Windows SDK with the Advanced Color INFO_2 declarations are required.
MSBuild compiles the vendored MIT-licensed Little CMS sources and the read-only
display query into `IwashiScope.DisplayColor.dll`. Native files rebuild when their
inputs change. The runtime dependency and license are copied into test, build and
publish outputs. No build-time network download is required.

Primary references:
- https://learn.microsoft.com/en-us/windows/win32/wcs/advanced-color-icc-profiles
- https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-geticmprofilew
- https://github.com/mm2/Little-CMS/releases/tag/lcms2.19.1
- https://www.color.org/displaycalibration/
