[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $InstallerPath,

    [string] $SourceAppcastPath,

    [string] $WinSparkleToolPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$windowsRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $windowsRoot '..'))
$installerFull = [IO.Path]::GetFullPath($InstallerPath)
if (-not (Test-Path -LiteralPath $installerFull -PathType Leaf)) {
    throw "Unsigned installer was not found: $installerFull"
}
if ([string]::IsNullOrWhiteSpace($SourceAppcastPath)) {
    $SourceAppcastPath = Join-Path $repositoryRoot 'docs\appcast-windows.xml'
}
$sourceAppcastFull = [IO.Path]::GetFullPath($SourceAppcastPath)
if (-not (Test-Path -LiteralPath $sourceAppcastFull -PathType Leaf)) {
    throw "Source Windows appcast was not found: $sourceAppcastFull"
}
if ([string]::IsNullOrWhiteSpace($WinSparkleToolPath)) {
    $WinSparkleToolPath = Join-Path $env:USERPROFILE `
        '.nuget\packages\winsparkle\0.9.4\tools\winsparkle-tool.exe'
}
$WinSparkleToolPath = [IO.Path]::GetFullPath($WinSparkleToolPath)
if (-not (Test-Path -LiteralPath $WinSparkleToolPath -PathType Leaf)) {
    throw "winsparkle-tool.exe was not found: $WinSparkleToolPath"
}

$sourceFeedHash = (Get-FileHash -LiteralPath $sourceAppcastFull `
    -Algorithm SHA256).Hash
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) `
    ("IwashiScope-WinSparkle-Test-" + [Guid]::NewGuid().ToString('N'))
$temporaryPublicKey = $null
$temporarySignedHash = $null
$appcastItemCount = 0
$historyAppcastItemCount = 0
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    $privateKey = Join-Path $temporaryRoot 'temporary-private.key'
    $testInstaller = Join-Path $temporaryRoot `
        (Split-Path -Leaf $installerFull)
    $testAppcast = Join-Path $temporaryRoot 'appcast-windows.xml'
    $historySourceAppcast = Join-Path $temporaryRoot `
        'appcast-windows-history-source.xml'
    $historyTestAppcast = Join-Path $temporaryRoot `
        'appcast-windows-history.xml'
    Copy-Item -LiteralPath $installerFull -Destination $testInstaller

    & $WinSparkleToolPath generate-key --file $privateKey | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Temporary key generation failed with exit code $LASTEXITCODE."
    }
    $publicOutput = (& $WinSparkleToolPath public-key `
        --private-key-file $privateKey) -join ' '
    if ($LASTEXITCODE -ne 0) {
        throw "Temporary public-key derivation failed with exit code $LASTEXITCODE."
    }
    $temporaryPublicKey = [regex]::Match(
        $publicOutput,
        '[A-Za-z0-9+/]{43}=').Value
    if ([string]::IsNullOrWhiteSpace($temporaryPublicKey)) {
        throw 'Unable to parse the temporary EdDSA public key.'
    }

    $signOutput = (& $WinSparkleToolPath sign `
        --private-key-file $privateKey `
        $testInstaller) -join ' '
    if ($LASTEXITCODE -ne 0) {
        throw "Temporary signing failed with exit code $LASTEXITCODE."
    }
    $signature = [regex]::Match(
        $signOutput,
        '[A-Za-z0-9+/]{86}==').Value
    if ([string]::IsNullOrWhiteSpace($signature)) {
        throw 'Unable to parse the temporary EdDSA signature.'
    }

    & $WinSparkleToolPath verify `
        --public-key $temporaryPublicKey `
        --signature $signature `
        $testInstaller
    if ($LASTEXITCODE -ne 0) {
        throw "Direct temporary signature verification failed with exit code $LASTEXITCODE."
    }

    & (Join-Path $PSScriptRoot 'New-WindowsAppcast.ps1') `
        -Version (Get-Item $testInstaller).VersionInfo.ProductVersion `
        -TagName 'temporary-signing-test' `
        -InstallerPath $testInstaller `
        -EdSignature $signature `
        -SourceAppcastPath $sourceAppcastFull `
        -OutputPath $testAppcast `
        -WinSparkleToolPath $WinSparkleToolPath `
        -PublicKey $temporaryPublicKey

    & (Join-Path $PSScriptRoot 'Test-WindowsAppcast.ps1') `
        -AppcastPath $testAppcast `
        -InstallerPath $testInstaller `
        -WinSparkleToolPath $WinSparkleToolPath `
        -PublicKey $temporaryPublicKey `
        -RequireItem

    [xml] $generated = Get-Content -LiteralPath $testAppcast `
        -Raw -Encoding UTF8
    $appcastItemCount = @($generated.rss.channel.item).Count
    $historicalXml = $generated.OuterXml.Replace(
        '<sparkle:version>0.9.5.4</sparkle:version>',
        '<sparkle:version>0.9.5.3</sparkle:version>').Replace(
        '<sparkle:shortVersionString>0.9.5.4</sparkle:shortVersionString>',
        '<sparkle:shortVersionString>0.9.5.3</sparkle:shortVersionString>').Replace(
        'IwashiScope-0.9.5.4-Windows-x64-Setup.exe',
        'IwashiScope-0.9.5.3-Windows-x64-Setup.exe')
    [IO.File]::WriteAllText(
        $historySourceAppcast,
        $historicalXml,
        [Text.UTF8Encoding]::new($false))
    & (Join-Path $PSScriptRoot 'New-WindowsAppcast.ps1') `
        -Version (Get-Item $testInstaller).VersionInfo.ProductVersion `
        -TagName 'temporary-signing-history-test' `
        -InstallerPath $testInstaller `
        -EdSignature $signature `
        -SourceAppcastPath $historySourceAppcast `
        -OutputPath $historyTestAppcast `
        -WinSparkleToolPath $WinSparkleToolPath `
        -PublicKey $temporaryPublicKey
    [xml] $historyGenerated = Get-Content -LiteralPath $historyTestAppcast `
        -Raw -Encoding UTF8
    $historyAppcastItemCount = @($historyGenerated.rss.channel.item).Count
    if ($historyAppcastItemCount -ne 2 -or
        $historyGenerated.rss.channel.item[0].version -ne '0.9.5.4' -or
        $historyGenerated.rss.channel.item[1].version -ne '0.9.5.3') {
        throw 'Windows appcast history was not preserved in newest-first order.'
    }
    $temporarySignedHash = (Get-FileHash -LiteralPath $testInstaller `
        -Algorithm SHA256).Hash
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
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}

$sourceFeedHashAfter = (Get-FileHash -LiteralPath $sourceAppcastFull `
    -Algorithm SHA256).Hash
if ($sourceFeedHashAfter -ne $sourceFeedHash) {
    throw 'Production Windows appcast changed during the temporary signing test.'
}
if (Test-Path -LiteralPath $temporaryRoot) {
    throw 'Temporary signing directory was not removed.'
}

Write-Host 'Temporary WinSparkle signing test passed.'
Write-Host "Temporary public key: $temporaryPublicKey"
Write-Host "Temporary installer SHA-256 after detached signing: $temporarySignedHash"
Write-Host "Generated appcast items: $appcastItemCount"
Write-Host "History-preserving appcast items: $historyAppcastItemCount"
Write-Host 'Production public key and feed were not changed.'
Write-Host 'Temporary private key and signed artifacts were removed.'
