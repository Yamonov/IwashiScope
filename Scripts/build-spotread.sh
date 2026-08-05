#!/bin/sh

set -eu

project_root=${PROJECT_DIR:-$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)}
argyll_root="$project_root/Argyll_V3.5.0"
built_helper_path="$argyll_root/spectro/iwashiscope-spotread"
helper_path=${SCRIPT_OUTPUT_FILE_0:-"$built_helper_path"}
build_stamp="$argyll_root/.iwashiscope-build-config"
deployment_target=${MACOSX_DEPLOYMENT_TARGET:-14.6}
build_signature="universal-arm64-x86_64-macos${deployment_target}-jsonl3-patch5-spectrum-analysis-v1"
jam_tool=${JAM:-}

if [ -z "$jam_tool" ]; then
	jam_tool=$(command -v jam || true)
fi
if [ -z "$jam_tool" ] && [ -x /opt/homebrew/bin/jam ]; then
	jam_tool=/opt/homebrew/bin/jam
fi
if [ -z "$jam_tool" ] && [ -x /usr/local/bin/jam ]; then
	jam_tool=/usr/local/bin/jam
fi
if [ -z "$jam_tool" ]; then
	printf '%s\n' "error: Jam is required to build the bundled iwashiscope-spotread." >&2
	printf '%s\n' "Install Jam or set JAM to its executable path." >&2
	exit 1
fi

cd "$argyll_root"
# Xcode exports OS=MACOS, which overrides Jam's built-in OS=MACOSX and makes
# ArgyllCMS select its Unix/X11 branch. Let Jam identify the host itself.
unset OS
# spotread does not need a machine-local OpenSSL installation. Force Argyll's
# bundled SSL dependency so an unrelated /usr/local binary cannot change the
# architecture or reproducibility of the helper executable.
needs_clean=true
if [ -r "$build_stamp" ]; then
	stored_signature=$(sed -n '1p' "$build_stamp")
	if [ -x "$built_helper_path" ] \
		&& [ "$stored_signature" = "$build_signature" ] \
		&& /usr/bin/lipo "$built_helper_path" -verify_arch arm64 x86_64 >/dev/null 2>&1 \
		&& [ ! "$0" -nt "$built_helper_path" ] \
		&& ! find "$argyll_root" -type f \
			! -path "$built_helper_path" \
			! -path "$argyll_root/spectro/iwashiscope-spotread-jsonl-test" \
			! -path "$build_stamp" \
			-newer "$built_helper_path" \
			-print -quit | grep -q .; then
		needs_clean=false
	fi
fi

if [ "$needs_clean" = true ]; then
	"$jam_tool" -q -fJambase \
		-sBUILTIN_SSL=true \
		-sIWASHISCOPE_ONLY=true \
		-sIWASHISCOPE_UNIVERSAL=true \
		-sIWASHISCOPE_DEPLOYMENT_TARGET="$deployment_target" \
		clean

	"$jam_tool" -q -fJambase \
		-sBUILTIN_SSL=true \
		-sIWASHISCOPE_ONLY=true \
		-sIWASHISCOPE_UNIVERSAL=true \
		-sIWASHISCOPE_DEPLOYMENT_TARGET="$deployment_target" \
		-sIWASHISCOPE_TESTS=true \
		iwashiscope_spotread_jsonl_test \
		iwashiscope_spotread

	"$argyll_root/spectro/iwashiscope-spotread-jsonl-test"

	if [ ! -x "$built_helper_path" ]; then
		printf '%s\n' "error: ArgyllCMS completed without producing spectro/iwashiscope-spotread." >&2
		exit 1
	fi
	if ! /usr/bin/lipo "$built_helper_path" -verify_arch arm64 x86_64; then
		printf '%s\n' "error: iwashiscope-spotread is not a Universal Binary." >&2
		exit 1
	fi

	printf '%s\n' "$build_signature" > "$build_stamp"
fi

# Xcode must own only files inside DerivedData. Declaring an executable inside
# SRCROOT as a build output causes its stale-file cleanup to be rejected after
# an output is renamed. Jam may build in-tree internally, but the output exposed
# to Xcode is copied to SCRIPT_OUTPUT_FILE_0 in DERIVED_FILE_DIR.
if [ "$helper_path" != "$built_helper_path" ]; then
	/bin/mkdir -p "$(dirname "$helper_path")"
	/usr/bin/ditto "$built_helper_path" "$helper_path"
	/bin/chmod 755 "$helper_path"
fi

if [ ! -x "$helper_path" ]; then
	printf '%s\n' "error: iwashiscope-spotread was not copied to the Xcode derived output." >&2
	exit 1
fi
if ! /usr/bin/lipo "$helper_path" -verify_arch arm64 x86_64; then
	printf '%s\n' "error: derived iwashiscope-spotread is not a Universal Binary." >&2
	exit 1
fi
