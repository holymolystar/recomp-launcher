#!/bin/bash
# Builds "Recomp Launcher.app" into mac/build/, plus a zip of it to share. Needs Xcode (or its command line tools).
#   ./build.sh              a universal app (Apple Silicon and Intel; needs full Xcode)
#   ARCHS=native ./build.sh only for this Mac (works with the command line tools alone)
#   VERSION=2.1.0 ./build.sh
set -euo pipefail
cd "$(dirname "$0")"
VERSION="${VERSION:-2.0.0}"
ARCHS="${ARCHS:-universal}"

if [ "$ARCHS" = "universal" ]; then
    FLAGS=(-c release --arch arm64 --arch x86_64)
else
    FLAGS=(-c release)
fi
swift build "${FLAGS[@]}"
BIN="$(swift build "${FLAGS[@]}" --show-bin-path)/RecompLauncher"

APP="build/Recomp Launcher.app"
rm -rf build
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/Recomp Launcher"
cp games-mac.json "$APP/Contents/Resources/"

# the app icon, made from the launcher's logo
if [ -f ../logo.png ]; then
    ICONSET="build/AppIcon.iconset"
    mkdir -p "$ICONSET"
    for s in 16 32 128 256 512; do
        sips -z $s $s ../logo.png --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
        sips -z $((s * 2)) $((s * 2)) ../logo.png --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
    done
    iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
    rm -rf "$ICONSET"
fi

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key><string>Recomp Launcher</string>
    <key>CFBundleIdentifier</key><string>io.github.holymolystar.recomplauncher</string>
    <key>CFBundleName</key><string>Recomp Launcher</string>
    <key>CFBundleDisplayName</key><string>Recomp Launcher</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>${VERSION}</string>
    <key>CFBundleVersion</key><string>${VERSION}</string>
    <key>CFBundleIconFile</key><string>AppIcon</string>
    <key>LSMinimumSystemVersion</key><string>13.0</string>
    <key>LSApplicationCategoryType</key><string>public.app-category.games</string>
    <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

# an ad-hoc signature, so it runs on Apple Silicon (it isn't notarized: the first start needs Open Anyway)
codesign --force --deep --sign - "$APP"

ditto -c -k --keepParent "$APP" "build/Recomp-Launcher-mac-${VERSION}.zip"
echo "Built $APP and build/Recomp-Launcher-mac-${VERSION}.zip"
