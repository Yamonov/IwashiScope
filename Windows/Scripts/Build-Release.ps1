[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version = '0.9.5',

    [Parameter(Mandatory = $true)]
    [string] $JamPath,

    [string] $VsDevCmdPath,

    [string] $VerifiedHelperPath,

    [string] $ExpectedHelperSha256,

    [string] $OutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-ExistingFile {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description was not found: $Path"
    }

    return (Resolve-Path -LiteralPath $Path).Path
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,

        [Parameter(Mandatory = $true)]
        [string[]] $ArgumentList
    )

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

$windowsRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $windowsRoot '..'))
$solutionPath = Join-Path $windowsRoot 'IwashiScope.Windows.slnx'
$appProjectPath = Join-Path $windowsRoot `
    'src\IwashiScope.App.Wpf\IwashiScope.App.Wpf.csproj'
$helperBuildScript = Join-Path $repositoryRoot `
    'Scripts\build-spotread-windows.ps1'

$JamPath = Resolve-ExistingFile -Path $JamPath -Description 'Jam'
$helperBuildScript = Resolve-ExistingFile `
    -Path $helperBuildScript `
    -Description 'Common Windows helper build script'

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $windowsRoot "artifacts\release-$Version"
}
$OutputRoot = $ExecutionContext.SessionState.Path.
    GetUnresolvedProviderPathFromPSPath(
        $OutputRoot
    )

$payloadName = "IwashiScope-$Version-Windows-x64"
$payloadPath = Join-Path $OutputRoot $payloadName
$zipPath = Join-Path $OutputRoot "$payloadName.zip"
$manifestPath = Join-Path $OutputRoot 'RELEASE_MANIFEST.txt'

foreach ($reservedPath in @($payloadPath, $zipPath, $manifestPath)) {
    if (Test-Path -LiteralPath $reservedPath) {
        throw "Release output already exists; preserving it: $reservedPath"
    }
}

$declaredVersion = Select-String `
    -LiteralPath (Join-Path $windowsRoot 'Directory.Build.props') `
    -Pattern '<Version>([^<]+)</Version>' |
    Select-Object -First 1
