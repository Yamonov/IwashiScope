# SpectraMateのビルド

## 必要な環境

- macOS
- Xcode 16.3以降
- Swift 6.1以降
- Jam 2.6.1互換の`jam`コマンド
- 初回のSwift Package取得に使用するインターネット接続

生成されるアプリの対応OSはmacOS 14.6以降です。

## 取得

```sh
git clone https://github.com/Yamonov/SpectraMate.git
cd SpectraMate
```

## Jam

`jam`をインストールし、次のコマンドが成功することを確認します。

```sh
jam -v
```

標準の検索場所にない場合は、ビルド時に`JAM`へ絶対パスを指定できます。

```sh
JAM=/absolute/path/to/jam \
  xcodebuild \
  -workspace SpectraMate.xcworkspace \
  -scheme SpectraMate \
  -configuration Debug \
  build
```

## Xcode

`SpectraMate.xcworkspace`を開き、`SpectraMate`スキームを選択してビルドします。コマンドラインでは次のように実行できます。

```sh
xcodebuild \
  -workspace SpectraMate.xcworkspace \
  -scheme SpectraMate \
  -configuration Debug \
  build
```

ビルドフェーズは`Scripts/build-spotread.sh`を実行し、`Argyll_V3.5.0/spectro/spotread`を生成してApp Bundleの補助実行ファイルへ格納します。

現行のスクリプトは、ビルドを実行したMacのCPUアーキテクチャ向けに`spotread`を生成します。署名済み配布物をUniversal Binaryにする場合は、アプリ本体だけでなく`spotread`もarm64とx86_64の両方を含むようにしてからArchiveしてください。

署名なしのReleaseビルドを確認する場合は、次を使用できます。

```sh
xcodebuild \
  -workspace SpectraMate.xcworkspace \
  -scheme SpectraMate \
  -configuration Release \
  CODE_SIGNING_ALLOWED=NO \
  build
```

## 生成物

次のファイルはソースではないためGitへ登録しません。

- ArgyllCMSのオブジェクト、静的ライブラリ、生成ヘッダ
- `spotread`実行ファイル
- DerivedData、Archive、App、ZIP
- 署名・公証・Sparkleの秘密鍵

公開リポジトリのタグには、その版のアプリと同梱`spotread`を再構築するためのソース、ビルドスクリプト、固定済みSwift Package情報を含めます。
