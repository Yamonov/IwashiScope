# IwashiScopeリリース手順

この文書はDeveloper IDによる署名・公証と、Sparkleによるアプリ内更新を前提にしています。署名鍵、notarytool資格情報、Sparkle秘密鍵はGitへ登録しません。

## 1. リリース前確認

1. `Config/Shared.xcconfig`の`MARKETING_VERSION`と`CURRENT_PROJECT_VERSION`を更新します。
2. `CHANGELOG.md`へ利用者向け変更点を記載します。
3. アプリアイコンが全サイズ登録されていることを確認します。
4. `Scripts/audit-source.sh`を実行し、必須ソースがGit管理下にあること、ライセンス表記、改変表示、メーカー固有データの混入がないことを確認します。
5. `main`のソースからDebug/Releaseビルドが成功することを確認します。
6. macOS版とWindows版の実機で、対応する各測定モード、校正、連続測定、測定中の終了、ワークスペース保存・復帰、画像・CSV・ASE書き出しを確認します。
7. `git status --ignored`で、秘密鍵や生成物が追跡対象に入っていないことを確認します。

IwashiScopeの配布形式はUniversal Binaryです。アプリ本体、改変版`iwashiscope-spotread`、App Bundle内の実行可能な第三者バイナリにarm64とx86_64の両方が必要です。

## 2. Archive、署名、公証

Scripta!と同じ署名・公証手順を使用します。公式ビルドはApple Developerチーム`5NFE273M7M`の自動署名を使用し、Release ArchiveをApple Developmentで署名します。Xcode Organizerから`IwashiScope`を`Any Mac (Apple Silicon, Intel)`宛先でArchiveした後、`Distribute App`の`Developer ID`を選択してAppleへアップロードし、表示が`Ready to distribute`になるまで待ちます。

コマンドラインでArchiveする場合は、宛先、チーム、アーキテクチャを明示します。

```sh
xcodebuild archive \
  -workspace IwashiScope.xcworkspace \
  -scheme IwashiScope \
  -configuration Release \
  -destination 'generic/platform=macOS' \
  DEVELOPMENT_TEAM=5NFE273M7M \
  ARCHS='arm64 x86_64' \
  ONLY_ACTIVE_ARCH=NO \
  -archivePath IwashiScope.xcarchive
```

公証が完了したArchiveの`Info.plist`を確認します。

```sh
IWASHISCOPE_ARCHIVE="/Users/yamo/Library/Developer/Xcode/Archives/YYYY-MM-DD/IwashiScope YYYY-MM-DD, HH.MM.xcarchive"
plutil -p "$IWASHISCOPE_ARCHIVE/Info.plist"
```

次を確認します。

- `CFBundleShortVersionString`と`CFBundleVersion`がリリース対象と一致する
- `ApplicationProperties`の`Architectures`に`arm64`と`x86_64`がある
- `ApplicationProperties`の`Team`が`5NFE273M7M`
- `Distributions`の`uploadDestination`が`Developer ID`
- `processingCompletedEvent`が成功し、`Ready to distribute`になっている

公証済みArchiveから一時フォルダへアプリを書き出します。XcodeBuildMCPには`-exportNotarizedApp`相当がないため、この工程だけはXcode標準の`xcodebuild`を使用します。

```sh
IWASHISCOPE_EXPORT_ROOT="$(mktemp -d /tmp/iwashiscope-notarized-export.XXXXXX)"
xcodebuild -exportNotarizedApp \
  -archivePath "$IWASHISCOPE_ARCHIVE" \
  -exportPath "$IWASHISCOPE_EXPORT_ROOT/Export"

IWASHISCOPE_EXPORTED_APP="$IWASHISCOPE_EXPORT_ROOT/Export/IwashiScope.app"
```

この公証済みアプリを配布元とします。署名済みApp Bundleの中身を後から上書き、追加、削除しないでください。修正が必要な場合は、ソースからArchiveを作り直し、再度Developer ID配布と公証を行います。

書き出したアプリについて、アプリ本体、Sparkle、同梱`iwashiscope-spotread`を検証します。

```sh
codesign --verify --deep --strict --verbose=4 "$IWASHISCOPE_EXPORTED_APP"
spctl --assess --type execute --verbose=4 "$IWASHISCOPE_EXPORTED_APP"
xcrun stapler validate "$IWASHISCOPE_EXPORTED_APP"
```

