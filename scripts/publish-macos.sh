#!/usr/bin/env bash
# Publish a self-contained, single-file macOS (Apple Silicon) build of CleanSweep
# and bundle it into a proper CleanSweep.app. No .NET install required to run.
#
# Output: publish/CleanSweep.app
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="$ROOT/src/CleanSweep/CleanSweep.csproj"
RID="osx-arm64"
PUBLISH="$ROOT/publish/$RID"
APP="$ROOT/publish/CleanSweep.app"

echo "Publishing CleanSweep for $RID (self-contained, single file)..."
dotnet publish "$PROJ" -c Release -r "$RID" --self-contained \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=none -p:DebugSymbols=false \
    -o "$PUBLISH"

# Drop stray native debug symbols.
find "$PUBLISH" -name '*.pdb' -delete

echo "Assembling CleanSweep.app bundle..."
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# The published payload (single-file executable + any side-by-side assets).
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/CleanSweep"

# Best-effort icon: convert the bundled .ico to .icns if sips is available.
ICON_SRC="$ROOT/src/CleanSweep/Assets/avalonia-logo.ico"
if [[ -f "$ICON_SRC" ]] && command -v sips >/dev/null 2>&1; then
  sips -s format icns "$ICON_SRC" --out "$APP/Contents/Resources/CleanSweep.icns" >/dev/null 2>&1 || true
fi

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>                  <string>CleanSweep</string>
    <key>CFBundleDisplayName</key>           <string>CleanSweep</string>
    <key>CFBundleIdentifier</key>            <string>com.cleansweep.app</string>
    <key>CFBundleVersion</key>               <string>1.0.0</string>
    <key>CFBundleShortVersionString</key>    <string>1.0.0</string>
    <key>CFBundlePackageType</key>           <string>APPL</string>
    <key>CFBundleExecutable</key>            <string>CleanSweep</string>
    <key>CFBundleIconFile</key>              <string>CleanSweep.icns</string>
    <key>LSMinimumSystemVersion</key>        <string>11.0</string>
    <key>NSHighResolutionCapable</key>       <true/>
    <key>NSPrincipalClass</key>              <string>NSApplication</string>
</dict>
</plist>
PLIST

echo ""
echo "Done. App bundle: $APP"
echo "Run it with:  open \"$APP\""
echo "(First launch: right-click -> Open, since the bundle isn't code-signed.)"
