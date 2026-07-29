[CmdletBinding()]
param(
    [string] $JamPath,
    [string] $VsDevCmdPath,
    [ValidateSet("x64")]
    [string] $Architecture = "x64",
    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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

function Quote-CmdPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    return '"' + $Path.Replace('"', '""') + '"'
}

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$argyllRoot = Join-Path $projectRoot "Argyll_V3.5.0"
$testExecutable = Join-Path $argyllRoot "spectro\iwashiscope-spotread-jsonl-test.exe"
$helperExecutable = Join-Path $argyllRoot "spectro\iwashiscope-spotread.exe"

if ([string]::IsNullOrWhiteSpace($JamPath)) {
    $jamCommand = Get-Command "jam.exe" -ErrorAction SilentlyContinue
    if ($null -eq $jamCommand) {
        throw "Jam was not found. Pass its absolute path with -JamPath."
    }
    $JamPath = $jamCommand.Source
}
$JamPath = Resolve-ExistingFile -Path $JamPath -Description "Jam"

if ([string]::IsNullOrWhiteSpace($VsDevCmdPath)) {
    $programFilesX86 = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::ProgramFilesX86
    )
    $vswherePath = Join-Path $programFilesX86 `
        "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path -LiteralPath $vswherePath -PathType Leaf)) {
        throw "Visual Studio locator was not found. Pass VsDevCmd.bat with -VsDevCmdPath."
    }
    $installationPath = & $vswherePath `
        -latest `
        -products * `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($installationPath)) {
        throw "Visual Studio with the C++ x64 tools was not found."
    }
    $VsDevCmdPath = Join-Path $installationPath "Common7\Tools\VsDevCmd.bat"
}
$VsDevCmdPath = Resolve-ExistingFile `
    -Path $VsDevCmdPath `
    -Description "Visual Studio developer command script"

$quotedJam = Quote-CmdPath -Path $JamPath
$quotedVsDevCmd = Quote-CmdPath -Path $VsDevCmdPath
$quotedTestExecutable = Quote-CmdPath -Path $testExecutable
$commands = @(
    "call $quotedVsDevCmd -no_logo -arch=$Architecture -host_arch=$Architecture",
    "$quotedJam -q -fJambase -sIWASHISCOPE_ONLY=true clean",
    "$quotedJam -q -fJambase -sIWASHISCOPE_ONLY=true -sIWASHISCOPE_TESTS=true iwashiscope_spotread_jsonl_test iwashiscope_spotread",
    $quotedTestExecutable
) -join " && "

Push-Location $argyllRoot
try {
    & $env:ComSpec /d /s /c $commands
    if ($LASTEXITCODE -ne 0) {
        throw "The Windows spotread build failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$helperExecutable = Resolve-ExistingFile `
    -Path $helperExecutable `
    -Description "Built iwashiscope-spotread.exe"

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath(
        $OutputPath
    )
    $outputDirectory = Split-Path -Parent $resolvedOutputPath
    if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
        New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    }
    Copy-Item -LiteralPath $helperExecutable -Destination $resolvedOutputPath -Force
    $helperExecutable = $resolvedOutputPath
}

Write-Host "Built Windows helper: $helperExecutable"
