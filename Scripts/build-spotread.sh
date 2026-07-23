#!/bin/sh

set -eu

project_root=${PROJECT_DIR:-$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)}
argyll_root="$project_root/Argyll_V3.5.0"
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
	printf '%s\n' "error: Jam is required to build the bundled SpectraMate spotread." >&2
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
"$jam_tool" -q -fJambase \
	-sBUILTIN_SSL=true \
	-sSPECTRAMATE_ONLY=true \
	spectramate_spotread

if [ ! -x "$argyll_root/spectro/spotread" ]; then
	printf '%s\n' "error: ArgyllCMS completed without producing spectro/spotread." >&2
	exit 1
fi
