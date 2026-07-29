# IwashiScope for Windows

IwashiScope 0.9.5 for Windows is the .NET 10 + WPF port of the macOS
application in this repository. It uses the same modified ArgyllCMS 3.5.0
source and JSON Lines protocol version 3 as the macOS build.

The normal application does not contain fake measurement data or a fake
measurement process. A real `iwashiscope-spotread.exe`, built from the
top-level `Argyll_V3.5.0` source tree, must be placed beside
`IwashiScope.exe`.

## Requirements

- Windows 10 or later, x64
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
- `Scripts/Build-Release.ps1`: helper build, test, publish, and ZIP packaging
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
the common licenses and corresponding-source metadata, and creates
`IwashiScope-0.9.5-Windows-x64.zip`.

```powershell
.\Windows\Scripts\Build-Release.ps1 `
  -Version 0.9.5 `
  -JamPath C:\path\to\jam.exe `
  -OutputRoot C:\path\to\artifacts\release-0.9.5
```

The script refuses to overwrite an existing payload, ZIP, or manifest.
Generated executables, `bin`, `obj`, test results, release artifacts, user
settings, logs, and `.iwashiscope` workspace files are excluded from source
control.

After a helper binary has completed hardware qualification, an official
package can reuse that exact binary while still checking its recorded hash:

```powershell
.\Windows\Scripts\Build-Release.ps1 `
  -Version 0.9.5 `
  -JamPath C:\path\to\jam.exe `
  -VerifiedHelperPath C:\qualified\iwashiscope-spotread.exe `
  -ExpectedHelperSha256 D214B5F884349E6766325EA32F075C2C87745FD801C85DACB6BECA60FB282816 `
  -OutputRoot C:\path\to\artifacts\release-0.9.5
```

Without `-VerifiedHelperPath`, the script builds and tests a fresh helper from
the common source tree. With it, the script packages only the supplied,
hash-matched binary; this avoids silently replacing a hardware-qualified
release helper with a newly linked native executable.

## Version and signing

The Windows product, assembly, file, and informational versions are defined in
`Directory.Build.props`. Release 0.9.5 uses `0.9.5` and `0.9.5.0`.

The release process does not create or trust a certificate automatically.
Official Authenticode signing requires a separately provisioned, trusted
code-signing certificate and an approved signing procedure. An unsigned build
is identified as such in `RELEASE_MANIFEST.txt`.

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
