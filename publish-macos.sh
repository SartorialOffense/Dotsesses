#!/bin/bash
# Build and package Dotsesses as a macOS app bundle

set -e

APP_NAME="Dotsesses"
BUNDLE_ID="com.dotsesses.app"
VERSION="1.0.0"
OUTPUT_DIR="./publish"
APP_BUNDLE="$OUTPUT_DIR/$APP_NAME.app"

echo "Building $APP_NAME for macOS arm64..."

# Clean previous build
rm -rf "$OUTPUT_DIR"

# Publish the application
dotnet publish Dotsesses/Dotsesses.csproj \
    -c Release \
    -r osx-arm64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$OUTPUT_DIR/bin"

echo "Creating app bundle structure..."

# Create the .app bundle structure
mkdir -p "$APP_BUNDLE/Contents/MacOS"
mkdir -p "$APP_BUNDLE/Contents/Resources"

# Copy the executable and dependencies
cp -R "$OUTPUT_DIR/bin/"* "$APP_BUNDLE/Contents/MacOS/"

# Copy Info.plist
cp "Dotsesses/Info.plist" "$APP_BUNDLE/Contents/"

# Copy the icon
cp "Dotsesses/Assets/dotsesses.icns" "$APP_BUNDLE/Contents/Resources/"

# Make executable
chmod +x "$APP_BUNDLE/Contents/MacOS/$APP_NAME"

# Clean up the intermediate bin folder
rm -rf "$OUTPUT_DIR/bin"

codesign --force --deep --sign ./publish/Dotsesses.app
ditto -c -k --sequesterRsrc --keepParent ./publish/Dotsesses.app ./publish/Dotsesses.zip

echo ""
echo "App bundle created: $APP_BUNDLE"
echo ""
echo "To install, drag the app to /Applications or run:"
echo "  cp -R '$APP_BUNDLE' /Applications/"
echo ""
echo "To run directly:"
echo "  open '$APP_BUNDLE'"