公証済みアプリを用意した後、次の監査を実行します。

```sh
Scripts/audit-release.sh "$IWASHISCOPE_EXPORTED_APP"
```

この監査は、Universal Binary、Developer ID署名、Hardened Runtime、secure timestamp、`get-task-allow`の不在、公証チケット、メーカー固有データの不在を確認します。

## 3. ZIPとSparkle署名

```sh
ditto -c -k --sequesterRsrc --keepParent \
  "$IWASHISCOPE_EXPORTED_APP" \
  IwashiScope-VERSION.zip
```

Sparkle 2の`sign_update`を、`Info.plist`の`SUPublicEDKey`に対応する秘密鍵で実行します。秘密鍵はリポジトリ外で管理します。

```sh
/path/to/Sparkle/bin/sign_update IwashiScope-VERSION.zip
```

出力された`edSignature`とZIPのバイト数を記録します。

## 4. GitHub Releaseと対応ソース

1. バイナリと一致するソースを`main`へ反映します。
2. 版と同じ名前のタグ`vVERSION`を作成します。
3. `Scripts/audit-source.sh --release vVERSION`を実行します。
4. `Scripts/package-source.sh VERSION`で`IwashiScope-VERSION-source.zip`を作成します。
5. GitHub ReleaseへmacOS版`IwashiScope-VERSION.zip`、Windows版`IwashiScope-VERSION-Windows-x64.zip`、`IwashiScope-VERSION-source.zip`を登録します。
6. Release本文から同じタグのソースへリンクします。
7. タグを新規cloneし、CIE生成データの検証とUniversal Releaseビルドが成功することを確認します。

配布バイナリとソースのタグを一致させることが、改変版ArgyllCMSを含む配布の前提です。

対応ソースには、IwashiScope全ソース、実際にリンクするArgyllCMSソース、ビルドスクリプト、Xcodeプロジェクト、固定済みSwift Package情報、CIE公式CSV・メタデータ・生成スクリプト・生成済みSwift適応物、編集可能なアイコン原稿、ライセンスと改変記録を含めます。署名鍵、notarytool資格情報、Sparkle秘密鍵は含めません。

Windows版は`Windows/Scripts/Build-Release.ps1`で、共通ArgyllCMSソースのWindows x64 helper、全Windowsテスト、自己完結型.NETランタイム、ライセンスと対応ソース情報を含むZIPを作成します。公開用の信頼されたAuthenticode証明書がない場合は自己署名せず、GitHub Release本文とサイトに未署名であることを明記します。

CIEの公式CSVはCC BY-SA 4.0のまま収録し、生成されたSwift適応物はGPL-3.0-onlyとして提供します。この適応物とAGPL-3.0-onlyのIwashiScope部分は、GPLv3・AGPLv3双方の第13条に基づいて結合します。`ThirdParty/CIE/README.md`、`THIRD_PARTY_NOTICES.md`、生成済みSwiftファイルの表示をリリースごとに維持します。

アプリZIPとソースZIPを配置した後、同じファイルを対象にチェックサム一覧を作成します。

```sh
shasum -a 256 \
  IwashiScope-VERSION.zip \
  Release/IwashiScope-VERSION-source.zip \
  > SHA256SUMS.txt
```

Windows版ZIPはWindows側で算出したSHA-256と、GitHub Releaseへアップロード後のasset digestを照合し、同じ`SHA256SUMS.txt`へ追加します。

Spyder 2の`spyd2PLD.bin`、Spyder 4/5の`spyd4cal.bin`、メーカー配布のEDR・CCSS・CCMXなど、再配布許諾を確認できないメーカー固有ファイルは、リポジトリとApp Bundleへ含めません。必要な機器では利用者自身が正規入手したファイルを使用します。

## 5. appcastとGitHub Pages

`IwashiScope/Info.plist`の更新URLは次です。

```text
https://yamonov.github.io/IwashiScope/appcast.xml
```

GitHubリポジトリのPages設定で、`main`ブランチの`/docs`を公開元にします。初回リリース前は`docs/appcast.xml`に項目がありません。

GitHub ReleaseのZIP公開後、Sparkleの`generate_appcast`または手作業で`docs/appcast.xml`へリリース項目を追加します。最低限、次を一致させます。