if ($null -eq $declaredVersion -or
    $declaredVersion.Matches[0].Groups[1].Value -ne $Version) {
    throw "Requested version $Version does not match Windows/Directory.Build.props."
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) `
    ("IwashiScope-release-" + [Guid]::NewGuid().ToString('N'))
$temporaryHelper = Join-Path $temporaryRoot 'iwashiscope-spotread.exe'
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null

try {
    if ([string]::IsNullOrWhiteSpace($VerifiedHelperPath)) {
        $helperArguments = @{
            JamPath = $JamPath
            Architecture = 'x64'
            OutputPath = $temporaryHelper
        }
        if (-not [string]::IsNullOrWhiteSpace($VsDevCmdPath)) {
            $helperArguments.VsDevCmdPath = $VsDevCmdPath
        }
        & $helperBuildScript @helperArguments

        $packagedHelper = Resolve-ExistingFile `
            -Path $temporaryHelper `
            -Description 'Built iwashiscope-spotread.exe'
        $helperOrigin = 'built from the repository source by this release run'
    }
    else {
        $packagedHelper = Resolve-ExistingFile `
            -Path $VerifiedHelperPath `
            -Description 'Hardware-qualified iwashiscope-spotread.exe'
        $helperOrigin = 'prebuilt hardware-qualified helper supplied to the release run'
    }

    $helperHash = (Get-FileHash -LiteralPath $packagedHelper `
        -Algorithm SHA256).Hash
    if (-not [string]::IsNullOrWhiteSpace($ExpectedHelperSha256) -and
        $helperHash -ne $ExpectedHelperSha256.ToUpperInvariant()) {
        throw "Helper SHA-256 mismatch. Expected $ExpectedHelperSha256, got $helperHash."
    }

    Invoke-CheckedCommand -FilePath 'dotnet' -ArgumentList @(
        'build',
        $solutionPath,
        '-c', 'Release',
        '--nologo'
    )
    Invoke-CheckedCommand -FilePath 'dotnet' -ArgumentList @(
        'test',
        $solutionPath,
        '-c', 'Release',
        '--no-build',
        '--nologo',
        '--logger', 'console;verbosity=minimal'
    )

    New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
    Invoke-CheckedCommand -FilePath 'dotnet' -ArgumentList @(
        'publish',
        $appProjectPath,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '--nologo',
        '-p:PublishSingleFile=false',
        "-p:Version=$Version",
        "-p:IwashiScopeHelperPath=$packagedHelper",
        '-o', $payloadPath
    )

    $publishedExecutable = Resolve-ExistingFile `
        -Path (Join-Path $payloadPath 'IwashiScope.exe') `
        -Description 'Published IwashiScope.exe'
    $publishedHelper = Resolve-ExistingFile `
        -Path (Join-Path $payloadPath 'iwashiscope-spotread.exe') `
        -Description 'Published iwashiscope-spotread.exe'
    $publishedHelperHash = (Get-FileHash -LiteralPath $publishedHelper `
        -Algorithm SHA256).Hash
    if ($publishedHelperHash -ne $helperHash) {
        throw 'Published helper hash does not match the helper built from common source.'
    }

    $argyllLicenseDirectory = Join-Path $payloadPath 'LICENSES\ArgyllCMS'
    $dotnetLicenseDirectory = Join-Path $payloadPath 'LICENSES\dotnet'
    New-Item -ItemType Directory -Path $argyllLicenseDirectory -Force |
        Out-Null
    New-Item -ItemType Directory -Path $dotnetLicenseDirectory -Force |
        Out-Null
    foreach ($licenseName in @('License.txt', 'License2.txt', 'License3.txt')) {
        Copy-Item -LiteralPath (
            Join-Path $repositoryRoot "Argyll_V3.5.0\$licenseName"
        ) -Destination (
            Join-Path $argyllLicenseDirectory $licenseName
        )
    }
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'ARGYLL_CHANGES.md') `
        -Destination (Join-Path $argyllLicenseDirectory 'ARGYLL_CHANGES.md')

    $dotnetExecutable = Resolve-ExistingFile `
        -Path (Get-Command dotnet).Source `
        -Description 'dotnet executable'
    $dotnetRoot = Split-Path -Parent $dotnetExecutable
    Copy-Item -LiteralPath (Join-Path $dotnetRoot 'LICENSE.txt') `
        -Destination (Join-Path $dotnetLicenseDirectory 'LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $dotnetRoot 'ThirdPartyNotices.txt') `
        -Destination (
            Join-Path $dotnetLicenseDirectory 'ThirdPartyNotices.txt'
        )
    Copy-Item -LiteralPath (Join-Path $windowsRoot 'README.md') `
        -Destination (Join-Path $payloadPath 'README-Windows.md')

    $sourceCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceCommit)) {
        throw 'Unable to determine the repository source commit.'
    }
    $sourceInfo = @(
        'IwashiScope corresponding source'
        'Repository: https://github.com/Yamonov/IwashiScope'
        "Source commit: $sourceCommit"
        "Version: $Version"
        'Helper source: Argyll_V3.5.0 (same repository commit)'
        "Helper packaging origin: $helperOrigin"
        "Helper SHA-256: $publishedHelperHash"
    )
    [IO.File]::WriteAllLines(
        (Join-Path $payloadPath 'SOURCE_INFO.txt'),
        $sourceInfo,
        [Text.UTF8Encoding]::new($false)
    )

    Compress-Archive -LiteralPath $payloadPath -DestinationPath $zipPath
    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    $executableInfo = (Get-Item -LiteralPath $publishedExecutable).VersionInfo
    $signature = Get-AuthenticodeSignature -LiteralPath $publishedExecutable

    $manifest = @(
        "Artifact: $zipPath"
        "Artifact SHA-256: $zipHash"
        "Application version: $($executableInfo.ProductVersion)"
        "Application file version: $($executableInfo.FileVersion)"
        "Application Authenticode: $($signature.Status)"
        "Helper packaging origin: $helperOrigin"
        "Helper SHA-256: $publishedHelperHash"
        "Source commit: $sourceCommit"
        "Runtime: self-contained win-x64"
    )
    [IO.File]::WriteAllLines(
        $manifestPath,
        $manifest,
        [Text.UTF8Encoding]::new($false)
    )

    Write-Host "Created release payload: $payloadPath"
    Write-Host "Created release archive: $zipPath"
    Write-Host "Archive SHA-256: $zipHash"
    Write-Host "Helper SHA-256: $publishedHelperHash"
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
        $resolvedSystemTemporaryRoot = [IO.Path]::GetFullPath(
            [IO.Path]::GetTempPath()
        )
        if (-not $resolvedTemporaryRoot.StartsWith(
            $resolvedSystemTemporaryRoot,
            [StringComparison]::OrdinalIgnoreCase
        )) {
            throw "Refusing to remove unexpected temporary path: $temporaryRoot"
        }
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
