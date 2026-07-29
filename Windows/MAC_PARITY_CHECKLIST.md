# macOS v0.9.4 → Windows 機能・テスト対応表

基準: `v0.9.4` / `343405721f1d1a5184efd32c24054259845d608c`
更新日: 2026-07-29

状態は実装と検証を混同しない。

- 実装: `済` / `一部` / `未`
- 検証: `自動` / `目視` / `未試験` / `実機待ち`
- `自動+目視`は両方を完了した項目

## A. 公開README「現在の実装」の全項目

| # | macOS v0.9.4の機能 | Windows実装 | 検証 | 備考 |
|---:|---|---|---|---|
| 1 | 反射原稿・環境光・発光のモード選択 | 済 | 自動+目視 | macOS同様の中央選択画面。通常workspaceに常設sidebarなし |
| 2 | JSON対応helperを高解像度・スペクトル付きで起動 | 済 | 自動 | 3モード引数テスト、native helper build済み |
| 3 | JSONLから校正・待機・完了をGUI表示 | 済 | 自動+目視 | fake測定のready/measurementを目視、校正panelは未目視 |
| 4 | state判定と接続・校正・測定・復旧中のblocking表示 | 済 | 未試験 | WPF全画面overlayを実装 |
| 5 | 校正・続行・任意校正skip・測定・復旧入力 | 済 | 自動 | 校正UI操作の目視は未実施 |
| 6 | 飽和・不安定・切断・sensor位置・校正・一般・fatalの区別 | 済 | 一部自動 | parser分類済み、全issueのUI fixtureは未試験 |
| 7 | 保存済みspotへ`N`を一度だけ送りlive測定のみ使用 | 済 | 未試験 | generation単位の送信guardあり、専用fake testを追加予定 |
| 8 | 30/90/60/15秒期限、正常停止後の強制終了、自動再起動1回 | 済 | 一部自動 | measurement timeout→1回再起動→停止は自動試験済み |
| 9 | 再起動loop停止後に手動再起動・mode選択を有効化 | 済 | 自動 | recovery integrationでloop停止を検証 |
| 10 | 手動強制再起動で結果・通信履歴を保持 | 済 | 未試験 | controller実装済み |
| 11 | 戻る/Escでhelper終了、無応答時強制終了 | 済 | 目視 | Esc/終了とorphanなしを目視 |
| 12 | 詳細ログへ入力・人間向けstderr・process lifecycleを時系列表示 | 済 | 自動+目視 | stdout JSONとstderr humanを分離 |
| 13 | ログ差分追加で全画面再layoutを回避 | 済 | 目視 | WPF TextBoxへAppendText |
| 14 | 100 ms batching、UTF-8/CRLF/任意分割/終了直前drain | 済 | 自動 | byte単位UTF-8分割とEOF drain |
| 15 | app異常終了時もhelperを孤立させない | 済 | 目視 | Job Object、UI終了後のfake orphanなし |
| 16 | 薄いスペクトルカラー背景と実用波長範囲切替 | 済 | 未試験 | 背景gradientとsquare plotを共通rendererへ実装 |
| 17 | D50/D65公式曲線を560 nm正規化して重ねる | 済 | 自動 | 560 nmを含まない場合は非表示 |
| 18 | XYZ/Lab/monochrome/peak/Lux/CCT/Duv/EV/CRI/TLCI/TM-30/issue | 済 | 自動+目視 | fake ambientで主要値を目視 |
| 19 | R1–R14はhelper、R15は5 nm/TCS15/CIE 13.3アプリ計算 | 済 | 自動 | Argyll由来reference test。Windows独自のapp-calculated表示は通常UIから除去 |
| 20 | R1–R15を試験色対応色の棒グラフで表示 | 済 | 自動+目視 | Mac palette、Ra bracket、負値label配置を実装 |
| 21 | TM-30 16 hue/Rf/Rg/Rf–Rg/CCT/Duv/99 sample | 済 | 自動+目視 | 4点補間・正規化・16矢印・99 sample計算。860×620、標準、最大化で枠内を確認 |
| 22 | logで`<SPACE>`、送信中/済/失敗を区別 | 済 | 自動+目視 | fake測定logで確認 |

README本文のJSONL v3契約（hello、trigger時measurementStarted、1 measurement eventの
spectrum/実用範囲/色・演色値、mode/配列数/必須値検証）もProtocol/Controllerへ実装済み。
TM-30本文の4点補間、基準輪郭の単位半径化、16 sector、変位矢印、CCT/Duv、99試料の
7.54係数も実装済みで、geometryとsample fidelityを自動試験している。

## B. 追加監査の機能チェックリスト

