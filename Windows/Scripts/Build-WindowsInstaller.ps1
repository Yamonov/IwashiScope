[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $PayloadPath,

    [ValidatePattern('^\d+\.\d+(?:\.\d+){0,2}$')]
    [string] $Version = '1.0.1',

    [string] $OutputRoot,

    [string] $CertificateThumbprint,

    [string] $SignToolPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$windowsRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $windowsRoot 'artifacts'))
$payloadFull = [IO.Path]::GetFullPath($PayloadPath)
if (-not (Test-Path -LiteralPath $payloadFull -PathType Container)) {
    throw "Published payload directory was not found: $payloadFull"
}
if (-not (Test-Path -LiteralPath (Join-Path $payloadFull 'IwashiScope.exe') -PathType Leaf)) {
    throw "IwashiScope.exe was not found in the payload: $payloadFull"
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $windowsRoot "artifacts\release-$Version"
}
$outputFull = [IO.Path]::GetFullPath($OutputRoot)
if (-not $outputFull.StartsWith(
    $artifactsRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase
)) {
    throw "OutputRoot must be inside Windows/artifacts: $outputFull"
}
New-Item -ItemType Directory -Path $outputFull -Force | Out-Null

$installerPath = Join-Path $outputFull `
    "IwashiScope-$Version-Windows-x64-Setup.exe"
if (Test-Path -LiteralPath $installerPath) {
    throw "Installer output already exists; preserving it: $installerPath"
}

$source = Join-Path $PSScriptRoot 'installer\IwashiScopeAppInstaller.cs'
$coreSource = Join-Path $PSScriptRoot `
    'installer\IwashiScopeInstallerCore.cs'
$installerTestScript = Join-Path $PSScriptRoot `
    'Test-WindowsInstaller.ps1'
$icon = Join-Path $windowsRoot `
    'src\IwashiScope.App.Wpf\Resources\Icons\IwashiScope.ico'
$csc = Join-Path $env:WINDIR `
    'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
foreach ($required in @(
    $source,
    $coreSource,
    $installerTestScript,
    $icon,
    $csc
)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required installer build input was not found: $required"
    }
}

& $installerTestScript

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) `
    ("IwashiScope-installer-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    $payloadZip = Join-Path $temporaryRoot 'payload.zip'
    $manifest = Join-Path $temporaryRoot 'install.properties'
    $assemblyInfo = Join-Path $temporaryRoot 'AssemblyInfo.cs'
    Compress-Archive -Path (Join-Path $payloadFull '*') `
        -DestinationPath $payloadZip
    [IO.File]::WriteAllLines(
        $manifest,
        @(
            "DisplayVersion=$Version"
            'Publisher=Yamonov'
        ),
        [Text.UTF8Encoding]::new($false)
    )
    [IO.File]::WriteAllLines(
        $assemblyInfo,
        @(
            'using System.Reflection;'
            '[assembly: AssemblyTitle("IwashiScope Installer")]'
            '[assembly: AssemblyProduct("IwashiScope")]'
            '[assembly: AssemblyCompany("Yamonov")]'
            "[assembly: AssemblyVersion(`"$Version`")]"
            "[assembly: AssemblyFileVersion(`"$Version`")]"
            "[assembly: AssemblyInformationalVersion(`"$Version`")]"
        ),
        [Text.UTF8Encoding]::new($false)
    )

    & $csc `
        /nologo `
        /target:winexe `
        /platform:x64 `
        /optimize+ `
        /win32icon:$icon `
        /out:$installerPath `
        "/resource:$payloadZip,payload.zip" `
        "/resource:$manifest,install.properties" `
        /reference:System.IO.Compression.dll `
        /reference:System.IO.Compression.FileSystem.dll `
        /reference:System.Windows.Forms.dll `
        $assemblyInfo `
        $coreSource `
        $source
    if ($LASTEXITCODE -ne 0) {
        throw "Installer compilation failed with exit code $LASTEXITCODE."
    }

    if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        if ([string]::IsNullOrWhiteSpace($SignToolPath)) {
            $kitsRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
            $SignToolPath = Get-ChildItem -LiteralPath $kitsRoot `
                -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
                Sort-Object FullName -Descending |
                Select-Object -First 1 -ExpandProperty FullName
        }
        if ([string]::IsNullOrWhiteSpace($SignToolPath) -or
            -not (Test-Path -LiteralPath $SignToolPath -PathType Leaf)) {
            throw 'signtool.exe was not found.'
        }
        & $SignToolPath sign /sha1 $CertificateThumbprint /fd SHA256 `
            /tr http://timestamp.digicert.com /td SHA256 $installerPath
        if ($LASTEXITCODE -ne 0) {
            throw "Installer signing failed with exit code $LASTEXITCODE."
        }
    }

    $installer = Get-Item -LiteralPath $installerPath
    $hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
    $signature = Get-AuthenticodeSignature -LiteralPath $installerPath
    Write-Host "Created Windows installer: $installerPath"
    Write-Host "Installer bytes: $($installer.Length)"
    Write-Host "Installer SHA-256: $hash"
    Write-Host "Installer Authenticode: $($signature.Status)"
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
        $systemTemporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolvedTemporaryRoot.StartsWith(
            $systemTemporaryRoot,
            [StringComparison]::OrdinalIgnoreCase
        )) {
            throw "Refusing to remove unexpected temporary path: $temporaryRoot"
        }
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
