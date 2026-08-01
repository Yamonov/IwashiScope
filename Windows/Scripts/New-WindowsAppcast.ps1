[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:\.\d+)?$')]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9._-]+$')]
    [string] $TagName,

    [Parameter(Mandatory = $true)]
    [string] $InstallerPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $EdSignature,

    [string] $SourceAppcastPath,

    [string] $OutputPath,

    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string] $Repository = 'Yamonov/IwashiScope',

    [string] $ReleaseNotesUrl,

    [string] $WinSparkleToolPath,

    [string] $PublicKey =
        'g7hHfIKHUp7kyMctJVsrKI0EHa7OyGRQgRhFRDdFHXM='
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Escape-Xml([string] $Value) {
    return [Security.SecurityElement]::Escape($Value)
}

$windowsRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $windowsRoot '..'))
$installerFull = [IO.Path]::GetFullPath($InstallerPath)
if (-not (Test-Path -LiteralPath $installerFull -PathType Leaf)) {
    throw "Windows installer was not found: $installerFull"
}
$installer = Get-Item -LiteralPath $installerFull
$expectedInstallerName = "IwashiScope-$Version-Windows-x64-Setup.exe"
if ($installer.Name -cne $expectedInstallerName) {
    throw "Installer filename must be $expectedInstallerName; got $($installer.Name)."
}
$versionInfo = $installer.VersionInfo
if ($versionInfo.ProductVersion -ne $Version -or
    $versionInfo.FileVersion -ne $Version) {
    throw "Installer ProductVersion/FileVersion must both be $Version."
}

try {
    $signatureBytes = [Convert]::FromBase64String($EdSignature)
}
catch {
    throw 'EdSignature must be a valid Base64-encoded EdDSA signature.'
}
if ($signatureBytes.Length -ne 64) {
    throw "EdSignature must decode to 64 bytes; got $($signatureBytes.Length)."
}
try {
    $publicKeyBytes = [Convert]::FromBase64String($PublicKey)
}
catch {
    throw 'PublicKey must be a valid Base64-encoded EdDSA public key.'
}
if ($publicKeyBytes.Length -ne 32) {
    throw "PublicKey must decode to 32 bytes; got $($publicKeyBytes.Length)."
}

if ([string]::IsNullOrWhiteSpace($WinSparkleToolPath)) {
    $WinSparkleToolPath = Join-Path $env:USERPROFILE `
        '.nuget\packages\winsparkle\0.9.4\tools\winsparkle-tool.exe'
}
$WinSparkleToolPath = [IO.Path]::GetFullPath($WinSparkleToolPath)
if (-not (Test-Path -LiteralPath $WinSparkleToolPath -PathType Leaf)) {
    throw "winsparkle-tool.exe was not found: $WinSparkleToolPath"
}
& $WinSparkleToolPath verify `
    --public-key $PublicKey `
    --signature $EdSignature `
    $installerFull
if ($LASTEXITCODE -ne 0) {
    throw "EdDSA verification failed with exit code $LASTEXITCODE."
}

if ([string]::IsNullOrWhiteSpace($SourceAppcastPath)) {
    $SourceAppcastPath = Join-Path $repositoryRoot 'docs\appcast-windows.xml'
}
$sourceFull = [IO.Path]::GetFullPath($SourceAppcastPath)
if (-not (Test-Path -LiteralPath $sourceFull -PathType Leaf)) {
    throw "Source Windows appcast was not found: $sourceFull"
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $windowsRoot `
        "artifacts\appcast-$Version\appcast-windows.xml"
}
$outputFull = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $outputFull) {
    throw "Appcast output already exists; preserving it: $outputFull"
}
$outputDirectory = Split-Path -Parent $outputFull
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$installerUrl = "https://github.com/$Repository/releases/download/" +
    "$TagName/$($installer.Name)"
if ([string]::IsNullOrWhiteSpace($ReleaseNotesUrl)) {
    $ReleaseNotesUrl = "https://github.com/$Repository/releases/tag/$TagName"
}
$releaseNotesUri = $null
if (-not [Uri]::TryCreate(
        $ReleaseNotesUrl,
        [UriKind]::Absolute,
        [ref] $releaseNotesUri) -or
    $releaseNotesUri.Scheme -ne [Uri]::UriSchemeHttps) {
    throw "ReleaseNotesUrl must use HTTPS: $ReleaseNotesUrl"
}
$pubDate = [DateTimeOffset]::UtcNow.ToString(
    'r',
    [Globalization.CultureInfo]::InvariantCulture)

$document = [Xml.XmlDocument]::new()
$document.PreserveWhitespace = $false
$document.Load($sourceFull)
$namespace = [Xml.XmlNamespaceManager]::new($document.NameTable)
$namespace.AddNamespace(
    'sparkle',
    'http://www.andymatuschak.org/xml-namespaces/sparkle')
$channel = $document.SelectSingleNode('/rss/channel', $namespace)
if ($null -eq $channel) {
    throw 'Source Windows appcast does not contain /rss/channel.'
}
$existing = $document.SelectNodes(
    "/rss/channel/item[sparkle:version='$Version']",
    $namespace)
if ($existing.Count -ne 0) {
    throw "Windows appcast already contains version $Version."
}

$fragment = $document.CreateDocumentFragment()
$fragment.InnerXml = @"
<item xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle">
  <title>IwashiScope $(Escape-Xml $Version) for Windows</title>
  <pubDate>$pubDate</pubDate>
  <sparkle:releaseNotesLink>$(Escape-Xml $ReleaseNotesUrl)</sparkle:releaseNotesLink>
  <sparkle:version>$(Escape-Xml $Version)</sparkle:version>
  <sparkle:shortVersionString>$(Escape-Xml $Version)</sparkle:shortVersionString>
  <sparkle:minimumSystemVersion>10.0</sparkle:minimumSystemVersion>
  <enclosure
      url="$(Escape-Xml $installerUrl)"
      length="$($installer.Length)"
      type="application/octet-stream"
      sparkle:os="windows-x64"
      sparkle:edSignature="$(Escape-Xml $EdSignature)" />
</item>
"@
$firstItem = $channel.SelectSingleNode('item')
if ($null -eq $firstItem) {
    $null = $channel.AppendChild($fragment)
}
else {
    $null = $channel.InsertBefore($fragment, $firstItem)
}

$settings = [Xml.XmlWriterSettings]::new()
$settings.Encoding = [Text.UTF8Encoding]::new($false)
$settings.Indent = $true
$settings.IndentChars = '    '
$settings.NewLineChars = "`r`n"
$settings.NewLineHandling = [Xml.NewLineHandling]::Replace
$writer = $null
try {
    $writer = [Xml.XmlWriter]::Create($outputFull, $settings)
    $document.Save($writer)
}
finally {
    if ($null -ne $writer) {
        $writer.Dispose()
    }
}

& (Join-Path $PSScriptRoot 'Test-WindowsAppcast.ps1') `
    -AppcastPath $outputFull `
    -InstallerPath $installerFull `
    -WinSparkleToolPath $WinSparkleToolPath `
    -PublicKey $PublicKey `
    -RequireItem

Write-Host "Created Windows appcast: $outputFull"
Write-Host "Installer URL: $installerUrl"
Write-Host "Installer bytes: $($installer.Length)"
Write-Host 'Platform: windows-x64'
