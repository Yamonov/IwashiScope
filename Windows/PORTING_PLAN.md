# IwashiScope Windows 全機能移植計画

基準: `v0.9.4` / `343405721f1d1a5184efd32c24054259845d608c`

## 1. 方針

正式選定は **.NET 10 + WPF、Windows x64** である。macOS側とのコード共通化は
行わず、macOS v0.9.4に実装された全機能の再現を完了条件とする。

理由:

- 現環境に .NET 10 SDK、WPF テンプレート、Managed Desktop ワークロードが揃っている。
- Windows 専用アプリとして、プロセス・匿名パイプ・Explorer drag-and-drop・キーボード操作・ファイルダイアログ・高密度カスタム描画を直接扱いやすい。
- Mac 版の Swift Charts / AppKit bridge を、WPF custom control、DrawingVisual、WriteableBitmap / RenderTargetBitmap、virtualized ItemsControl へ対応付けやすい。
- WinUI 3 の Windows App SDK ランタイム構成を追加せず、最初の再現性と配布の単純さを優先できる。
- `.NET 8` は現在のマシンに SDK がなく、サポート終期も近いため、新規ポートの基準にはしない。

UI scaffold、Core/Protocol/Infrastructure、実機helper、自動テストの実装を開始済み。
機能単位の実装・検証・実機待ち・未実装の最新状態は
[`MAC_PARITY_CHECKLIST.md`](MAC_PARITY_CHECKLIST.md)を正とする。

## 2. UI 技術候補の比較

| 評価軸 | .NET 10 + WPF | .NET 10 + WinUI 3 | .NET 10 + Avalonia |
|---|---|---|---|
| 現環境 | SDK・workload・template 導入済み | Windows App SDK C# component 未導入 | template / package 未導入 |
| Windows ネイティブ統合 | 強い | 最も新しい Windows UI | 良好だが抽象化層あり |
| カスタムチャート | DrawingVisual 等で実装可能 | Win2D / composition 等の選定が必要 | Skia 系描画と相性がよい |
| 3000px PNG | RenderTargetBitmap または専用 renderer | 専用描画経路が必要 | Skia renderer が有力 |
| CSV / ASE | UI 非依存の .NET 実装 | 同左 | 同左 |
| Explorer D&D | 成熟している | 対応可能 | 対応可能、platform差の検証が必要 |
| ja/en localization | resx / satellite assembly | PRI / resource 管理 | resx 等 |
| 大容量ログ | TextBox 依存を避け custom/virtualized 表示可 | custom control が必要 | custom/virtualized 表示可 |
| 配布 | self-contained exe/dir、MSIX/WiX へ拡張可 | framework-dependent / self-contained と MSIX 設計がやや複雑 | self-contained / Native AOT も候補 |
| 将来の他 OS | Windows 限定 | Windows 限定 | 強い |
| 主なリスク | UI が Fluent 風ではない | 追加 toolchain、runtime、実装量 | 第三者 framework、ネイティブ挙動差 |

判断:

- **Windows 版を忠実かつ低リスクで完成させる:** WPF
- 将来、Mac/Linux も同一 .NET UI に統合する戦略が確定した場合: Avalonia を再評価
- Windows 11 の最新 UI 表現を最優先し、追加実装・配布複雑性を受け入れる場合: WinUI 3

## 3. 絶対に維持する契約

### spotread 実行契約

- ファイル名: `iwashiscope-spotread.exe`
- JSON mode option: `-J`
- JSON Lines `protocolVersion: 3`
- 最初の有効 event は hello
- hello:
  - `implementation == "IwashiScope spot reader"`
  - `implementationVersion == 1`
  - `argyllVersion == "3.5.0"`
- event: hello / instrument / state / calibration / issue / measurement
- stdout は JSON Lines 専用
- 人間向け出力と診断は stderr
- Windows では `_dup` / `_dup2` による既存 stdout の stderr 転送を維持
- mode:
  - reflectance: `-J -v -s -H -c N`
  - ambient: `-J -v -s -H -a -c N`
  - emissive: `-J -v -s -H -e -T -c N`
- 測定コマンド: space
- calibration: `k`
- calibration skip: `S`
- saved reading 無視: `N`

### measurement 契約

