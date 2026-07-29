# ArgyllCMS 3.5.0の改変

IwashiScopeは、ArgyllCMS 3.5.0の`spotread`をGUIから安全に制御するため、機械処理用のJSON Linesプロトコルを追加しています。

- 上流版: ArgyllCMS 3.5.0
- 上流サイト: <https://www.argyllcms.com/>
- 改変者: Yamonov
- 改変日: 2026-07-23
- 追加改変日: 2026-07-29

## 改変ファイル

| ファイル | 改変内容 |
| --- | --- |
| `Jamfile` | IwashiScopeのビルド時に、`spotread`へ必要なサブプロジェクトだけを読み込む分岐を追加 |
| `spectro/Jamfile` | JSON Lines実装を追加し、上流版と混同しない`iwashiscope-spotread`ビルドターゲットを定義 |
| `spectro/inst.h` | 現在のモード・解像度に対する実用波長範囲を取得する任意APIを追加 |
| `spectro/instappsup.c` | 既存APIを維持したまま、校正状態を通知する任意コールバックを追加 |
| `spectro/instappsup.h` | 校正イベントとコールバックAPIを宣言 |
| `spectro/munki.c` | ColorMunki系ドライバの実用波長範囲を共通APIから返す処理を追加 |
| `spectro/munki_imp.c` | 高解像度測定で複製される短波長側を除いた実用波長範囲を返す処理を追加 |
| `spectro/munki_imp.h` | ColorMunki系の実用波長範囲取得関数を宣言 |
| `spectro/spotread.c` | `-J`、JSON Lines v3、測定・校正・エラー状態、CRI/TM-30出力、実用波長範囲、IwashiScope改変版の起動表示を追加 |
| `spectro/spotread_jsonl.c` | JSON Linesの生成、実用波長範囲の出力、標準出力・標準エラー分離を新規実装 |
| `spectro/spotread_jsonl.h` | JSON Linesの測定データ、実用波長範囲、出力APIを新規定義 |
| `xicc/tm3015.c` | TM-30-15の99評価用試料について、基準光・測定光のJab値を呼び出し側へ返す機能を追加 |
| `xicc/tm3015.h` | 99評価用試料の出力引数を宣言 |

## プロトコル

`iwashiscope-spotread -J`は標準出力をJSON Lines専用にし、従来の人間向けメッセージを標準エラーへ送ります。各行は`protocolVersion: 3`を持ちます。最初の`hello`には、改変版を上流の`spotread`と区別するため、次の識別情報を含めます。

- `implementation: "IwashiScope spot reader"`
- `implementationVersion: 1`
- `argyllVersion: "3.5.0"`

主なイベントは次のとおりです。

- `hello`
- `instrument`
- `state`
- `calibration`
- `issue`
- `measurement`

`measurement`には、利用可能な場合にスペクトル、実用波長範囲、XYZ、Lab、Lux、CCT、Duv、CRI、TLCI、TM-30-15の16色相ビンと99評価用試料が含まれます。実用波長範囲は、ドライバがノイズ対策で端部を複製または除外している場合はその処理後の範囲、それ以外は測定器が返したスペクトル範囲です。

## Swiftへ移植・適応したArgyllCMSコード

次のファイルには、ArgyllCMS 3.5.0のGPL-2.0-or-later部分から移植または適応したコード・データがあります。各ファイル先頭にも原著作権、移植元、ライセンス、変更内容、変更日を記載しています。

| IwashiScopeファイル | ArgyllCMS 3.5.0の移植元 | 2026-07-23の変更内容 |
| --- | --- | --- |
| `IwashiScopePackage/Sources/IwashiScopeFeature/Models/ColorRenderingReferenceData.swift` | `xicc/xspect.c`の等色関数、昼光基底、TCS15データ | R15計算に必要な表をSwiftの不変配列として収録 |
| `IwashiScopePackage/Sources/IwashiScopeFeature/Models/ColorRenderingIndexCalculator.swift` | `xicc/xspect.c`の`icx_CIE1995_CRI()` | R15専用のSwift実装へ変換し、補間と入力検証を追加 |
| `IwashiScopePackage/Sources/IwashiScopeFeature/Views/TM30ColorPatchChartView.swift` | `xicc/tm3015.c`の`icx_IES_TM_30_15()` | 99 CESの個別忠実度計算をSwift化し、棒グラフ表示を追加 |
| `IwashiScopePackage/Sources/IwashiScopeFeature/Views/TM30ColorVectorGraphicView.swift` | `xicc/tm3015.c`の`tm3015_plot()` | 正規化・補間をSwift化し、16区分と変位矢印を描画 |

移植元の著作者はGraeme W. Gillです。`xicc/xspect.c`はCopyright (C) 2000–2006、`xicc/tm3015.c`はCopyright (C) 2019で、元の部分にはGPL-2.0-or-laterが適用されます。IwashiScopeによる変更部分にはAGPL-3.0-onlyを適用し、結合されたIwashiScope配布物はAGPL-3.0-onlyで提供します。

## ライセンス

上流ファイルの著作権表示とライセンスは変更していません。新規の`spotread_jsonl.c`と`spotread_jsonl.h`はGNU AGPLバージョン3（AGPL-3.0-only）で公開します。上流のライセンス本文は`Argyll_V3.5.0/License.txt`、`License2.txt`、`License3.txt`および各サブディレクトリにあります。
