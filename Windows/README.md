# IwashiScope for Windows

IwashiScope 1.0 for Windows is the .NET 10 + WPF port of the macOS
application in this repository. It uses the same modified ArgyllCMS 3.5.0
source and JSON Lines protocol version 3 as the macOS build.

The normal application does not contain fake measurement data or a fake
measurement process. A real `iwashiscope-spotread.exe`, built from the
top-level `Argyll_V3.5.0` source tree, must be placed beside
`IwashiScope.exe`.

## Requirements

- Windows 11 x64、または.NET 10が対応するWindows 10 LTSC/Enterprise x64
- .NET 10 SDK for source builds
- Visual Studio 2026 or Build Tools with the MSVC x64 toolchain
- Windows SDK
- Jam compatible with the top-level ArgyllCMS build
- An ArgyllCMS-compatible USB driver for instruments that require it

The self-contained release ZIP includes the .NET runtime and does not require
a separate .NET installation on the target computer.

## Source layout

- `IwashiScope.Windows.slnx`: Windows solution
- `src/IwashiScope.App.Wpf`: WPF application and Windows resources
- `src/IwashiScope.Core`: calculations, history, workspace, and exporters
- `src/IwashiScope.Protocol`: JSON Lines version 3 parser
- `src/IwashiScope.Infrastructure.Windows`: process, pipe, storage, and logging
- `tests/IwashiScope.Tests`: automated tests
- `Scripts/Build-Release.ps1`: helper build, test, publish, portable ZIP, and
  Windows installer packaging
- `Scripts/Build-WindowsInstaller.ps1`: creates the per-user installer from an
  already-published payload
- `Scripts/New-WindowsAppcast.ps1`: creates a signed Windows x64 appcast after
  the installer has been uploaded
- `tools/Generate-WindowsIcon.ps1`: regenerates Windows icons from the
  top-level macOS AppIcon assets

The common helper source is not duplicated under `Windows/`. It remains at
`../Argyll_V3.5.0`, and is built by `../Scripts/build-spotread-windows.ps1`.

## Build and test

From the repository root:

```powershell
dotnet build .\Windows\IwashiScope.Windows.slnx -c Release
dotnet test .\Windows\IwashiScope.Windows.slnx -c Release --no-build
```

Development builds locate a helper through `IWASHISCOPE_SPOTREAD` or an
app-local `iwashiscope-spotread.exe`. Release publishing deliberately fails
unless the helper path is supplied, which prevents a package without the
measurement backend.

## Reproducible Windows release

The release script builds and runs the common C helper test, builds and tests
the WPF solution, publishes a self-contained win-x64 application, includes
the common licenses and corresponding-source metadata, and creates both
`IwashiScope-1.0-Windows-x64.zip` and the directly executable
`IwashiScope-1.0-Windows-x64-Setup.exe`.

```powershell
.\Windows\Scripts\Build-Release.ps1 `
  -Version 1.0 `
  -JamPath C:\path\to\jam.exe `
  -OutputRoot C:\path\to\artifacts\release-1.0
```

The installer follows the established Scripta for Windows pattern: it installs
without elevation for the current user under
`%LOCALAPPDATA%\Programs\IwashiScope`, creates a Start Menu shortcut and HKCU
uninstall entry, and leaves settings and `.iwashiscope` files outside the
installation folder untouched. The release script refuses to overwrite an
existing payload, ZIP, installer, or manifest.
Unlike the earlier Scripta implementation, the IwashiScope installer validates
every ZIP entry, rejects traversal, absolute/alternate-stream, reserved-name,
duplicate, and link entries, stages beside the destination, and swaps the
application directory transactionally. A failed shortcut, registry, launch, or
file replacement restores the previous application and metadata. The uninstall
cleanup validates the exact per-user path and deletes its temporary executable
without elevation after the installed process exits.

The installer security and isolated update/uninstall tests can be run without
changing the actual user installation:

```powershell
.\Windows\Scripts\Test-WindowsInstaller.ps1
```

Generated executables, `bin`, `obj`, test results, release artifacts, user
settings, logs, and `.iwashiscope` workspace files are excluded from source
control.

## Application updates

The Windows app integrates WinSparkle 0.9.4 from the fixed NuGet package and
ships its x64 native DLL app-local. Native loading is restricted to the
application directory. It uses the Windows-only HTTPS feed at
`https://yamonov.github.io/IwashiScope/appcast-windows.xml` and the same EdDSA
public key as the macOS Sparkle integration. The Help menu contains
`アップデートを確認…` / `Check for Updates…`; WinSparkle also manages its
per-user automatic-check consent and schedule under HKCU.