- mode、timestamp、instrument metadata
- spectrum と実用波長範囲
- XYZ / Lab
- monochrome indicator
- Lux / CCT / Duv / EV
- CRI R1–R14 とアプリ計算 R15
- TLCI
- TM-30 Rf / Rg、16 hue bins、99 color evaluation samples
- Planckian / daylight reference information
- warning / issue と測定結果を混同しない

## 4. 推奨アーキテクチャ

次の責務分離でsolutionを作成済み。

```text
IwashiScope.Core
  domain models, calculations, history, workspace, export models
IwashiScope.Protocol
  JSONL DTO, streaming parser, contract validation, fixtures
IwashiScope.Infrastructure.Windows
  process, pipes, Job Object, files, color management, executable locator
IwashiScope.App.Wpf
  state projection, views, controls, dialogs, drag-and-drop, localization
IwashiScope.Tests
  unit, protocol, golden export, state-machine, recovery tests
IwashiScope.HardwareTests
  opt-in instrument tests
```

Core / Protocol / exporter は UI 非依存にする。画面から JSON DTO、Process、ファイル形式へ直接依存させない。

## 5. 作業単位、依存関係、完了条件

### P0. 基準固定・ビルド再現性・ライセンス

依存: なし

作業:

- upstream tag / commit / annotated tag 状態を記録
- Argyll-Jam の取得元、archive hash、ビルド方法を固定
- `IWASHISCOPE_ONLY=true` の x64 build script 化
- `iwashiscope-spotread.exe` の hash / PE / dependencies を記録
- AGPL-3.0-only、Argyll 各原著ライセンス、GPL-2.0-or-later、GPL-3.0-only、CC BY-SA 4.0、第三者通知を分類
- CIE D50/D65 原データと生成物の attribution / ShareAlike 条件を保持

完了条件:

- clean reference source から 1 コマンドで x64 executable を再生成できる
- source modification と generated artifact を明確に分離できる
- `LICENSE`、Argyll `License*.txt`、`THIRD_PARTY_NOTICES` と binary distribution notice の包含試験がある
- ライセンスを削除・緩和せず、相互作用する GPL/AGPL 部分の配布条件がレビュー済み

### P1. ドメインモデルと JSON Lines v3

依存: P0

作業:

- Swift の MeasurementMode / SpotMeasurement / SpotreadIssue / SpotreadInteraction を C# immutable model へ移植
- UTF-8 arbitrary chunk を受ける streaming line decoder
- 不完全 UTF-8 sequence と最終 drain
- hello-first / hello-once / exact version / implementation validation
- 全 event DTO と semantic validation
- unknown event / unknown field は forward compatibility 方針を定義
- malformed JSON、mode mismatch、配列長、数値範囲、NaN/Infinity を分類
- stderr はログへ、stdout の非 JSON は protocol violation として扱う
- 100 ms batch 相当の UI 更新抑制を protocol reader と UI dispatcher の境界に置く

完了条件:

- Mac 版 fixture と probe 出力をすべて parse
- 1 byte ごとに分割した UTF-8、複数行一括、末尾改行なしを同じ結果として処理
- hello 欠落・重複・version/ID 不一致が fatal configuration issue になる
- stdout の人間向け混入を検出し、stderr ログと混在させない
- measurement の CRI 14、TM-30 16 / 99、XYZ/Lab 3、spectrum range を厳格に検証

### P2. Windows プロセス管理とログ

依存: P1

作業:

- executable locator: app-local `iwashiscope-spotread.exe` を最優先し、開発用 override を限定
- `ProcessStartInfo` で shell を介さず引数配列・working directory・UTF-8 pipe を設定
- stdin / stdout / stderr の独立した非同期 drain
- Windows Job Object の `KILL_ON_JOB_CLOSE` で親終了時に子を確実に終了
- cooperative stop → 短い猶予 → process tree kill
- stdin command の pending / sent / failed を記録
- launch、exit code、stderr、protocol violation、timeout を時系列ログ化
- ログ上限 2,000,000 characters、超過時 1,600,000 characters へ trim
- UI は全文再代入せず incremental append / virtualization
- 機密情報や任意パスを過剰にログへ出さない

完了条件:

- stdout/stderr deadlock なしで大量出力を処理
- app crash / normal exit / user stop の各経路で orphan process が残らない
- 実行ファイル未配置・起動拒否・早期 exit・broken pipe がユーザー向け issue になる
- `<SPACE>` を含む入力ログと lifecycle log が Mac 版相当
- ログ trim 後も末尾の診断情報を保持

