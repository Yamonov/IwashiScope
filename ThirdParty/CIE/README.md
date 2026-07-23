# CIE標準光源D50・D65データ

このディレクトリには、International Commission on Illumination（CIE）が公開する公式CSVとメタデータを、改変せずに収録しています。

## 出典とライセンス

### D50

- データ名: CIE standard illuminant D50
- 作成者・公開者: International Commission on Illumination (CIE), Vienna, AT
- 公開年: 2022
- データ: <https://files.cie.co.at/CIE_std_illum_D50.csv>
- メタデータ: <https://files.cie.co.at/CIE_std_illum_D50.csv_metadata.json>
- DOI: <https://doi.org/10.25039/CIE.DS.etgmuqt5>
- 元資料: ISO/CIE 11664-2:2022, Table B.1
- ライセンス: [Creative Commons Attribution-ShareAlike 4.0 International](https://creativecommons.org/licenses/by-sa/4.0/)
- MD5: `e72757c3078b58e78ba63051be4b27b0`
- SHA-256: `b23049c6f7b266c1c1fbe147aa271e8930ca02d6e569c5ae1804c036faea4193`

### D65

- データ名: CIE standard illuminant D65
- 作成者・公開者: International Commission on Illumination (CIE), Vienna, AT
- 公開年: 2019
- データ: <https://files.cie.co.at/CIE_std_illum_D65.csv>
- メタデータ: <https://files.cie.co.at/CIE_std_illum_D65.csv_metadata.json>
- DOI: <https://doi.org/10.25039/CIE.DS.hjfjmt59>
- 元資料: ISO/CIE 11664-2:2022, Table B.1
- ライセンス: [Creative Commons Attribution-ShareAlike 4.0 International](https://creativecommons.org/licenses/by-sa/4.0/)
- MD5: `03d4eb9b837c60671627c946fb534deb`
- SHA-256（収録CSVから算出）: `e76f210bffff3d552ef7113025da5f325d5dfec200dd4b878b1a2f3a507032cb`

CIEのD65メタデータに記録されたSHA-256文字列は現在63桁のため、再生成スクリプトは公式ページにも記載されるMD5と、収録CSVから算出した64桁のSHA-256を検証します。

## Swift適応物とライセンスの整理

公式CSVとメタデータは改変せず、CC BY-SA 4.0のまま収録します。`Scripts/generate-cie-standard-illuminants.swift`が生成するSwiftファイルは、公式データから必要な波長位置を選択し、Swift配列として表現した適応物です。

生成された`CIEStandardIlluminantData.generated.swift`には、次の条件を適用します。

- CIEの原データにはCC BY-SA 4.0が引き続き適用されます。
- Yamonovによる選択・配列化・Swift表現への寄与は、Creative CommonsがCC BY-SA 4.0の一方向互換ライセンスとして指定したGNU GPLバージョン3のみ（GPL-3.0-only）で提供します。
- CC BY-SA 4.0とGPL-3.0-onlyの双方が適用されます。下流の利用者は、Creative Commonsが説明する一方向互換の仕組みに従い、GPLv3の方法で両ライセンスの表示・継承条件を満たすことができます。
- IwashiScopeへコンパイルした場合、このSwift適応物はGPL-3.0-onlyのまま、IwashiScope本体のAGPL-3.0-only部分とGPLv3・AGPLv3双方の第13条に基づいて結合されます。

互換性の根拠:

- Creative Commons「Compatible Licenses」: <https://creativecommons.org/compatible-licenses/>
- Creative Commons「ShareAlike compatibility: GPLv3」: <https://wiki.creativecommons.org/wiki/ShareAlike_compatibility%3A_GPLv3>
- GNU GPLv3本文: `LICENSES/GPL-3.0-only.txt`
- GNU AGPLv3本文: `LICENSE`

変換日: 2026-07-23

## Swiftデータへの変換

`Scripts/generate-cie-standard-illuminants.swift`は、公式の1 nm CSVから380〜730 nmの5 nm位置だけを数値補間せずに抽出し、次のファイルを生成します。

```text
IwashiScopePackage/Sources/IwashiScopeFeature/Models/CIEStandardIlluminantData.generated.swift
```

再生成:

```sh
Scripts/generate-cie-standard-illuminants.swift
```

収録CSVのチェックサムと生成結果の一致確認:

```sh
Scripts/generate-cie-standard-illuminants.swift --check
```

生成物を変更する場合は、公式CSVを直接編集せず、生成スクリプトを変更して再生成してください。公式CSV、生成スクリプト、生成済みSwiftファイルが、この適応物の変更に適したソース一式です。
