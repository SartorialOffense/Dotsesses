#!/bin/bash
# Build and package Dotsesses as a macOS .app bundle, then code-sign it.
#
# Signing identity is taken from the SIGNING_IDENTITY env var. Default is "-",
# which means ad-hoc signing (no Apple Developer account required, but friends
# will see a Gatekeeper warning on first launch). To sign for distribution,
# set SIGNING_IDENTITY to your "Developer ID Application: ..." identity name.
# See SIGNING.md for the full path from ad-hoc → notarized.

set -euo pipefail

APP_NAME="Dotsesses"
OUTPUT_DIR="./publish"
APP_BUNDLE="$OUTPUT_DIR/$APP_NAME.app"
SIGNING_IDENTITY="${SIGNING_IDENTITY:--}"

echo "Building $APP_NAME for macOS arm64..."

rm -rf "$OUTPUT_DIR"

dotnet publish Dotsesses/Dotsesses.csproj \
    -c Release \
    -r osx-arm64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$OUTPUT_DIR/bin"

echo "Creating .app bundle..."

mkdir -p "$APP_BUNDLE/Contents/MacOS"
mkdir -p "$APP_BUNDLE/Contents/Resources"

cp -R "$OUTPUT_DIR/bin/"* "$APP_BUNDLE/Contents/MacOS/"
cp "Dotsesses/Info.plist" "$APP_BUNDLE/Contents/"
cp "Dotsesses/Assets/dotsesses.icns" "$APP_BUNDLE/Contents/Resources/"
chmod +x "$APP_BUNDLE/Contents/MacOS/$APP_NAME"

rm -rf "$OUTPUT_DIR/bin"

echo "Signing with identity: $SIGNING_IDENTITY"

# Ad-hoc signing uses --deep to recursively sign nested binaries in one pass.
# --deep is deprecated for hardened-runtime / notarization workflows; see
# SIGNING.md for the inside-out approach required at stage 3.
codesign --force --deep --sign "$SIGNING_IDENTITY" --timestamp=none "$APP_BUNDLE"

echo "Verifying signature..."
codesign --verify --verbose=2 "$APP_BUNDLE"

ditto -c -k --sequesterRsrc --keepParent "$APP_BUNDLE" "$OUTPUT_DIR/$APP_NAME.zip"

echo ""
echo "App bundle: $APP_BUNDLE"
echo "Zip:        $OUTPUT_DIR/$APP_NAME.zip"
echo ""
echo "Run with:   open '$APP_BUNDLE'"
