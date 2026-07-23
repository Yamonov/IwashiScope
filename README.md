# IwashiScope

*Spectral & Color Measurement*
分光・測色ツール

macOS上で同梱版ArgyllCMS `spotread`を対話操作し、スペクトルと測色・光源評価値を表示するSwiftUIアプリです。

> [!IMPORTANT]
> 現在は初回リリース前のソース公開版です。公開されているソースからアプリをビルドできますが、署名・公証済みバイナリはまだ配布していません。

## 現在の実装

- 起動時に「反射原稿」「環境光」「発光」から測定モードを選択
- 選択したモードで、IwashiScope用JSONプロトコルを追加したArgyllCMS 3.5.0 `spotread`を高解像度・スペクトル出力付きで起動
- `spotread`が標準出力へ送るJSON Linesを解析し、機種に応じたキャリブレーション手順、待機中、完了をGUIに表示
- `state`イベントで待機、測定開始、保存済み測定値の確認を判定し、接続・キャリブレーション・測定・復旧中は操作を遮る大きな円形待機画面を表示
- GUIのボタンからキャリブレーション、続行、任意校正のスキップ、測定、エラー種別に応じた復旧を入力
- `calibration`と`issue`イベントで、飽和、不安定な読み取り、通信切断、センサー位置、校正失敗、一般操作エラー、致命的エラーを区別して案内
- 測定器内の保存済みspot読み取りを検出した場合は、ArgyllCMSが定義する`N`を送って無視し、ライブ測定だけを使用
- 起動・キャリブレーション・測定・復旧に応答期限を設け、停止時は`SIGTERM`後に`SIGKILL`へ移行して同じモードで1回だけ自動再起動
- 自動再起動後も復旧しない場合は再起動ループを止め、GUIの再起動とモード選択を操作可能な状態で表示
- 待機画面から手動で`spotread`を強制再起動でき、測定結果とデバッグ通信履歴は保持
- 「モード選択へ戻る」またはEscでいつでも測定画面を離れ、spotreadを終了（応答しない場合は強制終了）
- 右側パネルの「spotread詳細ログ」タブに、アプリからの入力、spotreadの人間向けログ、プロセスの起動・終了を時系列で表示
- デバッグログはAppKitのテキストビューへ差分追加し、長時間・連続測定でも画面全体を再レイアウトしない
- `spotread`の細かな出力を100 ms単位でまとめ、UTF-8・CRLF・任意の分割位置・終了直前の未読出力を保持して処理
- アプリが異常終了した場合も独立した監視プロセスが`spotread`を終了し、孤立プロセスを残さない
- 380〜730 nmのスペクトルカラーを薄い背景にしたSwift Chartsグラフ
- 環境光・発光モードでは、D50とD65の基準分光分布を選択して、560 nmで測定値に合わせた比較曲線を重ねて表示
- XYZ、D50 Lab、モノクロY/L*、ピーク、Lux、CCT、Duv、EV、CRI R1〜R15、TLCI、TM-30、および各Caution・算出不能状態を解析して表示
- CRI R1〜R14は`spotread`出力を使用し、R15は測定スペクトルを5 nm間隔へ補間してTCS15からCIE 13.3方式で独自計算
- R1〜R15を試験色に対応した横並びの色付き棒グラフでスペクトル図の下に表示
- 環境光・発光モードでは、CRIとIES TM-30-15をタブで切り替え、TM-30には16色相ビンの色相グラフ、Rf、Rg、大型Rf–Rgプロット、CCT、Duv、99色評価用試料の試料別忠実度棒グラフを表示
- 「spotread詳細ログ」では空白入力を`<SPACE>`として可視化し、送信中・送信済み・送信失敗も区別

R15はArgyllCMS 3.5本来のCRI計算結果には含まれません。アプリ内の計算値であることを画面上に明記しています。

同梱版`spotread`には`-J`を追加しています。標準出力は機械処理専用のJSON Lines、標準エラーは従来の人間向けログです。JSONプロトコルv2では、測定器またはキーボードからのトリガーを受けた時点で`measurementStarted`を通知し、その後、1回分のスペクトル、測色値、演色評価値、TM-30の16色相ビンと99色評価用試料を1個のmeasurementイベントとして出力します。測定器本体のスイッチを待つキャリブレーションも、実際のトリガー時点で開始を再通知します。このため表示文言の変更には依存せず、Swift側はプロトコルの版、モード、点数、必須値を検証し、不完全な測定は表示せず解析エラーとしてログに残します。

