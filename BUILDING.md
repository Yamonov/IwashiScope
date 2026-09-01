# IwashiScopeのビルド

## macOSアプリに必要な環境

- macOS
- Xcode 16.3以降
- Swift 6.1以降
- Jam 2.6.1互換の`jam`コマンド
- 初回のSwift Package取得に使用するインターネット接続

生成されるアプリの対応OSはmacOS 14.6以降です。

## 取得

```sh
git clone https://github.com/Yamonov/IwashiScope.git
cd IwashiScope
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
  -workspace IwashiScope.xcworkspace \
  -scheme IwashiScope \
  -configuration Debug \
  -destination 'generic/platform=macOS' \
  ARCHS='arm64 x86_64' \
  ONLY_ACTIVE_ARCH=NO \
  build
```

## CIE標準光源データの再生成

公開ソースには、CIEの公式CSVとメタデータ、チェックサムを検証する生成スクリプト、生成済みSwift・C#適応物を含めます。通常のビルドでは再生成は不要です。データ生成部分を変更した場合は、次を実行します。

```sh
Scripts/generate-cie-standard-illuminants.swift
Scripts/generate-cie-standard-illuminants.swift --check
```

原データはCC BY-SA 4.0、生成されたSwift・C#適応物はGPL-3.0-onlyです。AGPL-3.0-onlyのIwashiScope部分とはGPLv3・AGPLv3双方の第13条に基づいて結合されます。詳細は`ThirdParty/CIE/README.md`を参照してください。

## Xcode

`IwashiScope.xcworkspace`を開き、`IwashiScope`スキームを選択してビルドします。コマンドラインでは次のように実行できます。

```sh
xcodebuild \
  -workspace IwashiScope.xcworkspace \
  -scheme IwashiScope \
  -configuration Debug \
  -destination 'generic/platform=macOS' \
  ARCHS='arm64 x86_64' \
  ONLY_ACTIVE_ARCH=NO \
  build
```

公式ビルドはScripta!と同じApple Developerチーム`5NFE273M7M`とXcodeの自動署名を使用します。別の開発者が署名してビルドする場合は、XcodeのSigning & Capabilitiesで自分のチームを選ぶか、コマンドラインで`DEVELOPMENT_TEAM`を上書きしてください。署名を行わずにビルドを確認する場合は、後述の`CODE_SIGNING_ALLOWED=NO`を使用できます。

ビルドフェーズは`Scripts/build-spotread.sh`を実行して改変版helperを構築し、Xcodeが管理する`DERIVED_FILE_DIR`へコピーしてからApp Bundleへ格納します。ソースフォルダ内の実行ファイルをXcodeのビルド出力にはしません。

アプリ本体と`iwashiscope-spotread`は、macOS 14.6を最低要件とするarm64・x86_64のUniversal Binaryとしてビルドされます。ビルドスクリプトは構成が変わった場合にArgyllCMSの生成物を消去してから再構築し、完成した補助実行ファイルに両方のアーキテクチャがあることを`lipo`で検証します。

署名なしのReleaseビルドを確認する場合は、次を使用できます。

```sh
xcodebuild \
  -workspace IwashiScope.xcworkspace \
  -scheme IwashiScope \
  -configuration Release \
  -destination 'generic/platform=macOS' \
  ARCHS='arm64 x86_64' \
  ONLY_ACTIVE_ARCH=NO \
  CODE_SIGNING_ALLOWED=NO \
  build
```

`My Mac`を宛先にすると実行中のMacのアーキテクチャだけが生成される場合があります。Universal Binaryの確認には`generic/platform=macOS`を使用してください。

## 生成物

次のファイルはソースではないためGitへ登録しません。

- ArgyllCMSのオブジェクト、静的ライブラリ、生成ヘッダ
- `iwashiscope-spotread`実行ファイル
- DerivedData、Archive、App、ZIP
- 署名・公証・Sparkleの秘密鍵

公開リポジトリのタグには、その版のアプリと同梱`iwashiscope-spotread`を再構築するためのソース、ビルドスクリプト、固定済みSwift Package情報を含めます。

## Windows版helper

同じ`Argyll_V3.5.0`の追跡ソースから、Windows x64版の`iwashiscope-spotread.exe`もビルドできます。macOSとWindowsではコンパイラと生成物が異なるため、それぞれのOSの別cloneまたは別worktreeで、同一コミットをビルドしてください。同じ作業ディレクトリへ両OSの中間生成物を混在させないでください。

必要な環境は次のとおりです。

- Windows
- Visual StudioのC++ x64ビルドツール
- Jam 2.6.1互換の`jam.exe`
- PowerShell

PowerShellから次を実行します。

```powershell
.\Scripts\build-spotread-windows.ps1 -JamPath C:\path\to\jam.exe
```

Visual Studioは`vswhere.exe`で検出します。自動検出できない場合は、`-VsDevCmdPath`へ`VsDevCmd.bat`の絶対パスを指定できます。スクリプトはUTF-8処理の共通Cテストを実行してから、次のファイルを生成します。

```text
Argyll_V3.5.0\spectro\iwashiscope-spotread.exe
```

Windowsアプリの出力先へ直接コピーする場合は、`-OutputPath`を指定します。

```powershell
.\Scripts\build-spotread-windows.ps1 `
  -JamPath C:\path\to\jam.exe `
  -OutputPath C:\path\to\app\iwashiscope-spotread.exe
```

公開前のソース監査は次で実行します。

```sh
Scripts/audit-source.sh
```

リリースタグを作成した後は、クリーンな作業ツリーとタグ一致も含めて確認します。

```sh
Scripts/audit-source.sh --release vVERSION
```

## Windows版アプリ

Windows版アプリ本体は.NET 10とWPFで実装し、`Windows/`以下にソリューション、ソース、テスト、配布スクリプトを収録しています。ソースからのビルドとテストはWindowsで実行します。

```powershell
dotnet build .\Windows\IwashiScope.Windows.slnx -c Release
dotnet test .\Windows\IwashiScope.Windows.slnx -c Release --no-build
```

自己完結型x64配布ZIPは、共通ArgyllCMSソースからWindows版helperをビルドしたうえで作成します。

```powershell
.\Windows\Scripts\Build-Release.ps1 `
  -Version VERSION `
  -JamPath C:\path\to\jam.exe `
  -OutputRoot C:\path\to\release
```

詳しい必要環境、成果物、ライセンス同梱内容は`Windows/README.md`を参照してください。
