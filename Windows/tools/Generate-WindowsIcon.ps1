[CmdletBinding()]
param(
    [string]$ReferenceRoot,
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

if ([string]::IsNullOrWhiteSpace($ReferenceRoot)) {
    $ReferenceRoot = [System.IO.Path]::GetFullPath(
        (Join-Path $PSScriptRoot '..\..')
    )
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $PSScriptRoot '..\src\IwashiScope.App.Wpf\Resources\Icons'
}

$assetRoot = Join-Path $ReferenceRoot 'IwashiScope\Assets.xcassets\AppIcon.appiconset'
$source1024 = Join-Path $assetRoot 'IwashiScope-512@2x.png'
$requiredSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$exactSources = @{
    16  = 'IwashiScope-16.png'
    32  = 'IwashiScope-32.png'
    64  = 'IwashiScope-32@2x.png'
    128 = 'IwashiScope-128.png'
    256 = 'IwashiScope-256.png'
}

if (-not (Test-Path -LiteralPath $source1024 -PathType Leaf)) {
    throw "Mac app icon source not found: $source1024"
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

function Save-HighQualityPng {
    param(
        [Parameter(Mandatory)] [string]$Source,
        [Parameter(Mandatory)] [string]$Destination,
        [Parameter(Mandatory)] [int]$Size
    )

    $sourceImage = [System.Drawing.Image]::FromFile($Source)
    try {
        $target = [System.Drawing.Bitmap]::new(
            $Size,
            $Size,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $target.SetResolution(96, 96)
            $graphics = [System.Drawing.Graphics]::FromImage($target)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.CompositingMode =
                    [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality =
                    [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode =
                    [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.SmoothingMode =
                    [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.PixelOffsetMode =
                    [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.DrawImage(
                    $sourceImage,
                    [System.Drawing.Rectangle]::new(0, 0, $Size, $Size),
                    0,
                    0,
                    $sourceImage.Width,
                    $sourceImage.Height,
                    [System.Drawing.GraphicsUnit]::Pixel)
            }
            finally {
                $graphics.Dispose()
            }

            $target.Save($Destination, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $target.Dispose()
        }
    }
    finally {
        $sourceImage.Dispose()
    }
}

$pngPaths = [System.Collections.Generic.List[string]]::new()
foreach ($size in $requiredSizes) {
    $destination = Join-Path $OutputRoot "IwashiScope-$size.png"
    if ($exactSources.ContainsKey($size)) {
        Copy-Item -LiteralPath (Join-Path $assetRoot $exactSources[$size]) `
            -Destination $destination -Force
    }
    else {
        Save-HighQualityPng -Source $source1024 -Destination $destination -Size $size
    }
    $pngPaths.Add($destination)
}

# Modern Windows accepts PNG-compressed frames inside an ICO container. Keeping
# each PNG as a complete frame preserves the source alpha channel and avoids a
# second rounded-rectangle mask around the already-shaped Mac artwork.
$icoPath = Join-Path $OutputRoot 'IwashiScope.ico'
$payloads = @($pngPaths | ForEach-Object {
    ,([System.IO.File]::ReadAllBytes($_))
})
$directoryLength = 6 + (16 * $payloads.Count)
$offset = $directoryLength

$stream = [System.IO.File]::Open(
    $icoPath,
    [System.IO.FileMode]::Create,
    [System.IO.FileAccess]::Write,
    [System.IO.FileShare]::None)
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$payloads.Count)

    for ($index = 0; $index -lt $payloads.Count; $index++) {
        $size = $requiredSizes[$index]
        $dimensionByte = if ($size -eq 256) { 0 } else { $size }
        $writer.Write([byte]$dimensionByte)
        $writer.Write([byte]$dimensionByte)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$payloads[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $payloads[$index].Length
    }

    foreach ($payload in $payloads) {
        $writer.Write($payload)
    }
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

$verification = foreach ($pngPath in $pngPaths) {
    $bitmap = [System.Drawing.Bitmap]::FromFile($pngPath)
    try {
        $cornerAlpha = @(
            $bitmap.GetPixel(0, 0).A
            $bitmap.GetPixel($bitmap.Width - 1, 0).A
            $bitmap.GetPixel(0, $bitmap.Height - 1).A
            $bitmap.GetPixel($bitmap.Width - 1, $bitmap.Height - 1).A
        )
        [pscustomobject]@{
            File = Split-Path -Leaf $pngPath
            Width = $bitmap.Width
            Height = $bitmap.Height
            CornerAlpha = $cornerAlpha -join ','
            Sha256 = (Get-FileHash -LiteralPath $pngPath -Algorithm SHA256).Hash
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

$verification | Format-Table -AutoSize
[pscustomobject]@{
    IcoPath = (Resolve-Path -LiteralPath $icoPath).Path
    IcoSha256 = (Get-FileHash -LiteralPath $icoPath -Algorithm SHA256).Hash
    Frames = $requiredSizes -join ','
    Source = (Resolve-Path -LiteralPath $source1024).Path
} | Format-List
