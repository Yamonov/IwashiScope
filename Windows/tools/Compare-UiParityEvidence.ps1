[CmdletBinding()]
param(
    [string]$EvidenceRoot,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

$scriptDirectory = if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    Join-Path (Get-Location) 'tools'
}
else {
    $PSScriptRoot
}
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptDirectory '..'))
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path $repositoryRoot 'artifacts\ui-parity'
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repositoryRoot 'UI_PARITY_DIFF.md'
}

$rows = [System.Collections.Generic.List[object]]::new()

function Add-TextChecks {
    param(
        [Parameter(Mandatory)] [string]$Screen,
        [Parameter(Mandatory)] [string]$FileName,
        [Parameter(Mandatory)] [string[]]$Required,
        [Parameter(Mandatory)] [string[]]$Forbidden,
        [Parameter(Mandatory)] [string]$Source
    )

    $path = Join-Path $EvidenceRoot $FileName
    $content = [System.IO.File]::ReadAllText($path)
    foreach ($token in $Required) {
        $present = $content.IndexOf($token, [StringComparison]::Ordinal) -ge 0
        $rows.Add([pscustomobject]@{
            Screen = $Screen
            Rule = "表示: $token"
            Result = if ($present) { 'PASS' } else { 'FAIL' }
            Source = $Source
        })
    }
    foreach ($token in $Forbidden) {
        $absent = $content.IndexOf($token, [StringComparison]::Ordinal) -lt 0
        $rows.Add([pscustomobject]@{
            Screen = $Screen
            Rule = "非表示: $token"
            Result = if ($absent) { 'PASS' } else { 'FAIL' }
            Source = $Source
        })
    }
}

Add-TextChecks `
    -Screen 'Mode selection' `
    -FileName 'mode-selection-app-icon-visible-text.txt' `
    -Required @('画像', '反射原稿', '環境光', '発光', 'spotreadを確認しました') `
    -Forbidden @('Adobe RGB', '⌁', '▤', '☀', '◉') `
    -Source 'ModeSelectionView.swift + user icon override'

Add-TextChecks `
    -Screen 'Reflectance' `
    -FileName 'final-reflectance-visible-text.txt' `
    -Required @('測定結果', '測色値', 'XYZ', 'D50 Lab', 'HEX（sRGB）', 'spotread詳細ログ', 'スウォッチ') `
    -Forbidden @('Adobe RGB', '基準分光分布', 'JSPST-1998', 'ISO 3664:2025', '照度', 'TM-30') `
    -Source 'MeasurementWorkspaceView.swift + MeasurementDetailsView.swift'

Add-TextChecks `
    -Screen 'Ambient' `
    -FileName 'final-ambient-visible-text.txt' `
    -Required @('基準分光分布', 'CIE D50', 'CIE D65', 'CRI', 'TM-30', '光源情報', '照度', 'JSPST-1998', 'ISO 3664:2025', 'spotread詳細ログ') `
    -Forbidden @('Adobe RGB', 'HEX（sRGB）', 'スウォッチ', 'IES TM-30-15') `
    -Source 'SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift'

Add-TextChecks `
    -Screen 'Emissive' `
    -FileName 'final-emissive-visible-text.txt' `
    -Required @('基準分光分布', 'CIE D50', 'CIE D65', 'CRI', 'TM-30', 'HEX（sRGB）', '光源情報', 'JSPST-1998', 'ISO 3664:2025') `
    -Forbidden @('Adobe RGB', 'スウォッチ', 'IES TM-30-15') `
    -Source 'SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift'

Add-TextChecks `
    -Screen 'TM-30 minimum' `
    -FileName 'ambient-tm30-min-visible-text.txt' `
    -Required @('IES TM-30-15', 'Rf', 'Rg', 'Rf–Rgプロット', '99色評価用試料') `
    -Forbidden @('Adobe RGB') `
    -Source 'TM30 views + 860x620 acceptance override'

$minimumPng = [System.IO.File]::ReadAllBytes(
    (Join-Path $EvidenceRoot 'ambient-tm30-min-860x620.png'))
$pngWidth = ([int]$minimumPng[16] * 16777216) +
    ([int]$minimumPng[17] * 65536) +
    ([int]$minimumPng[18] * 256) +
    [int]$minimumPng[19]
$pngHeight = ([int]$minimumPng[20] * 16777216) +
    ([int]$minimumPng[21] * 65536) +
    ([int]$minimumPng[22] * 256) +
    [int]$minimumPng[23]
$minimumCaptureMatches = $pngWidth -eq 847 -and $pngHeight -eq 613
$rows.Add([pscustomobject]@{
    Screen = 'TM-30 minimum'
    Rule = "PNG client capture: ${pngWidth}x${pngHeight} (860x620 outer window)"
    Result = if ($minimumCaptureMatches) { 'PASS' } else { 'FAIL' }
    Source = 'PNG IHDR + MainWindow MinWidth/MinHeight'
})

$passCount = @($rows | Where-Object Result -eq 'PASS').Count
$failCount = @($rows | Where-Object Result -eq 'FAIL').Count
$generatedAt = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss K')
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# macOS view-condition / Windows visible-text diff')
$lines.Add('')
$lines.Add("Generated: $generatedAt")
$lines.Add('')
$lines.Add('This is a mechanical token scan of the accessibility text captured from the')
$lines.Add('captured UI-evidence screens. The expected/forbidden sets follow')
$lines.Add('`UI_PARITY_MATRIX.md` and the cited Swift view conditions. It is not a')
$lines.Add('pixel-diff against a macOS screenshot.')
$lines.Add('')
$lines.Add("| Screen | Rule | Result | Mac source/override |")
$lines.Add("| --- | --- | --- | --- |")
foreach ($row in $rows) {
    $safeRule = $row.Rule.Replace('|', '\|')
    $safeSource = $row.Source.Replace('|', '\|')
    $lines.Add("| $($row.Screen) | $safeRule | $($row.Result) | $safeSource |")
}
$lines.Add('')
$lines.Add("Summary: **$passCount PASS / $failCount FAIL**.")
$lines.Add('')
$lines.Add('The TM-30 containment decision additionally relies on the responsive-layout')
$lines.Add('unit tests and the saved minimum-size screenshot; accessibility text alone')
$lines.Add('does not expose child pixel bounds.')

[System.IO.File]::WriteAllLines(
    [System.IO.Path]::GetFullPath($OutputPath),
    $lines,
    [System.Text.UTF8Encoding]::new($false))

if ($failCount -gt 0) {
    throw "UI parity evidence contains $failCount failed checks."
}

[pscustomobject]@{
    Output = [System.IO.Path]::GetFullPath($OutputPath)
    Pass = $passCount
    Fail = $failCount
    MinimumCapture = "${pngWidth}x${pngHeight}"
} | Format-List