### P3. セッション状態機械と異常復旧

依存: P1, P2

状態:

- idle
- launching
- calibrationRecommended
- awaitingCalibrationSetup
- waitingForInstrument
- calibrating
- ready
- measuring
- retryAvailable
- configurationRequired
- recovering
- workspace
- stopped
- failed

作業:

- reducer / explicit transition table と side effect を分離
- launch 30s、calibration 90s、measurement 60s、recovery 15s
- 一度だけ自動 recovery restart、その後は loop を止め manual restart / mode switch を表示
- restart 時に mode、history、selection、log を保持
- stale process event を generation ID で拒否
- calibration setup、retry、saved-reading prompt を UI action へ対応
- stop と mode change の競合を cancellation token で整理

完了条件:

- すべての state / event / timeout / command の表形式テストがある
- 二重起動、二重測定、stale measurement、restart loop が発生しない
- 一度目の回復と二度目の停止を deterministic に再現
- process exit と protocol fatal の双方から安全に次の状態へ遷移
- 記録済みJSONL fixtureと状態遷移テストで実機非依存部分を再現できる

### P4. 色・照明計算

依存: P1

作業:

- R15 を CIE 13.3:1995 相当の既存ロジックで移植
  - 400–700 nm coverage
  - 5 nm interpolation
  - CCT 1–25000 K
  - 5000 K 未満 Planckian、5000 K 以上 daylight
- Lab → display RGB
  - D50 Lab 基準
  - sRGB / Adobe RGB
  - Mac ColorSync 依存を deterministic C# color math または Windows ICC path に置換
  - gamut clipping を明示
- TM-30:
  - sample fidelity scaling 7.54
  - softplus transform
  - 0–100 clamp
  - 16 hue-bin vector interpolation、reference radius normalization、test/reference ratio
- printing/viewing condition:
  - JSPST-1998
  - ISO 3664:2025
- D50 / D65 overlay を 560 nm 正規化
- Rf-Rg guide range は評価の目安であり pass/fail と表示しない

完了条件:

- Swift 実装と同じ fixtures で許容誤差内
- R15、TM-30、Lab変換の境界・欠損・異常値テスト
- 400–700 nm を満たさない spectrum では計算不能理由を保持
- PNG と画面の色変換が同じ core implementation を使用
- 数値 formatting と calculation を分離し、locale で計算結果が変わらない

### P5. 履歴と workspace

依存: P1, P3

作業:

- acquisition order を immutable source order として保持
- mode ごとの presentation order
- selection set / active item / anchor
- exclusive、toggle、range、select all
- multi-select reorder、delete、rename
- `.iwashiscope` JSON formatVersion 1
- ISO 8601、pretty / sorted key output の互換性を評価
- duplicate ID、mode mismatch、order/selection 不整合を strict validation
- 保存済み workspace を機器接続なしで閲覧
- 未保存変更の close / open / new confirmation
- atomic save: temp write → replace

完了条件:

- Mac 版 workspace fixture を読み込める
- Windows 出力を Mac 版が読めることを相互試験
- rename / reorder / delete / selection の property-based または網羅的 reducer test
- 破損ファイルを部分適用せず、元 workspace を保持してエラー表示
- 保存失敗で既存ファイルを破損しない

### P6. UI とチャート

依存: P3, P4, P5

作業:

- mode selection
- status / calibration / retry / manual restart
- busy overlay と操作抑止
- reflectance:
  - spectrum chart
  - practical range toggle
  - hover / keyboard values
  - D50 / D65 overlay
  - details
- lighting:
  - spectrum / metrics
  - CRI R1–R15 bars
  - TM-30 tabs
  - vector graphic
  - Rf-Rg view
  - 99 sample bars
  - printing/viewing condition evaluation
- history cards / selection / rename / reorder
- debug log panel
- DPI 100–250%、light/dark、window resize
- keyboard focus、screen reader names、色以外の状態表現

実装原則:

- 画面チャートと PNG exporter は同じ chart scene model を共有
- 大量 point / log で UI thread を塞がない
- view model は state-machine projection であり、プロセスを直接操作しない

完了条件:

- Mac 版各画面の機能対応表が全項目 pass
- spectrum / CRI / TM-30 の golden image diff が許容範囲内
- 100/150/200/250% DPI で欠け・重なりなし
- keyboard だけで mode、測定、履歴、rename、export、log に到達
- 記録済みJSONL fixtureで全 warning / recovery / calibration UI を検証可能

