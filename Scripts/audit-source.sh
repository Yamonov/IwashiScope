#!/bin/sh

set -eu

project_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$project_root"

release_tag=
case $# in
	0)
		;;
	2)
		[ "$1" = "--release" ] || {
			printf '%s\n' "usage: Scripts/audit-source.sh [--release vVERSION]" >&2
			exit 2
		}
		release_tag=$2
		;;
	*)
		printf '%s\n' "usage: Scripts/audit-source.sh [--release vVERSION]" >&2
		exit 2
		;;
esac

fail() {
	printf '%s\n' "error: $*" >&2
	exit 1
}

git rev-parse --is-inside-work-tree >/dev/null 2>&1 \
	|| fail "source audit must run inside the IwashiScope Git repository"

required_files="
.gitattributes
LICENSE
LICENSES/GPL-3.0-only.txt
LICENSES/WinSparkle-MIT.txt
NOTICE
THIRD_PARTY_NOTICES.md
ARGYLL_CHANGES.md
BUILDING.md
RELEASING.md
PRIVACY.md
TRADEMARKS.md
IwashiScope.xcodeproj/project.xcworkspace/xcshareddata/swiftpm/Package.resolved
IwashiScope.xcworkspace/xcshareddata/swiftpm/Package.resolved
Argyll_V3.5.0/License.txt
Argyll_V3.5.0/License2.txt
Argyll_V3.5.0/License3.txt
ThirdParty/CIE/CIE_std_illum_D50.csv
ThirdParty/CIE/CIE_std_illum_D50.csv_metadata.json
ThirdParty/CIE/CIE_std_illum_D65.csv
ThirdParty/CIE/CIE_std_illum_D65.csv_metadata.json
ThirdParty/CIE/README.md
Scripts/generate-cie-standard-illuminants.swift
Scripts/package-source.sh
Scripts/audit-source.sh
Scripts/audit-release.sh
Scripts/build-spotread.sh
Scripts/build-spotread-windows.ps1
Windows/.gitignore
Windows/README.md
Windows/Directory.Build.props
Windows/IwashiScope.Windows.slnx
Windows/Scripts/Build-Release.ps1
Windows/Scripts/Build-WindowsInstaller.ps1
Windows/Scripts/New-WindowsAppcast.ps1
Windows/Scripts/Test-WinSparkleSigning.ps1
Windows/Scripts/Test-WindowsAppcast.ps1
Windows/Scripts/Test-WindowsInstaller.ps1
Windows/Scripts/installer/IwashiScopeAppInstaller.cs
Windows/Scripts/installer/IwashiScopeInstallerCore.cs
Windows/Scripts/installer/IwashiScopeInstallerCoreTests.cs
Windows/src/IwashiScope.App.Wpf/IwashiScope.App.Wpf.csproj
Windows/src/IwashiScope.App.Wpf/MainWindow.xaml
Windows/src/IwashiScope.App.Wpf/MainWindow.xaml.cs
Windows/src/IwashiScope.App.Wpf/Updates/UpdateShutdownPolicy.cs
Windows/src/IwashiScope.App.Wpf/Updates/WinSparkleUpdater.cs
Windows/src/IwashiScope.App.Wpf/Resources/Icons/IwashiScope.ico
Windows/src/IwashiScope.App.Wpf/Resources/Icons/IwashiScope-128.png
Windows/src/IwashiScope.App.Wpf/Resources/Localizable.xcstrings
Windows/src/IwashiScope.Core/IwashiScope.Core.csproj
Windows/src/IwashiScope.Core/Models/SpectrumYAxisConfiguration.cs
Windows/src/IwashiScope.Infrastructure.Windows/IwashiScope.Infrastructure.Windows.csproj
Windows/src/IwashiScope.Protocol/IwashiScope.Protocol.csproj
Windows/tests/IwashiScope.Tests/IwashiScope.Tests.csproj
Windows/tests/IwashiScope.Tests/ProtocolTests.cs
Windows/tests/IwashiScope.Tests/ReleaseMetadataTests.cs
Windows/tests/IwashiScope.Tests/SpectrumYAxisTests.cs
Windows/tests/IwashiScope.Tests/WinSparkleIntegrationTests.cs
docs/appcast-windows.xml
Windows/tools/Generate-WindowsIcon.ps1
Windows/tools/Compare-UiParityEvidence.ps1
Argyll_V3.5.0/spectro/spotread_jsonl.c
Argyll_V3.5.0/spectro/spotread_jsonl.h
Argyll_V3.5.0/spectro/spotread_jsonl_test.c
IwashiScopePackage/Sources/IwashiScopeFeature/Models/CIEStandardIlluminantData.generated.swift
IwashiScope/Resources/NOTICE.txt
IwashiScope/Resources/THIRD-PARTY-NOTICES.txt
IwashiScope/Resources/Licenses/ArgyllCMS-AGPL-3.0.txt
IwashiScope/Resources/Licenses/GPL-3.0-only.txt
IwashiScope.icon/icon.json
IwashiScope.icon/README.md
IwashiScope.icon/Assets/Image 2.svg
IwashiScope.icon/Assets/Image 3.svg
IwashiScope/Assets.xcassets/AppIcon.appiconset/Contents.json
IwashiScope/Assets.xcassets/AppIcon.appiconset/IwashiScope-16.png
IwashiScope/Assets.xcassets/AppIcon.appiconset/IwashiScope-16@2x.png
IwashiScope/Assets.xcassets/AppIcon.appiconset/IwashiScope-32.png
IwashiScope/Assets.xcassets/AppIcon.appiconset/IwashiScope-32@2x.png
IwashiScope/Assets.xcassets/AppIcon.appiconset/IwashiScope-128.png
IwashiScope/Assets.xcassets/AppIcon.appiconset/IwashiScope-128@2x.png
IwashiScope/Assets.xcassets/AppIcon.appiconset/IwashiScope-256.png
IwashiScope/Assets.xcassets/AppIcon.appiconset/IwashiScope-256@2x.png
IwashiScope/Assets.xcassets/AppIcon.appiconset/IwashiScope-512.png
IwashiScope/Assets.xcassets/AppIcon.appiconset/IwashiScope-512@2x.png
"

