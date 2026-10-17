#!/bin/sh
# Builds MandelSwift.app + DMG for publishing. Usage: make-app-dmg.sh <version>
# (version must match Sources/MandelCore/AppVersion.swift).
# Build happens on SSD scratch: the repo lives on NAS (slow disks).
set -e
cd "$(dirname "$0")/.."
VER="${1:?usage: make-app-dmg.sh <version>}"
SCRATCH="${TMPDIR}mandel-swift-build"
BIN="$SCRATCH/arm64-apple-macosx/release/MandelSwift"
APP="dist/MandelSwift.app"
STAGE="dist/stage"

swift build -c release --scratch-path "$SCRATCH"
rm -rf "$APP" "$STAGE" dist/*.dmg
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/"
sed "s/__VERSION__/$VER/g" packaging/Info.plist > "$APP/Contents/Info.plist"
cp ../macos-avalonia/source/Assets/app.icns "$APP/Contents/Resources/AppIcon.icns"
codesign --force --deep --sign - "$APP"
codesign --verify "$APP"
mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
hdiutil create -volname "MandelSwift $VER" -srcfolder "$STAGE" -ov -format UDZO \
  "dist/MandelSwift-$VER-arm64.dmg"
rm -rf "$STAGE"
ls -la dist/