### P7. Export と drag-and-drop

依存: P4, P5, P6 の chart scene model

作業:

- CSV:
  - spectrum / lighting metadata
  - spectrum
  - CRI
  - TM-30 16 bins / 99 samples
  - invariant number format
  - CRLF
- ASE:
  - big-endian `ASEF`
  - Lab spot swatches
  - combined reflectance palette
- PNG:
  - 3000px
  - spectrum / CRI / TM-30
  - sRGB
  - opaque white background
- Explorer drag:
  - reflectance は ASE
  - lighting は複数ファイルを含む一時フォルダ
  - delayed rendering または安全な一時ファイル lifetime
- internal drag:
  - custom payload
  - selected-set reorder
- Windows filename sanitation:
  - `\ / : * ? " < > |`
  - `CON`, `PRN`, `AUX`, `NUL`, `COM1`…、`LPT1`…
  - 末尾 dot / space
  - case-insensitive collision
  - path length

完了条件:

- Mac 版 fixture と CSV / ASE の byte または semantic equivalence
- ASE を既知 reader で再読込し、swatch name / Lab を照合
- PNG が 3000px、sRGB、alphaなし、white background
- Explorer への drop 後も consumer が読み終えるまで一時ファイルを保持
- 同名測定の export が既存ファイルを無断上書きしない

### P8. 日英表示・アクセシビリティ

依存: P3, P5, P6, P7

作業:

- `Localizable.xcstrings` の 430 entries を resource key inventory にする
- source language ja と全 430 English translations を resx へ移す
- view 内 hard-coded text の検出 test
- culture-specific date/time と user-facing number display
- protocol / CSV / workspace は invariant culture
- runtime language change の要否をユーザー確認
- automation properties、focus order、minimum contrast、reduced motion

完了条件:

- ja-JP / en-US で全主要フロー screenshot review
- resource key 漏れ・未翻訳 key を CI で検出
- OS language fallback が定義済み
- JSON/CSV の decimal separator が OS locale に影響されない
- screen reader と keyboard の smoke test が完了

### P9. 配布・更新・セキュリティ

依存: P0–P8

初期推奨:

- `win-x64` self-contained publish
- app と `iwashiscope-spotread.exe` を同一の署名・versioned payload に含める
- zip または installer は後段で選定
- MSIX は driver / app-local child executable / file association 要件を検証後に判断

作業:

- single-file 化は native child executable と notice の展開挙動を検証してから判断
- SmartScreen 対策として code signing をリリース工程に追加
- executable hash / version / hello を起動時に照合
- DLL search path、working directory、untrusted override path を制限
- workspace / export file association
- crash log と privacy 方針
- third-party notice 画面 / package 内 notice

完了条件:

- clean Windows x64 VM で driver 不要機器または no-device 起動が可能
- framework 未導入環境で self-contained package が動作
- executable 差し替え・protocol mismatch を明示的に拒否
- uninstall でユーザー workspace/history を無断削除しない
- package に全ライセンスと source offer / source location 情報を含む

### P10. 実機統合とリリースゲート

依存: P0–P9

機器マトリクス:

- HID、Argyll libusb0.sys が必要な機器
- reflectance、ambient、emissive
- calibration required / optional / failed
- 一台、複数台、抜去、再接続

試験:

- Mac 版と Windows 版で同じ機器・試料を測定
- protocol event sequence と数値の比較
- spectrum practical range
- XYZ / Lab / Lux / CCT / Duv / EV
- CRI / TLCI / TM-30
- 連続測定、キャンセル、timeout、app 強制終了
- sleep / resume、USB re-enumeration
- driver install / uninstall
- workspace / export の往復

完了条件:

- 指定した機器マトリクスで blocking defect なし
- Mac 版との差が許容誤差または意図した Windows 差として記録済み
- orphan process、stdout contamination、データ破損なし
- licensing / notice / source availability review 完了
- user acceptance 後にのみ release artifact を作成

## 6. テスト資産

最初に UI より先に次を固定する。