printf '%s\n' "$required_files" |
	while IFS= read -r required_file; do
		[ -n "$required_file" ] || continue
		[ -f "$required_file" ] \
			|| fail "required source or license file is missing: $required_file"
		git ls-files --error-unmatch -- "$required_file" >/dev/null 2>&1 \
			|| fail "required source or license file is not tracked by Git: $required_file"
	done

# Preserve the downloaded CIE originals byte-for-byte, including their CRLF
# newlines. Their checksums are verified by the generator immediately below.
for diff_mode in working staged; do
	case "$diff_mode" in
		working)
			diff_command='git diff --check'
			;;
		staged)
			diff_command='git diff --cached --check'
			;;
	esac

	if ! diff_output=$(
		$diff_command -- . \
			':(exclude)ThirdParty/CIE/*.csv' \
			':(exclude)ThirdParty/CIE/*.json' 2>&1
	); then
		printf '%s\n' "$diff_output" >&2
		fail "$diff_mode source changes contain whitespace errors"
	fi
done

cmp -s Argyll_V3.5.0/License3.txt LICENSES/GPL-3.0-only.txt \
	|| fail "source GPL-3.0-only text does not match the official GPLv3 copy"
cmp -s Argyll_V3.5.0/License3.txt \
	IwashiScope/Resources/Licenses/GPL-3.0-only.txt \
	|| fail "bundled GPL-3.0-only text does not match the official GPLv3 copy"

module_cache=$(mktemp -d "${TMPDIR:-/tmp}/iwashiscope-module-cache.XXXXXX")
trap 'rm -rf "$module_cache"' EXIT HUP INT TERM
CLANG_MODULE_CACHE_PATH="$module_cache/clang" \
SWIFT_MODULECACHE_PATH="$module_cache/swift" \
	Scripts/generate-cie-standard-illuminants.swift --check

generated_cie_file=IwashiScopePackage/Sources/IwashiScopeFeature/Models/CIEStandardIlluminantData.generated.swift
grep -F 'SPDX-License-Identifier: GPL-3.0-only' "$generated_cie_file" >/dev/null \
	|| fail "generated CIE Swift adaptation is not marked GPL-3.0-only"
grep -F 'Adaptation date: 2026-07-23' "$generated_cie_file" >/dev/null \
	|| fail "generated CIE Swift adaptation date is missing"
grep -F 'Both CC BY-SA 4.0 and GPL-3.0-only apply' "$generated_cie_file" >/dev/null \
	|| fail "generated CIE Swift adaptation does not preserve the original license notice"
grep -F 'section 13 of GPLv3 and AGPLv3' "$generated_cie_file" >/dev/null \
	|| fail "generated CIE Swift adaptation does not document the section 13 combination"

if git grep -n -E \
	'AGPL[^[:cntrl:]]*(or later|以降)|version 3 or later|Version 3 or later|バージョン3以降' \
	-- NOTICE README.md ARGYLL_CHANGES.md IwashiScope IwashiScopePackage/Sources \
		Argyll_V3.5.0/spectro/spotread_jsonl.c \
		Argyll_V3.5.0/spectro/spotread_jsonl.h \
		Argyll_V3.5.0/spectro/spotread_jsonl_test.c; then
	fail "whole-product AGPL wording still allows a later version"
fi

if git grep -n -E 'JSON( Lines)?プロトコルv2|JSON protocol v2' \
	-- README.md CHANGELOG.md BUILDING.md RELEASING.md; then
	fail "public documentation still describes JSON protocol version 2"
fi

grep -F 'JSONプロトコルv3' README.md >/dev/null \
	|| fail "README does not identify JSON protocol version 3"
grep -F 'GPLv3・AGPLv3双方の第13条' README.md >/dev/null \
	|| fail "README does not document the CIE GPLv3/AGPLv3 section 13 combination"