| 領域 | 完了条件 | Windows実装 | 検証 |
|---|---|---|---|
| workspace | `.iwashiscope` formatVersion 1、全mode履歴・presentation order・selection・active・anchor・name・instrument identity・selected mode・右tab | 済 | 自動 |
| workspace | 破損・duplicate・unknown version・order/selection整合性を部分適用せず拒否 | 済 | 一部自動 |
| workspace | 未保存確認、保存データ閲覧中はhelper未起動、後から接続 | 済 | 未試験 |
| reference | CIE公式D50/D65、560 nm正規化、560 nm非包含時は非表示 | 済 | 自動 |
| color | sRGB HEXとsRGB gamut warning。Adobe RGBはユーザー確認により全モード非表示 | 済 | 自動+目視 |
| standards | JSPST-1998、ISO 3664:2025の測定可能な数値項目、P3/P4、完全適合ではない説明 | 済 | 自動+目視 |
| CRI | R15 app計算、R1–R8 Ra範囲、負値を含むlabel配置 | 済 | 一部自動 |
| TM-30 | 16 bins、4点補間normalized contour/arrows、CCT/Duv、Rf/Rg、fixed Rf–Rg、99 sample/7.54 | 済 | 自動+目視。最小860×620相当でoverflowなし |
| history footer | 初期/折畳/展開/drag、最大90%、領域適応、keyboard操作 | 済 | geometry自動、UI目視 |
| name edit | double-click/直接編集、IME marked textを壊さないEnter/Tab/Shift+Tab、前後wrap、外側click、presentation順 | 済 | 未試験 |
| selection | Ctrl click/Shift範囲/Ctrl+A/Ctrl+D/Delete、mode間移動禁止 | 済 | core自動、WPF標準selectionは目視未完 |
| reorder | 複数カード順維持、先頭/末尾/折返し、presentation順 | 済 | 自動 |
| drag | reflectance選択を1 ASE、lightingをSpectrum/CRI/TM-30 PNG等、internal reorderとexternal file drag | 済 | 未試験 |
| PNG | 幅3,000 px、sRGB chunk、Bgr24/alphaなし、実用範囲、square plot、D50/D65凡例 | 済 | 構造自動、実用範囲/凡例の画像差分は未試験 |
| CSV | CRLF、安定列、lighting spectrum/CRI/TLCI/TM-30 section | 済 | 自動 |
| naming | 並べ替え後の全履歴位置、最低3桁、Unicode、禁止文字・予約名・末尾dot/space・重複回避 | 済 | 自動 |
| ASE | big-endian Lab spot、reflectance選択を単一palette、単一/複数名 | 済 | 自動 |
| process | 100 ms、UTF-8分割/CRLF/drain、stdout JSON・stderr human | 済 | 自動 |
| process | saved promptへ`N`一度、30/90/60/15秒、正常終了→kill、自動再起動1回、manual restart保持 | 済 | 一部自動 |
| process | Job Objectで親終了時に子を残さない | 済 | 目視 |
| log | `<SPACE>`、pending/sent/failed、差分、follow toggle、clear、時系列 | 済 | 目視 |
| localization | runtime ja/enと430-entry inventory | 一部 | 目視 | 主要VM文言は切替、XAML固定文言の完全resource化は未 |
| license | ライセンス/ソース表示、AGPL/GPL/CC BY-SA notice同梱 | 済 | build output確認済み、UI操作は未試験 |
| icon | `IwashiScope.icon`/AppIconを正本に16/20/24/32/40/48/64/128/256 ICO、EXE/Window/title画像 | 済 | 自動+目視 |
| update | Sparkle相当のWindows更新方式 | 未 | 未試験 | 方式決定まで明示的な未実装。完了扱いしない |
| packaging | self-contained/installer、署名、clean-machine、notice/source offer | 未 | 未試験 | releaseは今回の範囲外 |

## C. macOS内部テスト観点とWindowsテスト

公開タグには `IwashiScopePackage/Tests/IwashiScopeFeatureTests` の実体が含まれず、
`README.md`も「開発に使用するテストと内部資料は公開リポジトリへ含めていません」と明記する。
したがって未知のテスト名を転記済みとは主張しない。Mac側から委譲されたテスト観点を、
次の実動作テストへ対応付けた。

| Mac側監査観点 | Windowsテスト |
|---|---|
| JSONL hello/version/mode/UTF-8/CRLF/分割/drain | `ProtocolTests` |
| fake process stdout/stderr/input/exit | `ProcessIntegrationTests` |
| timeoutと同mode自動再起動1回 | `RecoveryIntegrationTests` |
| selection geometry、mode local、multi-reorder、footer geometry | `HistoryAndLayoutTests` |
| workspace nesting/casing/identity/tab/duplicate/unknown version | `CoreFeatureTests.WorkspaceRoundTripsAndRejectsDuplicateIds` |
| R15、TM-30 7.54、normalized/interpolated contour、色空間独立gamut | `CoreFeatureTests` |
| CSV CRLF/section、ASE endian/Lab | `CoreFeatureTests` |
| PNG 3000×1500（logical 1000×500、3× raster）/Bgr24/sRGB、560 nm不在時のreference非表示 | `PngExportTests` |
| full-order連番、Unicode、reserved/duplicate、単一/複数ASE名 | `ExportFileNameTests` |

現行自動テストは44件。存在確認だけでなく、JSON round-trip、PNG chunk/pixel format、
process pipe、selection/reorder geometry、TM-30 contourの数値構造を検証する。

## D. 実機待ち

- ColorMunkiのArgyll用USB driver導入、列挙、校正、反射/環境光/発光の実測
- 実測の実用波長範囲、XYZ/Lab/Lux/CCT/Duv/EV/CRI/TLCI/TM-30精度
- USB抜去・再接続、複数機器、sleep/resume、device-specific issue
- Mac/Windows間で同一機器・試料を使う数値比較

driver変更は未実施。承認境界と復旧方法は `DRIVER_PLAN.md` に保持する。
