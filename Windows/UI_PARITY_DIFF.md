# macOS view-condition / Windows visible-text diff

Generated: 2026-07-29 22:03:22 +09:00

This is a mechanical token scan of the accessibility text captured from the
final fake-measurement screens. The expected/forbidden sets follow
`UI_PARITY_MATRIX.md` and the cited Swift view conditions. It is not a
pixel-diff against a macOS screenshot.

| Screen | Rule | Result | Mac source/override |
| --- | --- | --- | --- |
| Mode selection | 表示: 画像 | PASS | ModeSelectionView.swift + user icon override |
| Mode selection | 表示: 反射原稿 | PASS | ModeSelectionView.swift + user icon override |
| Mode selection | 表示: 環境光 | PASS | ModeSelectionView.swift + user icon override |
| Mode selection | 表示: 発光 | PASS | ModeSelectionView.swift + user icon override |
| Mode selection | 表示: spotreadを確認しました | PASS | ModeSelectionView.swift + user icon override |
| Mode selection | 非表示: Adobe RGB | PASS | ModeSelectionView.swift + user icon override |
| Mode selection | 非表示: ⌁ | PASS | ModeSelectionView.swift + user icon override |
| Mode selection | 非表示: ▤ | PASS | ModeSelectionView.swift + user icon override |
| Mode selection | 非表示: ☀ | PASS | ModeSelectionView.swift + user icon override |
| Mode selection | 非表示: ◉ | PASS | ModeSelectionView.swift + user icon override |
| Reflectance | 表示: 測定結果 | PASS | MeasurementWorkspaceView.swift + MeasurementDetailsView.swift |
| Reflectance | 表示: 測色値 | PASS | MeasurementWorkspaceView.swift + MeasurementDetailsView.swift |
| Reflectance | 表示: XYZ | PASS | MeasurementWorkspaceView.swift + MeasurementDetailsView.swift |
| Reflectance | 表示: D50 Lab | PASS | MeasurementWorkspaceView.swift + MeasurementDetailsView.swift |
| Reflectance | 表示: HEX（sRGB） | PASS | MeasurementWorkspaceView.swift + MeasurementDetailsView.swift |
| Reflectance | 表示: spotread詳細ログ | PASS | MeasurementWorkspaceView.swift + MeasurementDetailsView.swift |
| Reflectance | 表示: スウォッチ | PASS | MeasurementWorkspaceView.swift + MeasurementDetailsView.swift |
| Reflectance | 非表示: Adobe RGB | PASS | MeasurementWorkspaceView.swift + MeasurementDetailsView.swift |
| Reflectance | 非表示: 基準分光分布 | PASS | MeasurementWorkspaceView.swift + MeasurementDetailsView.swift |
| Reflectance | 非表示: JSPST-1998 | PASS | MeasurementWorkspaceView.swift + MeasurementDetailsView.swift |
| Reflectance | 非表示: ISO 3664:2025 | PASS | MeasurementWorkspaceView.swift + MeasurementDetailsView.swift |
| Reflectance | 非表示: 照度 | PASS | MeasurementWorkspaceView.swift + MeasurementDetailsView.swift |
| Reflectance | 非表示: TM-30 | PASS | MeasurementWorkspaceView.swift + MeasurementDetailsView.swift |
| Ambient | 表示: 基準分光分布 | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Ambient | 表示: CIE D50 | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Ambient | 表示: CIE D65 | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Ambient | 表示: CRI | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Ambient | 表示: TM-30 | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Ambient | 表示: 光源情報 | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Ambient | 表示: 照度 | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Ambient | 表示: JSPST-1998 | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Ambient | 表示: ISO 3664:2025 | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Ambient | 表示: spotread詳細ログ | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Ambient | 非表示: Adobe RGB | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Ambient | 非表示: HEX（sRGB） | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Ambient | 非表示: スウォッチ | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Ambient | 非表示: IES TM-30-15 | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Emissive | 表示: 基準分光分布 | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Emissive | 表示: CIE D50 | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Emissive | 表示: CIE D65 | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Emissive | 表示: CRI | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Emissive | 表示: TM-30 | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Emissive | 表示: HEX（sRGB） | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Emissive | 表示: 光源情報 | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Emissive | 表示: JSPST-1998 | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Emissive | 表示: ISO 3664:2025 | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Emissive | 非表示: Adobe RGB | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Emissive | 非表示: スウォッチ | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| Emissive | 非表示: IES TM-30-15 | PASS | SpectrumChartView.swift + LightingRenderingTabsView.swift + MeasurementDetailsView.swift |
| TM-30 minimum | 表示: IES TM-30-15 | PASS | TM30 views + 860x620 acceptance override |
| TM-30 minimum | 表示: Rf | PASS | TM30 views + 860x620 acceptance override |
| TM-30 minimum | 表示: Rg | PASS | TM30 views + 860x620 acceptance override |
| TM-30 minimum | 表示: Rf–Rgプロット | PASS | TM30 views + 860x620 acceptance override |
| TM-30 minimum | 表示: 99色評価用試料 | PASS | TM30 views + 860x620 acceptance override |
| TM-30 minimum | 非表示: Adobe RGB | PASS | TM30 views + 860x620 acceptance override |
| TM-30 minimum | PNG client capture: 847x613 (860x620 outer window) | PASS | PNG IHDR + MainWindow MinWidth/MinHeight |

Summary: **56 PASS / 0 FAIL**.

The TM-30 containment decision additionally relies on the responsive-layout
unit tests and the saved minimum-size screenshot; accessibility text alone
does not expose child pixel bounds.