- バージョンとビルド番号
- ZIPのHTTPS URL
- ZIPの正確なバイト数
- `sparkle:edSignature`
- macOS 14.6の最低要件
- リリースノート

appcastを公開した後、次を確認します。

```sh
xmllint --noout docs/appcast.xml
curl -fsSL https://yamonov.github.io/IwashiScope/appcast.xml
```

最後に、1つ前の公開版から「アップデートを確認…」を実行し、署名検証、ダウンロード、置換、再起動まで確認します。

## Windows updates with WinSparkle

Windows版はNuGetの`WinSparkle` 0.9.4と、次のWindows専用feedを使用します。

```text
https://yamonov.github.io/IwashiScope/appcast-windows.xml
```

`docs/appcast-windows.xml`へ追加できるのは、インストール可能なWindows x64
payloadだけです。`Windows/Scripts/Build-Release.ps1`はportable ZIPに加え、
`IwashiScope-VERSION-Windows-x64-Setup.exe`を作成します。portable ZIPは
インストーラーではないため、WinSparkleのenclosureには指定しません。

Windows installerを作成した後、installerをMacのリリース担当環境へ渡し、
macOS版の`SUPublicEDKey`と同じ公開鍵に対応する既存のSparkle
`sign_update`で署名します。IwashiScope本番秘密鍵はWindowsへコピー、要求、保存、
表示しません。Windowsへ戻すのはdetached EdDSA署名文字列だけです。

```sh
/path/to/Sparkle/bin/sign_update \
  IwashiScope-VERSION-Windows-x64-Setup.exe
```

公開前の署名経路確認には、Windowsで一時鍵だけを生成し、production公開鍵とfeedを
変更しない次のテストを使います。テスト秘密鍵と成果物は終了時に削除されます。

```powershell
.\Windows\Scripts\Test-WinSparkleSigning.ps1 `
  -InstallerPath .\Windows\artifacts\release-VERSION\IwashiScope-VERSION-Windows-x64-Setup.exe
```

installerをGitHub Releaseへアップロードした後、Macから戻した署名文字列と実ファイルから
Windows専用appcastを生成します。生成先は既定では`Windows/artifacts`配下で、
追跡済みfeedを自動上書きしません。

```powershell
.\Windows\Scripts\New-WindowsAppcast.ps1 `
  -Version VERSION `
  -TagName vVERSION `
  -InstallerPath .\Windows\artifacts\release-VERSION\IwashiScope-VERSION-Windows-x64-Setup.exe `
  -EdSignature SIGNATURE
```

出力された署名、installerの正確なbyte数、HTTPS URLをenclosureへ記録し、
`sparkle:os="windows-x64"`、`sparkle:version`、
`sparkle:shortVersionString`、`sparkle:edSignature`を設定します。本番秘密鍵はWindows、
Git、build output、release ZIPへ入れません。feed公開前に次を確認します。

```powershell
.\Windows\Scripts\Test-WindowsAppcast.ps1 `
  -AppcastPath PATH_TO_GENERATED_APPCAST `
  -InstallerPath IwashiScope-VERSION-Windows-x64-Setup.exe `
  -RequireItem
```

この検証は実installerに対してproduction公開鍵で署名を検証し、正確なbyte数、
ProductVersion/FileVersion、HTTPS URL、`windows-x64`、重複version、履歴itemを確認します。
署名のないitemを`docs/appcast-windows.xml`へ公開してはいけません。

最後に1つ前のWindows公開版から、手動更新確認、EdDSA検証、installer起動、
未保存workspaceがある場合の終了拒否、更新後の再起動を実機確認します。

MacとWindowsは同じrepositoryとGitHub Pagesを使いますが、feedは
`docs/appcast.xml`と`docs/appcast-windows.xml`に分けます。したがって片方だけを
先に公開でき、version番号もOSごとに独立して進められます。各OSの履歴内では
`sparkle:version`を必ず増加させます。1つのfeedを共有する場合も、OSごとに別の
`item`を作り、それぞれのenclosureへ`sparkle:os="macos"`または
`sparkle:os="windows-x64"`を設定すれば同じ運用が可能ですが、Sparkle公式は
Mac/Windows別feedを推奨しています。