TM-30の円形グラフは、ArgyllCMSの`tm3015_plot`と同じく16色相ビン間を補間し、基準光の輪郭を単位半径へ正規化して測定光との差を矢印で描画します。色相エリアは22.5°ずつ均等に分割し、補助色と番号を各エリアの中央へ配置します。CCTとDuvは左上、線の凡例は右下へ重ねて表示します。99本の棒グラフは、各評価用試料のJab色差からTM-30-15の係数7.54で算出した`Rf,CES`を示します。Rf–Rgプロットの①と②は、それぞれプランク軌跡上の光源と実用光源のおおよその範囲であり、適合判定には使用しません。

## CIE標準光源データ

D50とD65の比較曲線は、CIEが公開する1 nm間隔の公式データから380〜730 nmを5 nm間隔で収録し、点間を直線補間しています。

- [CIE標準光源D50](https://www.cie.co.at/datatable/cie-standard-illuminant-d50) — DOI `10.25039/CIE.DS.etgmuqt5`
- [CIE標準光源D65](https://www.cie.co.at/datatable/cie-standard-illuminant-d65) — DOI `10.25039/CIE.DS.hjfjmt59`

これらのデータセットは[CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/)で公開されています。用途の表記は、D50をISO 3664の観察条件、D65をISO 3668の塗料色比較条件として区別しています。曲線の数値はいずれもISO/CIE 11664-2に対応するCIE公式データです。

## spotread実行ファイル

1. `IWASHISCOPE_SPOTREAD_PATH`環境変数
2. App Bundle内の補助実行ファイルまたはResources

一般のHomebrew版や`PATH`上の`spotread`はJSONプロトコルを備えていないため使用しません。通常のXcodeビルドでは`Scripts/build-spotread.sh`が必要な依存関係だけをビルドし、生成した`spotread`をApp Bundleへ格納します。

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
├── Scripts/build-spotread.sh         # spotreadだけをビルドするJamターゲット
├── IwashiScope.xcodeproj              # App shell
├── IwashiScope/                       # App entry point / assets
└── IwashiScopePackage/
    └── Sources/IwashiScopeFeature/    # UI、プロセス制御、パーサー
```

開発に使用するテストと内部資料は公開リポジトリへ含めていません。`Package.swift`はローカルにテストが存在する場合だけテストターゲットを追加します。

## ビルド

必要な環境と手順は[BUILDING.md](BUILDING.md)を参照してください。Xcodeでは`IwashiScope.xcworkspace`を開きます。初回ビルド時にSparkle 2.9.4を取得し、`Scripts/build-spotread.sh`が同梱ソースから`spotread`を作成します。

## Sandboxと配布

測定器へのアクセスと補助プロセス実行のため、App Sandboxは有効にしていません。配布物には、同じソースからビルドして署名した`spotread`をApp Bundle内の補助実行ファイルとして同梱します。

ArgyllCMSソースとIwashiScope側の改変ソースを同じリポジトリで提供し、生成したオブジェクト、静的ライブラリ、`spotread`バイナリはコミット対象から除外します。再配布時はArgyllCMSのライセンス文書とAGPLv3の条件を確認してください。

## ライセンスとソースコード

Copyright © 2026 Yamonov.

IwashiScopeの独自部分は、特記のない限り[GNU Affero General Public Licenseバージョン3以降](LICENSE)で公開します。このソフトウェアには一切の保証がありません。

- ArgyllCMS 3.5.0の原著作権と個別ライセンスは維持されています。
- IwashiScopeによるArgyllCMSの改変内容は[ARGYLL_CHANGES.md](ARGYLL_CHANGES.md)に記録しています。
- Sparkle、CIE標準光源データ、ArgyllCMS内の各構成要素は[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)を参照してください。
- バイナリの作成・署名・公証・Sparkle配信は[RELEASING.md](RELEASING.md)を参照してください。

IwashiScopeおよび改変版`spotread`の対応ソースは、このリポジトリの各リリースタグから取得できます。