- JSONL probe の 6 event fixture
- malformed / partial UTF-8 / unknown event fixture
- Mac 版実測 session の匿名化 fixture
- state-machine transition table
- workspace v1 valid / invalid corpus
- R15 / TM-30 / Lab reference vectors
- CSV / ASE golden files
- spectrum / CRI / TM-30 golden PNG
- filename sanitation table
- Windows匿名パイプのBOMなしUTF-8 / CRLF command framing
- 記録済みJSONL transcript:
  - normal / slow hello / no hello / bad version
  - stderr flood / partial JSON
  - calibration loop / measurement timeout / crash and recover

## 7. 現時点の実機依存境界

無実機で完了可能:

- JSONL schema / parser
- state-machine reducer
- process pipe / Job Object / timeout
- calculation fixtures
- history / workspace
- rendering / export
- localization
- recovery simulation

実機が必要:

- USB driver と enumeration
- calibration interaction
- actual event timing
- measurement accuracy
- practical wavelength range
- physical disconnect / reconnect
- device-specific issue mapping

UIの残りは記録済みfixtureで進め、接続・校正・測定・抜去は実機suiteとして検証する。

## 8. 最終 Definition of Done

Windows 版の完了は「画面が似ている」ではなく、次をすべて満たすこととする。

1. Mac 版の全 measurement mode と全出力指標を保持
2. JSON Lines v3 と spotread lifecycle を厳密に保持
3. calibration、timeout、再起動、機器抜去から安全に復旧
4. 履歴・選択・rename・reorder・workspace の互換性を保持
5. CSV / ASE / 3000px PNG / Explorer D&D を再現
6. spectrum / CRI / TM-30 / viewing condition の計算と表示を照合
7. ja/en、keyboard、accessibility、DPI を検証
8. stdout/stderr 分離と orphan-process 防止を検証
9. AGPL/GPL/CC BY-SA と第三者通知を配布物に保持
10. 実機マトリクスと clean-machine package test を通過

### 8.1 2026-07-29時点の状態

- **実装済み・自動試験済み:** protocol/model/process、主要計算、履歴・選択・
  workspace、CSV/ASE/PNG、ファイル名、per-mode UI profile、TM-30 responsive
  layout、Mac正本由来の9サイズWindows ICO、Windows pipeのBOMなしUTF-8 /
  CRLF framing。Release buildは警告0・エラー0、48/48 tests pass。
- **過去のUI fixtureで目視済み:** 3モード表示、各モード履歴1件、CRI既定タブ、
  Adobe RGB非表示、環境光でsRGB非表示、反射/発光でsRGB表示、TM-30の
  860×620/標準/最大化 containment、アプリアイコン、XAML起動。
- **実装済みだが試験不足:** 全issue/calibration画面、IMEを含む名前編集の全遷移、
  Explorerへの実ドラッグ、ja/en全固定文言、PNG pixel-level Mac比較、
  ライセンス画面操作。
- **実機検証済み:** ColorMunkiのArgyll libusb0 driver、列挙、instrument identity、
  反射モード校正、反射実測1件（380–730 nm / 106点 / 実用420–730 nm）。
- **実機検証中:** 環境光・発光実測、抜去・再接続、sleep/resume、測定精度比較。
- **削除済み:** fake helper、fake runtime切替、`IWASHISCOPE_USE_FAKE`、製品コード内の
  ダミー測定値生成。アプリは実機helperだけを起動する。
- **未実装:** Windows更新方式、installer/署名/clean-machine release検証。
  これらを完了扱いにしない。
- **表示上の明示的例外:** Adobe RGBは計算コードが残っていても全モードの通常UIで
  非表示。画像実体のないSF Symbolsは近似せず未表示。タイトル波形だけはユーザー
  指定によりMacのIwashiScope実アイコンへ置換。

詳細な機能/試験区分は `MAC_PARITY_CHECKLIST.md`、画面条件は
`UI_PARITY_MATRIX.md`、実測証拠は `UI_PARITY_DIFF.md`、アイコン生成は
`ICON_ASSET_REPORT.md` を正とする。

## 9. 技術選定の公式参照

- [ArgyllCMS: Compiling the Source Code](https://www.argyllcms.com/doc/Compiling.html)
- [ArgyllCMS: Installing on Microsoft Windows](https://www.argyllcms.com/doc/Installing_MSWindows.html)
- [WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)
- [WPF drag-and-drop overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/drag-and-drop-overview)
- [WPF globalization and localization](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-globalization-and-localization-overview)
- [Windows App SDK deployment overview](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/deploy-overview)
- [Avalonia supported platforms](https://docs.avaloniaui.net/docs/supported-platforms)
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
