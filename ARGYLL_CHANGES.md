# ArgyllCMS 3.5.0の改変

IwashiScopeは、ArgyllCMS 3.5.0の`spotread`をGUIから安全に制御するため、機械処理用のJSON Linesプロトコルを追加しています。

- 上流版: ArgyllCMS 3.5.0
- 上流サイト: <https://www.argyllcms.com/>
- 改変者: Yamonov
- 改変日: 2026-07-23

## 改変ファイル

| ファイル | 改変内容 |
| --- | --- |
| `Jamfile` | IwashiScopeのビルド時に、`spotread`へ必要なサブプロジェクトだけを読み込む分岐を追加 |
| `spectro/Jamfile` | JSON Lines実装を`spotread`へ追加し、IwashiScope専用ビルドターゲットを定義 |
| `spectro/instappsup.c` | 既存APIを維持したまま、校正状態を通知する任意コールバックを追加 |
| `spectro/instappsup.h` | 校正イベントとコールバックAPIを宣言 |
| `spectro/spotread.c` | `-J`、JSON Lines v2、測定・校正・エラー状態、CRI/TM-30出力を追加 |
| `spectro/spotread_jsonl.c` | JSON Linesの生成と標準出力・標準エラー分離を新規実装 |
| `spectro/spotread_jsonl.h` | JSON Linesの測定データと出力APIを新規定義 |
| `xicc/tm3015.c` | TM-30-15の99評価用試料について、基準光・測定光のJab値を呼び出し側へ返す機能を追加 |
| `xicc/tm3015.h` | 99評価用試料の出力引数を宣言 |

## プロトコル

`spotread -J`は標準出力をJSON Lines専用にし、従来の人間向けメッセージを標準エラーへ送ります。各行は`protocolVersion: 2`を持ちます。

主なイベントは次のとおりです。

- `hello`
- `instrument`
- `state`
- `calibration`
- `issue`
- `measurement`

`measurement`には、利用可能な場合にスペクトル、XYZ、Lab、Lux、CCT、Duv、CRI、TLCI、TM-30-15の16色相ビンと99評価用試料が含まれます。

## ライセンス

上流ファイルの著作権表示とライセンスは変更していません。新規の`spotread_jsonl.c`と`spotread_jsonl.h`はGNU AGPLバージョン3以降で公開します。上流のライセンス本文は`Argyll_V3.5.0/License.txt`、`License2.txt`、`License3.txt`および各サブディレクトリにあります。
