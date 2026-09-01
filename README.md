# IwashiScope

*Spectral & Color Measurement*
分光・測色ツール

ArgyllCMS 3.5.0を改変した同梱コマンド`iwashiscope-spotread`を対話操作し、スペクトルと測色・光源評価値を表示する分光・測色アプリです。macOS版はSwiftUI、Windows版はWPFで実装し、同じ改変ソースから各OS用の`iwashiscope-spotread`をビルドします。

> [!IMPORTANT]
> macOS版Version 0.9には、Sparkle.frameworkを見つけられず起動できない問題があります。Version 0.9をダウンロードした場合は、署名・公証済みの[IwashiScope 1.0](https://github.com/Yamonov/IwashiScope/releases/tag/v1.0)を手動でダウンロードして置き換えてください。macOS版・Windows版の配布バイナリに対応する完全なソース一式も、同じReleaseで公開しています。

## 現在の実装

- 起動時に「反射原稿」「環境光」「発光」から測定モードを選択
- 選択したモードで、IwashiScope用JSONプロトコルを追加した`iwashiscope-spotread`を高解像度・スペクトル出力付きで起動
- `iwashiscope-spotread`が標準出力へ送るJSON Linesを解析し、機種に応じたキャリブレーション手順、待機中、完了をGUIに表示
- `state`イベントで待機、測定開始、保存済み測定値の確認を判定し、接続・キャリブレーション・測定・復旧中は操作を遮る大きな円形待機画面を表示
- GUIのボタンからキャリブレーション、続行、任意校正のスキップ、測定、エラー種別に応じた復旧を入力
- `calibration`と`issue`イベントで、飽和、不安定な読み取り、通信切断、センサー位置、校正失敗、一般操作エラー、致命的エラーを区別して案内
- 測定器内の保存済みspot読み取りを検出した場合は、ArgyllCMSが定義する`N`を送って無視し、ライブ測定だけを使用
- 起動・キャリブレーション・測定・復旧に応答期限を設け、停止時は`SIGTERM`後に`SIGKILL`へ移行して同じモードで1回だけ自動再起動
- 自動再起動後も復旧しない場合は再起動ループを止め、GUIの再起動とモード選択を操作可能な状態で表示
- 待機画面から手動で`iwashiscope-spotread`を強制再起動でき、測定結果とデバッグ通信履歴は保持
- 「モード選択へ戻る」またはEscでいつでも測定画面を離れ、`iwashiscope-spotread`を終了（応答しない場合は強制終了）
- 右側パネルの「spotread詳細ログ」タブに、アプリからの入力、spotreadの人間向けログ、プロセスの起動・終了を時系列で表示
- デバッグログはAppKitのテキストビューへ差分追加し、長時間・連続測定でも画面全体を再レイアウトしない
- `iwashiscope-spotread`の細かな出力を100 ms単位でまとめ、UTF-8・CRLF・任意の分割位置・終了直前の未読出力を保持して処理
- アプリが異常終了した場合も独立した監視プロセスが`iwashiscope-spotread`を終了し、孤立プロセスを残さない
- 測定器が返す波長範囲のスペクトルカラーを薄い背景にしたグラフと、ドライバのノイズ対策を反映した実用波長範囲だけに絞る表示オプション
- スペクトルグラフの縦軸は自動または10〜500の10刻みで固定でき、反射原稿は固定100、環境光・発光は自動（固定値200）を初期値として、整数の目盛りと同じ設定を画面表示・PNG書出しへ反映
- 環境光・発光モードでは、D50とD65の基準分光分布を選択して、560 nmで測定値に合わせた比較曲線を重ねて表示
- XYZ、D50 Lab、モノクロY/L*、ピーク、Lux、CCT、Duv、EV、CRI R1〜R15、TLCI、TM-30、および各Caution・算出不能状態を解析して表示
- 全測定モードでD50 Labと範囲自動調整付きa*b*グラフを表示し、反射原稿では測定スペクトルからCIE標準イルミナントC・CIE 1931 2°標準観測者によるマンセル値を算出
- CRI R1〜R14は`iwashiscope-spotread`出力を使用し、R15は測定スペクトルを5 nm間隔へ補間してTCS15からCIE 13.3方式で独自計算
- R1〜R15を試験色に対応した横並びの色付き棒グラフでスペクトル図の下に表示
- 環境光・発光モードでは、CRIとIES TM-30-15をタブで切り替え、TM-30には16色相ビンの色相グラフ、Rf、Rg、大型Rf–Rgプロット、CCT、Duv、99色評価用試料の試料別忠実度棒グラフを表示
- 「spotread詳細ログ」では空白入力を`<SPACE>`として可視化し、送信中・送信済み・送信失敗も区別

R15はArgyllCMS 3.5本来のCRI計算結果には含まれません。アプリ内の計算値であることを画面上に明記しています。

改変版`iwashiscope-spotread`には`-J`を追加しています。標準出力は機械処理専用のJSON Lines、標準エラーは従来の人間向けログです。JSONプロトコルv3では、測定器またはキーボードからのトリガーを受けた時点で`measurementStarted`を通知し、その後、1回分のスペクトル、実用波長範囲、測色値、演色評価値、TM-30の16色相ビンと99色評価用試料を1個のmeasurementイベントとして出力します。測定器本体のスイッチを待つキャリブレーションも、実際のトリガー時点で開始を再通知します。このため表示文言の変更には依存せず、Swift側はプロトコルの版、モード、点数、必須値を検証し、不完全な測定は表示せず解析エラーとしてログに残します。

JSONプロトコルv3の`hello.capabilities`に`spectrumAnalysisV1`がある場合は、起動中の同じプロセスへ平均スペクトルを仮想測定値として渡せます。クライアントは待機中に制御文字`0x1D`だけを送り、`spectrumAnalysisInputReady`を受けてから、4バイトのビッグエンディアン長とUTF-8 JSONを送ります。JSONには`analyzeSpectrum`、要求ID、現在の測定モード、平均に採用した回数、波長範囲、モード別の正規化値、スペクトル値を含めます。正常時は`spectrumAnalysisStarted`に続いて、`source: averagedSpectrum`、要求ID、採用回数を付けた通常の`measurement`イベントを返します。この経路では待機中の装置読み取りを測定せずに中断し、装置接続とキャリブレーション状態を維持したまま、既存の分光計算経路でXYZ・Labを、照明評価が有効なモードではCCT・CRI・TLCI・TM-30も求めます。

TM-30の円形グラフは、ArgyllCMSの`tm3015_plot`と同じく16色相ビン間を補間し、基準光の輪郭を単位半径へ正規化して測定光との差を矢印で描画します。色相エリアは22.5°ずつ均等に分割し、補助色と番号を各エリアの中央へ配置します。CCTとDuvは左上、線の凡例は右下へ重ねて表示します。99本の棒グラフは、各評価用試料のJab色差からTM-30-15の係数7.54で算出した`Rf,CES`を示します。Rf–Rgプロットの①と②は、それぞれプランク軌跡上の光源と実用光源のおおよその範囲であり、適合判定には使用しません。

## CIE標準光源データ

D50とD65の比較曲線は、CIEが公開する1 nm間隔の公式データから380〜730 nmを5 nm間隔で収録し、点間を直線補間しています。

- [CIE標準光源D50](https://www.cie.co.at/datatable/cie-standard-illuminant-d50) — DOI `10.25039/CIE.DS.etgmuqt5`
- [CIE標準光源D65](https://www.cie.co.at/datatable/cie-standard-illuminant-d65) — DOI `10.25039/CIE.DS.hjfjmt59`

これらの原データセットは[CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/)で公開されています。用途の表記は、D50をISO 3664の観察条件、D65をISO 3668の塗料色比較条件として区別しています。曲線の数値はいずれもISO/CIE 11664-2に対応するCIE公式データです。

公式CSVとメタデータ、チェックサム、変換内容は[`ThirdParty/CIE/`](ThirdParty/CIE/)に収録しています。`Scripts/generate-cie-standard-illuminants.swift`でmacOS用Swift配列とWindows用C#配列を同時に再生成でき、`--check`で収録CSVと両生成結果の一致を検証できます。

生成されたSwift・C#データはCC BY-SA 4.0データの適応物です。原データにはCC BY-SA 4.0が引き続き適用され、Yamonovによる適応への寄与はGNU GPLバージョン3のみ（GPL-3.0-only）で提供されます。GPLv3はCreative CommonsがCC BY-SA 4.0の一方向互換ライセンスとして指定しています。これらの適応物は、GPLv3・AGPLv3双方の第13条に基づき、AGPL-3.0-onlyのIwashiScope部分と結合されます。詳細は[`ThirdParty/CIE/README.md`](ThirdParty/CIE/README.md)を参照してください。

## spotread実行ファイル

1. `IWASHISCOPE_SPOTREAD_PATH`環境変数
2. App Bundle内の補助実行ファイルまたはResources

一般のHomebrew版や`PATH`上の上流`spotread`はJSONプロトコルを備えていないため使用しません。通常のXcodeビルドでは`Scripts/build-spotread.sh`が必要な依存関係だけをビルドし、生成した`iwashiscope-spotread`をApp Bundleへ格納します。

測定器番号は既定で`-c 1`です。変更する場合は`IWASHISCOPE_INSTRUMENT_INDEX`を指定します。

| モード | 引数 |
| --- | --- |
| 反射原稿 | `-J -v -s -H -c N` |
| 環境光 | `-J -v -s -H -a -c N` |
| 発光 | `-J -v -s -H -e -T -c N` |

環境光モードでは、ArgyllCMS 3.5が演色情報を自動的に有効化するため`-T`を付けません。付けると逆に出力が抑制されます。

アプリからの起動時は`ARGYLL_NOT_INTERACTIVE=1`を設定し、パイプ経由の入力と出力フラッシュをspotreadへ明示します。

## プロジェクト構成

```text
IwashiScope/
├── Argyll_V3.5.0/                    # 同梱するArgyllCMSソースとIwashiScope用の最小改変
├── Scripts/build-spotread.sh         # 改変版測定コマンドだけをビルドするJamターゲット
├── Scripts/build-spotread-windows.ps1 # 同じ改変ソースからWindows x64 helperをビルド
├── IwashiScope.xcodeproj              # App shell
├── IwashiScope/                       # App entry point / assets
├── IwashiScopePackage/
│   └── Sources/IwashiScopeFeature/    # macOS UI、プロセス制御、パーサー
└── Windows/                            # WPFアプリ、テスト、Windowsリリーススクリプト
```

macOS版のローカルテストは公開リポジトリへ含めていません。`Package.swift`はローカルにテストが存在する場合だけテストターゲットを追加します。Windows版の自動テストとUI対応表は`Windows/`以下へ収録しています。

## ビルド

macOS版に必要な環境と手順は[BUILDING.md](BUILDING.md)を参照してください。Xcodeでは`IwashiScope.xcworkspace`を開きます。初回ビルド時にSparkle 2.9.4を取得し、`Scripts/build-spotread.sh`が同梱ソースからUniversal Binaryの`iwashiscope-spotread`を作成します。

Windows版は.NET 10、WPF、WinSparkle 0.9.4を使用します。ビルド、テスト、自己完結型x64 ZIP、更新機能の構成は[Windows/README.md](Windows/README.md)を参照してください。

## Sandboxと配布

macOS版は、測定器へのアクセスと補助プロセス実行のためApp Sandboxを有効にしていません。配布物には、同じソースからビルドして署名した改変版`iwashiscope-spotread`をApp Bundle内の補助実行ファイルとして同梱します。Windows版には、同じコミットからWindows x64用にビルドした`iwashiscope-spotread.exe`をアプリと同じフォルダーへ同梱します。

ArgyllCMSソースとIwashiScope側の改変ソースを同じリポジトリで提供し、生成したオブジェクト、静的ライブラリ、`iwashiscope-spotread`バイナリはコミット対象から除外します。再配布時はArgyllCMSのライセンス文書とAGPLv3の条件を確認してください。

## ライセンスとソースコード

Copyright © 2026 Murakami Yoshiteru

IwashiScopeのSwift・C#アプリと改変版`iwashiscope-spotread`は、第三者著作物に個別の表示がある部分を除き、[GNU Affero General Public Licenseバージョン3（AGPL-3.0-only）](LICENSE)で公開します。CIE標準光源データから生成したSwift・C#適応物には[GNU GPLバージョン3のみ（GPL-3.0-only）](LICENSES/GPL-3.0-only.txt)が適用され、AGPL-3.0-only部分とは双方の第13条に基づいて結合されます。このソフトウェアには一切の保証がありません。

- ArgyllCMS 3.5.0の原著作権と個別ライセンスは維持されています。
- IwashiScopeによるArgyllCMSの改変内容は[ARGYLL_CHANGES.md](ARGYLL_CHANGES.md)に記録しています。
- Sparkle、WinSparkle、CIE標準光源データ、ArgyllCMS内の各構成要素は[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)を参照してください。
- バイナリの作成・署名・公証・Sparkle配信は[RELEASING.md](RELEASING.md)を参照してください。
- 更新確認を含む通信とデータの扱いは[PRIVACY.md](PRIVACY.md)を参照してください。
- 名称とロゴの扱いは[TRADEMARKS.md](TRADEMARKS.md)を参照してください。

IwashiScopeおよび改変版`iwashiscope-spotread`のソースは、このリポジトリの各リリースタグから取得できます。
