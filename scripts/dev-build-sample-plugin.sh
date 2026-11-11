#!/bin/bash

# Builds SamplePlugin and puts watched-plugins/SamplePlugin.emberdrop in place.
#
# Usage: ./scripts/dev-build-sample-plugin.sh [version]
#
# A plugin's version is the "version" in its emberdrop.json, not the assembly version.
# With no argument the package carries the version in SamplePlugin/emberdrop.json.
# With one (for example 1.1.0) the same build is packaged under that version, which is the
# quickest way to try a hot update: run the server, then run this script with 1.0.0 and
# again with 1.1.0 and watch the first version drain and unload (GET /api/plugins).
#
# The package is built outside the watched folder and moved in with a single rename, so a
# running server sees one finished file and never an intermediate version.

set -euo pipefail

# Run from the repository root, wherever the script is called from.
cd "$(dirname "$0")/.."

WATCHED="watched-plugins"
PACKAGE_NAME="SamplePlugin.emberdrop"
VERSION="${1:-}"

if [ -n "$VERSION" ] && ! [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$ ]]; then
    echo "Not a Semantic Version: $VERSION (expected for example 1.2.3 or 1.2.3-beta.1)" >&2
    exit 1
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

echo "Building SamplePlugin..."
dotnet build SamplePlugin/SamplePlugin.csproj \
    --verbosity minimal \
    -p:EmberdropPackageDirectory="$WORK"

if [ -n "$VERSION" ]; then
    mkdir "$WORK/content"
    unzip -q "$WORK/$PACKAGE_NAME" -d "$WORK/content"
    # Only the manifest changes; the binaries are the same build.
    sed -E -i.bak "s/(\"version\"[[:space:]]*:[[:space:]]*\")[^\"]*(\")/\1${VERSION}\2/" "$WORK/content/emberdrop.json"
    rm "$WORK/content/emberdrop.json.bak"
    rm "$WORK/$PACKAGE_NAME"
    (cd "$WORK/content" && zip -qr "$WORK/$PACKAGE_NAME" .)
fi

mkdir -p "$WATCHED"
# Stage next to the destination (same file system, and not matching the watcher's *.emberdrop filter), then rename.
cp "$WORK/$PACKAGE_NAME" "$WATCHED/.$PACKAGE_NAME.tmp"
mv "$WATCHED/.$PACKAGE_NAME.tmp" "$WATCHED/$PACKAGE_NAME"

BUILT_VERSION="$(unzip -p "$WATCHED/$PACKAGE_NAME" emberdrop.json | sed -nE 's/.*"version"[[:space:]]*:[[:space:]]*"([^"]*)".*/\1/p' | head -1)"
echo "Package: $WATCHED/$PACKAGE_NAME ($(du -h "$WATCHED/$PACKAGE_NAME" | cut -f1)), plugin version $BUILT_VERSION"
