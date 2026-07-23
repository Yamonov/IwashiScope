#!/bin/sh

set -eu

application_path=${1:-}
expected_team_identifier=${IWASHISCOPE_DEVELOPMENT_TEAM:-5NFE273M7M}

if [ -z "$application_path" ]; then
	printf '%s\n' "usage: Scripts/audit-release.sh IwashiScope.app" >&2
	exit 2
fi

fail() {
	printf '%s\n' "error: $*" >&2
	exit 1
}

[ -d "$application_path" ] || fail "application bundle not found: $application_path"

main_executable="$application_path/Contents/MacOS/IwashiScope"
measurement_helper="$application_path/Contents/MacOS/iwashiscope-spotread"
sparkle_executable="$application_path/Contents/Frameworks/Sparkle.framework/Versions/B/Sparkle"
[ -x "$main_executable" ] || fail "main executable is missing"
[ -x "$measurement_helper" ] || fail "iwashiscope-spotread is missing"
[ -x "$sparkle_executable" ] || fail "Sparkle.framework is missing"

main_dependencies=$(/usr/bin/otool -L "$main_executable")
printf '%s\n' "$main_dependencies" |
	grep -F '@rpath/Sparkle.framework/Versions/B/Sparkle' >/dev/null \
	|| fail "main executable does not link the bundled Sparkle.framework"

