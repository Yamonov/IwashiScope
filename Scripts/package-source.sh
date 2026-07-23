#!/bin/sh

set -eu

project_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$project_root"

version=${1:-}
output_directory=${2:-"$project_root/Release"}

if [ -z "$version" ]; then
	printf '%s\n' "usage: Scripts/package-source.sh VERSION [OUTPUT_DIRECTORY]" >&2
	exit 2
fi

tag="v$version"
archive_name="IwashiScope-$version-source.zip"
archive_path="$output_directory/$archive_name"

Scripts/audit-source.sh --release "$tag"

mkdir -p "$output_directory"
git archive \
	--format=zip \
	--prefix="IwashiScope-$version-source/" \
	--output="$archive_path" \
	"$tag"

checksum=$(shasum -a 256 "$archive_path" | awk '{print $1}')
printf '%s  %s\n' "$checksum" "$archive_name" \
	> "$archive_path.sha256"

printf '%s\n' "Created $archive_path"
printf '%s\n' "Created $archive_path.sha256"
