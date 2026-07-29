# Windows icon asset report

基準は macOS 公開版 `v0.9.4`
（commit `343405721f1d1a5184efd32c24054259845d608c`）です。

## 正本

- 編集可能な原稿:
  `_reference/IwashiScope/IwashiScope.icon/icon.json`
  (`C0E785D1507EC864AC8960B2E0C1F885C3CC558B6985B8FEE9730326D1F00258`)
- 色帯・魚の原稿:
  `_reference/IwashiScope/IwashiScope.icon/Assets/Image 2.svg`
  (`B360E30206855449BD5C5D1EAFACF693974E72FD98496FDFE68373ED639E1D33`)
- 角丸背景の原稿:
  `_reference/IwashiScope/IwashiScope.icon/Assets/Image 3.svg`
  (`40C8330C00F044A4048C2C5A05F3E7963B89AF20DD777B11BAC8DD5E0358AF8A`)
- Xcode で書き出された正規ビットマップ:
  `_reference/IwashiScope/IwashiScope/Assets.xcassets/AppIcon.appiconset/`
- 最大の書き出し済みPNG:
  `IwashiScope-512@2x.png`（1024×1024、
  `B1287C320B8634CB45A8E6D857AA787370EC8E580C8A500CF2D3C4E86E9D93C0`）

`IwashiScope.icon` のレイヤーをWindows側で再解釈せず、Xcodeが既に合成した
AppIcon PNGを入力にしています。元PNGは四隅が透過済みです。Windows用生成で
角丸マスク、背景、影を追加していないため、macOSの角丸背景が二重に適用される
ことはありません。

## Windows成果物

- `src/IwashiScope.App.Wpf/Resources/Icons/IwashiScope.ico`
- `src/IwashiScope.App.Wpf/Resources/Icons/IwashiScope-{size}.png`
- ICOフレーム: 16 / 20 / 24 / 32 / 40 / 48 / 64 / 128 / 256 px
- ICO SHA-256:
  `255393C8A433B47F06658D9DCC095C916193CEA46F9ED4B218D4449239C6FE21`

16 / 32 / 64 / 128 / 256 pxはAsset Catalogの同寸法PNGをそのまま格納します。
20 / 24 / 40 / 48 pxだけ1024 px正本から32-bit ARGB・高品質bicubicで縮小します。
全PNGで四隅のalphaが0であることを生成時に確認します。ICOにはPNG圧縮フレームを
格納し、縮小品質と透過を維持します。

再生成:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Generate-WindowsIcon.ps1
```

WPFプロジェクトの `ApplicationIcon`、`MainWindow.Icon`、モード選択画面の
タイトル上部64×64画像に同じ資産を設定しています。画像は縦横比を維持し、
高品質bitmap scalingを使用します。

## SF Symbols

SwiftUIはモードカード、ボタン、状態表示などにSF Symbolsを使用していますが、
参照cloneにはそれらの画像実体がありません。AppleのSF Symbolsを抽出したり、
Unicode文字やWindows標準アイコンで近似したりしていません。

現段階では、従来の文字近似だったタイトル波形、反射／環境光／発光カード記号、
状態丸、色域警告三角を除去しました。タイトル波形だけはユーザー指定により
IwashiScope実アイコンへ置換しました。ほかのSF Symbols箇所は、将来
ライセンス上利用可能な正規画像を用意するか、アイコンなしを維持するかの
判断待ちです。色域警告は記号を追加せず、該当値の色とHelpTextで状態を保持します。
