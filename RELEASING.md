# IwashiScopeリリース手順

この文書はDeveloper IDによる署名・公証と、Sparkleによるアプリ内更新を前提にしています。署名鍵、notarytool資格情報、Sparkle秘密鍵はGitへ登録しません。

## 1. リリース前確認

1. `Config/Shared.xcconfig`の`MARKETING_VERSION`と`CURRENT_PROJECT_VERSION`を更新します。
2. `CHANGELOG.md`へ利用者向け変更点を記載します。
3. アプリアイコンが全サイズ登録されていることを確認します。
4. Apple Silicon専用かUniversal Binaryかを確定します。Universal配布では、同梱`spotread`にもarm64とx86_64の両方が含まれることを確認します。
5. `main`のソースからDebug/Releaseビルドが成功することを確認します。
6. 実機で、対応する各測定モード、校正、連続測定、測定中の終了、ワークスペース保存・復帰、画像・CSV・ASE書き出しを確認します。
7. `git status --ignored`で、秘密鍵や生成物が追跡対象に入っていないことを確認します。

## 2. Archive、署名、公証

Xcode Organizerから`IwashiScope`をArchiveし、Developer ID Applicationで配布します。Release構成はHardened Runtimeを有効にしています。

書き出したアプリについて、アプリ本体、Sparkle、同梱`spotread`を検証します。

```sh
codesign --verify --deep --strict --verbose=2 IwashiScope.app
spctl --assess --type execute --verbose=2 IwashiScope.app
xcrun stapler validate IwashiScope.app
```

署名済みApp Bundleの中身を後から置き換えないでください。修正が必要な場合は、ソースからArchiveを作り直します。

## 3. ZIPとSparkle署名

```sh
ditto -c -k --sequesterRsrc --keepParent \
  IwashiScope.app \
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
3. GitHub Releaseへ`IwashiScope-VERSION.zip`を登録します。
4. Release本文から同じタグのソースへリンクします。
5. GitHubが生成するSource codeアーカイブに加え、タグからビルドできることを確認します。

配布バイナリと対応ソースのタグを一致させることが、改変版ArgyllCMSを含む配布の前提です。

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
