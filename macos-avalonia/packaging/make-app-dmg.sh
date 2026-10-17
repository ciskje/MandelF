#!/bin/sh
# Builds MandelbrotViewer.app + DMG for publishing. Usage: make-app-dmg.sh <version>
# (version must match <Version> in source/MandelbrotViewer.csproj).
set -e
cd "$(dirname "$0")/.."
VER="${1:?usage: make-app-dmg.sh <version>}"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
APP="dist/MandelbrotViewer.app"
STAGE="dist/stage"

dotnet publish MandelbrotViewer -c Release -r osx-arm64 --self-contained \
  -p:PublishSingleFile=true -o published
rm -rf "$APP" "$STAGE" dist/*.dmg
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R published/* "$APP/Contents/MacOS/"
sed "s/__VERSION__/$VER/g" packaging/Info.plist > "$APP/Contents/Info.plist"
cp source/Assets/app.icns "$APP/Contents/Resources/AppIcon.icns"
codesign --force --deep --sign - "$APP"
codesign --verify "$APP"
mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
hdiutil create -volname "MandelbrotViewer $VER" -srcfolder "$STAGE" -ov -format UDZO \
  "dist/MandelbrotViewer-$VER-arm64.dmg"
rm -rf "$STAGE"
ls -la dist/
