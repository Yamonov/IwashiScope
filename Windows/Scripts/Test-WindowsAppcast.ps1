[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $AppcastPath,

    [string] $InstallerPath,

    [string] $WinSparkleToolPath,

    [string] $PublicKey =
        'g7hHfIKHUp7kyMctJVsrKI0EHa7OyGRQgRhFRDdFHXM=',

    [switch] $RequireItem
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$appcastFull = [IO.Path]::GetFullPath($AppcastPath)
if (-not (Test-Path -LiteralPath $appcastFull -PathType Leaf)) {
    throw "Windows appcast was not found: $appcastFull"
}
$document = [Xml.XmlDocument]::new()
$document.PreserveWhitespace = $true
$document.Load($appcastFull)
$namespace = [Xml.XmlNamespaceManager]::new($document.NameTable)
$namespace.AddNamespace(
    'sparkle',
    'http://www.andymatuschak.org/xml-namespaces/sparkle')
$channel = $document.SelectSingleNode('/rss/channel', $namespace)
if ($null -eq $channel) {
    throw 'Windows appcast does not contain /rss/channel.'
}
$items = @($document.SelectNodes('/rss/channel/item', $namespace))
if ($RequireItem -and $items.Count -eq 0) {
    throw 'Windows appcast must contain at least one signed update item.'
}

$versions = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
foreach ($item in $items) {
    $versionNode = $item.SelectSingleNode('sparkle:version', $namespace)
    $shortVersionNode = $item.SelectSingleNode(
        'sparkle:shortVersionString',
        $namespace)
    $enclosures = @($item.SelectNodes('enclosure', $namespace))
    $enclosure = if ($enclosures.Count -eq 1) { $enclosures[0] } else { $null }
    if ($null -eq $versionNode -or
        [string]::IsNullOrWhiteSpace($versionNode.InnerText) -or
        $null -eq $shortVersionNode -or
        [string]::IsNullOrWhiteSpace($shortVersionNode.InnerText) -or
        $null -eq $enclosure) {
        throw 'Every Windows appcast item must contain versions and an enclosure.'
    }
    if (-not $versions.Add($versionNode.InnerText)) {
        throw "Duplicate Windows appcast version: $($versionNode.InnerText)"
    }
    if ($versionNode.InnerText -ne $shortVersionNode.InnerText -or
        $versionNode.InnerText -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') {
        throw 'Windows appcast display and build versions must be the same numeric version.'
    }

    $url = $enclosure.GetAttribute('url')
    $lengthText = $enclosure.GetAttribute('length')
    $os = $enclosure.GetAttribute(
        'os',
        'http://www.andymatuschak.org/xml-namespaces/sparkle')
    $signature = $enclosure.GetAttribute(
        'edSignature',
        'http://www.andymatuschak.org/xml-namespaces/sparkle')
    $uri = $null
    if (-not [Uri]::TryCreate($url, [UriKind]::Absolute, [ref] $uri) -or
        $uri.Scheme -ne [Uri]::UriSchemeHttps) {
        throw "Windows enclosure URL must use HTTPS: $url"
    }
    [long] $declaredLength = 0
    if (-not [long]::TryParse($lengthText, [ref] $declaredLength) -or
        $declaredLength -le 0) {
        throw "Windows enclosure length must be a positive integer: $lengthText"
    }
    if ($os -ne 'windows-x64') {
        throw "Windows enclosure must specify sparkle:os=windows-x64; got $os."
    }
    $expectedFileName = "IwashiScope-$($versionNode.InnerText)-Windows-x64-Setup.exe"
    if ($uri.Segments[-1] -ne $expectedFileName) {
        throw "Windows enclosure must name the installable x64 setup: $expectedFileName"
    }
    try {
        $signatureBytes = [Convert]::FromBase64String($signature)
    }
    catch {
        throw "Windows enclosure has an invalid Base64 EdDSA signature."
    }
    if ($signatureBytes.Length -ne 64) {
        throw "Windows enclosure EdDSA signature must decode to 64 bytes."
    }
}

if (-not [string]::IsNullOrWhiteSpace($InstallerPath)) {
    if ($items.Count -eq 0) {
        throw 'No Windows appcast item is available for installer verification.'
    }
    $installerFull = [IO.Path]::GetFullPath($InstallerPath)
    if (-not (Test-Path -LiteralPath $installerFull -PathType Leaf)) {
        throw "Windows installer was not found: $installerFull"
    }
    $installer = Get-Item -LiteralPath $installerFull
    $matchingItems = @($items | Where-Object {
        $candidate = $_.SelectSingleNode('enclosure', $namespace)
        ([Uri] $candidate.GetAttribute('url')).Segments[-1] -eq $installer.Name
    })
    if ($matchingItems.Count -ne 1) {
        throw "Expected exactly one appcast item for $($installer.Name)."
    }
    $matchingItem = $matchingItems[0]
    $matchingEnclosure = $matchingItem.SelectSingleNode('enclosure', $namespace)
    $matchingVersion = $matchingItem.SelectSingleNode(
        'sparkle:version',
        $namespace).InnerText
    $matchingShortVersion = $matchingItem.SelectSingleNode(
        'sparkle:shortVersionString',
        $namespace).InnerText
    if ([long]$matchingEnclosure.GetAttribute('length') -ne $installer.Length) {
        throw 'Windows appcast byte length does not match the installer.'
    }
    if ($matchingVersion -ne $matchingShortVersion -or
        $matchingVersion -ne $installer.VersionInfo.FileVersion -or
        $matchingVersion -ne $installer.VersionInfo.ProductVersion) {
        throw 'Windows appcast version does not match installer metadata.'
    }

    if ([string]::IsNullOrWhiteSpace($WinSparkleToolPath)) {
        $WinSparkleToolPath = Join-Path $env:USERPROFILE `
            '.nuget\packages\winsparkle\0.9.4\tools\winsparkle-tool.exe'
    }
    $WinSparkleToolPath = [IO.Path]::GetFullPath($WinSparkleToolPath)
    if (-not (Test-Path -LiteralPath $WinSparkleToolPath -PathType Leaf)) {
        throw "winsparkle-tool.exe was not found: $WinSparkleToolPath"
    }
    $signature = $matchingEnclosure.GetAttribute(
        'edSignature',
        'http://www.andymatuschak.org/xml-namespaces/sparkle')
    & $WinSparkleToolPath verify `
        --public-key $PublicKey `
        --signature $signature `
        $installerFull
    if ($LASTEXITCODE -ne 0) {
        throw "EdDSA verification failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Windows appcast valid: $appcastFull"
Write-Host "Windows appcast items: $($items.Count)"
