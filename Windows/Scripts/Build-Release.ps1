[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:\.\d+)?$')]
    [string] $Version = '0.9.6',

    [Parameter(Mandatory = $true)]
    [string] $JamPath,

    [string] $VsDevCmdPath,

    [string] $OutputRoot,

    [string] $CertificateThumbprint,

    [string] $SignToolPath
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
$installerBuildScript = Join-Path $windowsRoot `
    'Scripts\Build-WindowsInstaller.ps1'

$JamPath = Resolve-ExistingFile -Path $JamPath -Description 'Jam'
$helperBuildScript = Resolve-ExistingFile `
    -Path $helperBuildScript `
    -Description 'Common Windows helper build script'
$installerBuildScript = Resolve-ExistingFile `
    -Path $installerBuildScript `
    -Description 'Windows installer build script'

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
$installerPath = Join-Path $OutputRoot `
    "IwashiScope-$Version-Windows-x64-Setup.exe"
$manifestPath = Join-Path $OutputRoot 'RELEASE_MANIFEST.txt'

foreach ($reservedPath in @(
    $payloadPath,
    $zipPath,
    $installerPath,
    $manifestPath
)) {
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
    $helperArguments = @{
        JamPath = $JamPath
        Architecture = 'x64'
        OutputPath = $temporaryHelper
    }
    if (-not [string]::IsNullOrWhiteSpace($VsDevCmdPath)) {
        $helperArguments.VsDevCmdPath = $VsDevCmdPath
    }
    & $helperBuildScript @helperArguments

    $temporaryHelper = Resolve-ExistingFile `
        -Path $temporaryHelper `
        -Description 'Built iwashiscope-spotread.exe'
    $helperHash = (Get-FileHash -LiteralPath $temporaryHelper `
        -Algorithm SHA256).Hash

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
        "-p:IwashiScopeHelperPath=$temporaryHelper",
        '-o', $payloadPath
    )

    $publishedExecutable = Resolve-ExistingFile `
        -Path (Join-Path $payloadPath 'IwashiScope.exe') `
        -Description 'Published IwashiScope.exe'
    $publishedHelper = Resolve-ExistingFile `
        -Path (Join-Path $payloadPath 'iwashiscope-spotread.exe') `
        -Description 'Published iwashiscope-spotread.exe'
    $publishedWinSparkle = Resolve-ExistingFile `
        -Path (Join-Path $payloadPath 'WinSparkle.dll') `
        -Description 'Published WinSparkle.dll'
    $publishedHelperHash = (Get-FileHash -LiteralPath $publishedHelper `
        -Algorithm SHA256).Hash
    $publishedWinSparkleHash = (Get-FileHash `
        -LiteralPath $publishedWinSparkle `
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
        "Helper SHA-256: $publishedHelperHash"
    )
    [IO.File]::WriteAllLines(
        (Join-Path $payloadPath 'SOURCE_INFO.txt'),
        $sourceInfo,
        [Text.UTF8Encoding]::new($false)
    )

    $installerArguments = @{
        PayloadPath = $payloadPath
        Version = $Version
        OutputRoot = $OutputRoot
    }
    if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        $installerArguments.CertificateThumbprint = $CertificateThumbprint
    }
    if (-not [string]::IsNullOrWhiteSpace($SignToolPath)) {
        $installerArguments.SignToolPath = $SignToolPath
    }
    & $installerBuildScript @installerArguments
    $installerPath = Resolve-ExistingFile `
        -Path $installerPath `
        -Description 'Windows x64 installer'
    $installerItem = Get-Item -LiteralPath $installerPath
    $installerHash = (Get-FileHash -LiteralPath $installerPath `
        -Algorithm SHA256).Hash
    $installerSignature = Get-AuthenticodeSignature `
        -LiteralPath $installerPath

    Compress-Archive -LiteralPath $payloadPath -DestinationPath $zipPath
    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    $executableInfo = (Get-Item -LiteralPath $publishedExecutable).VersionInfo
    $signature = Get-AuthenticodeSignature -LiteralPath $publishedExecutable
    $winSparkleSignature = Get-AuthenticodeSignature `
        -LiteralPath $publishedWinSparkle

    $manifest = @(
        "Artifact: $zipPath"
        "Artifact SHA-256: $zipHash"
        "Installer: $installerPath"
        "Installer bytes: $($installerItem.Length)"
        "Installer SHA-256: $installerHash"
        "Installer Authenticode: $($installerSignature.Status)"
        "Application version: $($executableInfo.ProductVersion)"
        "Application file version: $($executableInfo.FileVersion)"
        "Application Authenticode: $($signature.Status)"
        "Helper SHA-256: $publishedHelperHash"
        "WinSparkle version: 0.9.4"
        "WinSparkle SHA-256: $publishedWinSparkleHash"
        "WinSparkle Authenticode: $($winSparkleSignature.Status)"
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
    Write-Host "Created Windows installer: $installerPath"
    Write-Host "Installer SHA-256: $installerHash"
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