if git grep -n 'Argyll_V3.5.0/spectro/spotread' \
	-- IwashiScope.xcodeproj README.md BUILDING.md RELEASING.md; then
	fail "an app build or distribution reference still uses the upstream spotread output name"
fi

if grep -F '$(SRCROOT)/Argyll_V3.5.0/spectro/iwashiscope-spotread' \
	IwashiScope.xcodeproj/project.pbxproj >/dev/null; then
	fail "the Xcode helper output must not be located inside the source tree"
fi
grep -F '$(DERIVED_FILE_DIR)/iwashiscope-spotread' \
	IwashiScope.xcodeproj/project.pbxproj >/dev/null \
	|| fail "the Xcode helper output is not located in DerivedData"

grep -F '8BAA00102F23A00000C0DE01' \
	IwashiScope.xcodeproj/project.pbxproj |
	grep -F 'settings = {ATTRIBUTES = (CodeSignOnCopy, ); };' >/dev/null \
	|| fail "the Xcode helper reference or CodeSignOnCopy setting is missing"
helper_build_file_references=$(grep -c \
	'8BAA00102F23A00000C0DE01' \
	IwashiScope.xcodeproj/project.pbxproj)
[ "$helper_build_file_references" -ge 2 ] \
	|| fail "iwashiscope-spotread is missing from the Embed build phase"
grep -F 'IwashiScope.xctestplan,' IwashiScope.xcodeproj/project.pbxproj >/dev/null \
	|| fail "the local test plan is not excluded from the application target"

for attributed_file in \
	IwashiScopePackage/Sources/IwashiScopeFeature/Models/ColorRenderingReferenceData.swift \
	IwashiScopePackage/Sources/IwashiScopeFeature/Models/ColorRenderingIndexCalculator.swift \
	IwashiScopePackage/Sources/IwashiScopeFeature/Views/TM30ColorPatchChartView.swift \
	IwashiScopePackage/Sources/IwashiScopeFeature/Views/TM30ColorVectorGraphicView.swift; do
	grep -F 'ArgyllCMS 3.5.0' "$attributed_file" >/dev/null \
		|| fail "ArgyllCMS attribution is missing: $attributed_file"
	grep -F 'GPL-2.0-or-later' "$attributed_file" >/dev/null \
		|| fail "original GPL license is missing: $attributed_file"
	grep -F '2026-07-23' "$attributed_file" >/dev/null \
		|| fail "modification date is missing: $attributed_file"
done

tracked_oem_files=$(git ls-files | grep -Ei \
	'(^|/)(spyd2PLD|spyd4cal)\.bin$|\.(edr|ccss|ccmx)$' || true)
if [ -n "$tracked_oem_files" ]; then
	printf '%s\n' "$tracked_oem_files" >&2
	fail "manufacturer firmware or calibration data is tracked"
fi

tracked_private_materials=$(git ls-files '材料/**')
if [ -n "$tracked_private_materials" ]; then
	printf '%s\n' "$tracked_private_materials" >&2
	fail "private design working files are tracked; publish IwashiScope.icon instead"
fi

tracked_products=$(git ls-files | grep -E \
	'(^|/)(iwashiscope-spotread|spotread)(\.exe)?$|\.(o|obj|a|lib|dylib|so|dll|exe|pdb|app|xcarchive)$' || true)
if [ -n "$tracked_products" ]; then
	printf '%s\n' "$tracked_products" >&2
	fail "generated build products are tracked"
fi

tracked_windows_local_data=$(git ls-files Windows | grep -E \
	'(^|/)(bin|obj|artifacts|TestResults|logs)(/|$)|\.(iwashiscope|user|suo|trx|coverage|coveragexml)$' || true)
if [ -n "$tracked_windows_local_data" ]; then
	printf '%s\n' "$tracked_windows_local_data" >&2
	fail "Windows build products or user data are tracked"
fi

if [ -n "$release_tag" ]; then
	case "$release_tag" in
		v[0-9]*)
			;;
		*)
			fail "release tag must use the vVERSION form"
			;;
	esac

	[ -z "$(git status --porcelain --untracked-files=all)" ] \
		|| fail "release source audit requires a clean working tree"

	head_commit=$(git rev-parse HEAD)
	tag_commit=$(git rev-parse "$release_tag^{commit}" 2>/dev/null) \
		|| fail "release tag does not exist: $release_tag"
	[ "$head_commit" = "$tag_commit" ] \
		|| fail "HEAD does not match release tag $release_tag"

	marketing_version=$(sed -n \
		's/^[[:space:]]*MARKETING_VERSION[[:space:]]*=[[:space:]]*//p' \
		Config/Shared.xcconfig | head -1)
	[ -n "$marketing_version" ] \
		|| fail "MARKETING_VERSION is missing"
	[ "$release_tag" = "v$marketing_version" ] \
		|| fail "release tag $release_tag does not match MARKETING_VERSION $marketing_version"

	if git show-ref --verify --quiet refs/remotes/origin/main; then
		git merge-base --is-ancestor HEAD refs/remotes/origin/main \
			|| fail "release commit is not present on origin/main"
	fi
fi

printf '%s\n' "Source audit passed."