main_runpaths=$(/usr/bin/otool -l "$main_executable" | awk '
	$1 == "cmd" && $2 == "LC_RPATH" {
		getline
		getline
		print $2
	}
')
printf '%s\n' "$main_runpaths" |
	grep -Fx '@executable_path/../Frameworks' >/dev/null \
	|| fail "main executable cannot resolve frameworks from Contents/Frameworks"

required_bundle_files="
Contents/Resources/NOTICE.txt
Contents/Resources/THIRD-PARTY-NOTICES.txt
Contents/Resources/ArgyllCMS-AGPL-3.0.txt
Contents/Resources/GPL-3.0-only.txt
Contents/Resources/Sparkle-LICENSE.txt
Contents/Resources/YAJL-COPYING.txt
Contents/Resources/libpng-LICENSE.txt
Contents/Resources/libtiff-COPYRIGHT.txt
Contents/Resources/zlib-LICENSE.txt
"
for relative_path in $required_bundle_files; do
	[ -f "$application_path/$relative_path" ] \
		|| fail "required license or notice is missing from app: $relative_path"
done

normalized_notice=$(tr '\n' ' ' \
	<"$application_path/Contents/Resources/NOTICE.txt")
printf '%s\n' "$normalized_notice" |
	grep -F 'GNU Affero General Public License version 3' >/dev/null \
	|| fail "application notice does not identify AGPL version 3"
printf '%s\n' "$normalized_notice" |
	grep -F 'GPL version 3 only' >/dev/null \
	|| fail "application notice does not identify the CIE GPL-3.0-only adaptation"
printf '%s\n' "$normalized_notice" |
	grep -F 'section 13 of GPLv3 and AGPLv3' >/dev/null \
	|| fail "application notice does not identify the section 13 combination"

bundle_version=$(/usr/libexec/PlistBuddy \
	-c 'Print :CFBundleShortVersionString' \
	"$application_path/Contents/Info.plist")
bundle_build=$(/usr/libexec/PlistBuddy \
	-c 'Print :CFBundleVersion' \
	"$application_path/Contents/Info.plist")
feed_url=$(/usr/libexec/PlistBuddy \
	-c 'Print :SUFeedURL' \
	"$application_path/Contents/Info.plist")
public_update_key=$(/usr/libexec/PlistBuddy \
	-c 'Print :SUPublicEDKey' \
	"$application_path/Contents/Info.plist")
minimum_system_version=$(/usr/libexec/PlistBuddy \
	-c 'Print :LSMinimumSystemVersion' \
	"$application_path/Contents/Info.plist")

[ -n "$bundle_version" ] || fail "CFBundleShortVersionString is empty"
[ -n "$bundle_build" ] || fail "CFBundleVersion is empty"
[ "$feed_url" = "https://yamonov.github.io/IwashiScope/appcast.xml" ] \
	|| fail "unexpected Sparkle feed URL"
[ -n "$public_update_key" ] || fail "Sparkle public update key is missing"
[ "$minimum_system_version" = "14.6" ] \
	|| fail "unexpected minimum macOS version: $minimum_system_version"

test_artifacts=$(find "$application_path" -type f \( \
	-name '*.xctestplan' -o \
	-name '*Tests.xctest' \
\) -print)
if [ -n "$test_artifacts" ]; then
	printf '%s\n' "$test_artifacts" >&2
	fail "test artifacts are present in the release app"
fi

while IFS= read -r candidate; do
	if file "$candidate" | grep -q 'Mach-O'; then
		/usr/bin/lipo "$candidate" -verify_arch arm64 x86_64 \
			|| fail "not Universal Binary: $candidate"
	fi
done <<EOF
$(find "$application_path" -type f -print)
EOF

codesign --verify --deep --strict --verbose=2 "$application_path"
signature_details=$(codesign -dvvv "$application_path" 2>&1)
printf '%s\n' "$signature_details" | grep -F 'flags=' | grep -F 'runtime' >/dev/null \
	|| fail "Hardened Runtime is not enabled"
printf '%s\n' "$signature_details" | grep -F 'Timestamp=' >/dev/null \
	|| fail "secure timestamp is missing"
printf '%s\n' "$signature_details" | grep -F 'Authority=Developer ID Application:' >/dev/null \
	|| fail "Developer ID Application signature is missing"
printf '%s\n' "$signature_details" | grep -F "TeamIdentifier=$expected_team_identifier" >/dev/null \
	|| fail "unexpected application signing team"

helper_signature_details=$(codesign -dvvv "$measurement_helper" 2>&1)
printf '%s\n' "$helper_signature_details" | grep -F 'flags=' | grep -F 'runtime' >/dev/null \
	|| fail "iwashiscope-spotread Hardened Runtime is not enabled"
printf '%s\n' "$helper_signature_details" | grep -F 'Timestamp=' >/dev/null \
	|| fail "iwashiscope-spotread secure timestamp is missing"
printf '%s\n' "$helper_signature_details" | grep -F 'Authority=Developer ID Application:' >/dev/null \
	|| fail "iwashiscope-spotread Developer ID signature is missing"
printf '%s\n' "$helper_signature_details" | grep -F "TeamIdentifier=$expected_team_identifier" >/dev/null \
	|| fail "unexpected iwashiscope-spotread signing team"

entitlements_file=$(mktemp /tmp/iwashiscope-entitlements.XXXXXX)
helper_entitlements_file=$(mktemp /tmp/iwashiscope-helper-entitlements.XXXXXX)
trap 'rm -f "$entitlements_file" "$helper_entitlements_file"' EXIT HUP INT TERM
codesign -d --entitlements :- "$application_path" >"$entitlements_file" 2>/dev/null
if plutil -extract com.apple.security.get-task-allow raw \
	-o - "$entitlements_file" 2>/dev/null | grep -q '^true$'; then
	fail "get-task-allow is enabled"
fi
codesign -d --entitlements :- "$measurement_helper" >"$helper_entitlements_file" 2>/dev/null
if plutil -extract com.apple.security.get-task-allow raw \
	-o - "$helper_entitlements_file" 2>/dev/null | grep -q '^true$'; then
	fail "iwashiscope-spotread get-task-allow is enabled"
fi

spctl --assess --type execute --verbose=2 "$application_path"
xcrun stapler validate "$application_path"

oem_files=$(find "$application_path" -type f \( \
	-iname '*.edr' -o \
	-iname '*.ccss' -o \
	-iname '*.ccmx' -o \
	-iname 'spyd2PLD.bin' -o \
	-iname 'spyd4cal.bin' \
\) -print)
if [ -n "$oem_files" ]; then
	printf '%s\n' "$oem_files" >&2
	fail "manufacturer firmware or calibration data is present in the app"
fi

printf '%s\n' "Release audit passed."
