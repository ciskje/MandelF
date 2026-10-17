#!/bin/sh
# Builds MandelF-avalonia.app + DMG for publishing. Usage: make-app-dmg.sh <version>
# (version must match <Version> in source/MandelbrotViewer.csproj).
set -e
cd "$(dirname "$0")/.."
VER="${1:?usage: make-app-dmg.sh <version>}"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
STAGE="${TMPDIR}mandel-avalonia-stage"
PUB="${TMPDIR}mandel-avalonia-pub"
APP="$STAGE/MandelF-avalonia.app"
DMG="MandelF-avalonia-$VER-arm64.dmg"

dotnet publish source -c Release -r osx-arm64 --self-contained \
  -p:PublishSingleFile=true -p:ArtifactsPath="$TMPDIR/mandel-avalonia-build" \
  -o "$PUB"
rm -rf "$STAGE" "dist/$DMG"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUB"/* "$APP/Contents/MacOS/"
# .pdb/.DS_Store/._ sidecars must not ship: they break the --deep seal
# (Gatekeeper "damaged") and are useless on macOS.
rm -f "$APP"/Contents/MacOS/*.pdb
find "$APP" -name "._*" -delete -o -name ".DS_Store" -delete
sed "s/__VERSION__/$VER/g" packaging/Info.plist > "$APP/Contents/Info.plist"
cp source/Assets/app.icns "$APP/Contents/Resources/AppIcon.icns"
codesign --force --deep --sign - "$APP"
codesign --verify "$APP"
mkdir -p "$STAGE"
ln -s /Applications "$STAGE/Applications"
hdiutil create -volname "MandelF-avalonia $VER" -srcfolder "$STAGE" -ov -format UDZO \
  "${TMPDIR}$DMG"
rm -rf "dist/MandelF-avalonia.app" "dist/$DMG"
cp -R "$APP" dist/
cp "${TMPDIR}$DMG" dist/
rm -rf "$STAGE" "$PUB" "${TMPDIR}$DMG"
ls -la dist/