WinSparkle accepts only correctly EdDSA-signed payloads. The feed must contain
only a Windows x64 installer enclosure with `sparkle:os="windows-x64"`. The
portable ZIP is not an installer and must not be added to this feed. Sign the
installer on the Mac release host using Sparkle's existing `sign_update` and
the production key already held there. The IwashiScope production private key
must never be copied to, requested by, or stored on Windows. Transfer only the
detached signature back to Windows, then generate a checked feed file:

```sh
/path/to/Sparkle/bin/sign_update \
  IwashiScope-1.0-Windows-x64-Setup.exe
```

```powershell
.\Windows\Scripts\New-WindowsAppcast.ps1 `
  -Version 1.0 `
  -TagName v1.0 `
  -InstallerPath .\Windows\artifacts\release-1.0\IwashiScope-1.0-Windows-x64-Setup.exe `
  -EdSignature SIGNATURE_FROM_MAC
```

`New-WindowsAppcast.ps1` refuses a wrong filename or version, verifies the
detached signature with the production public key, preserves older items, and
checks the exact byte length, HTTPS URL, and `windows-x64` platform before it
writes a new file. `Test-WindowsAppcast.ps1` performs the same validation on an
existing feed. Upload the installer first, verify its URL and signature, and only then replace
`docs/appcast-windows.xml` with the generated file and publish GitHub Pages.
The signing private key is never stored in this repository or on the Windows
release host. The tracked feed intentionally contains no publishable item until
the production signature and public release URL exist.

The signing path can be tested on Windows without production material. This
command generates a random temporary key, signs and verifies a temporary copy,
checks a one-item and history-preserving appcast, and removes the key and all
temporary signed artifacts:

```powershell
.\Windows\Scripts\Test-WinSparkleSigning.ps1 `
  -InstallerPath .\Windows\artifacts\release-1.0\IwashiScope-1.0-Windows-x64-Setup.exe
```

The macOS and Windows feeds are separate files in the same repository. This
allows either platform to release first and to use a different version number.
A single shared feed is technically possible by marking each separate item as
`sparkle:os="macos"` or `sparkle:os="windows-x64"`, but separate feeds are the
layout recommended by the Sparkle project and prevent one platform's generator
from overwriting the other platform's release history.

## Version and signing

The Windows product, assembly, file, and informational versions are defined in
`Directory.Build.props`. Release 1.0 uses `1.0` for the product and
informational versions, and `1.0.0.0` for file and assembly versions.

The release process does not create or trust a certificate automatically.
Official Authenticode signing requires a separately provisioned, trusted
code-signing certificate and an approved signing procedure. An unsigned build
is identified as such in `RELEASE_MANIFEST.txt`. EdDSA update signing and
Authenticode signing are separate: a valid WinSparkle signature does not make
the installer Authenticode-signed.

## Licenses and corresponding source

IwashiScope is distributed under `AGPL-3.0-only`. The authoritative license,
notices, ArgyllCMS change record, third-party license texts, and CIE source
data are stored at the repository root:

- `../LICENSE`
- `../NOTICE`
- `../THIRD_PARTY_NOTICES.md`
- `../ARGYLL_CHANGES.md`
- `../LICENSES/`
- `../ThirdParty/`

The release ZIP includes these notices, the ArgyllCMS license texts, the .NET
runtime license and third-party notices, and a `SOURCE_INFO.txt` file pointing
to the exact repository commit.
