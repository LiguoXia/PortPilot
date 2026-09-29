#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
rid="${1:-osx-arm64}"
version="${2:-1.1.0}"
case "$rid" in osx-x64|osx-arm64) ;; *) echo "Usage: $0 [osx-x64|osx-arm64] [version]" >&2; exit 2;; esac
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "Expected a numeric release version" >&2; exit 2; }
[[ "$(uname -s)" == Darwin ]] || { echo "App packaging requires macOS (codesign, iconutil and hdiutil)." >&2; exit 2; }
out="$PWD/artifacts/macos/$rid"
app="$out/PortPilot.app"
# Do not reuse an old bundle: stale DLLs can invalidate signatures and produce mixed releases.
[[ ! -e "$out" ]] || { echo "Output already exists: $out. Choose a clean checkout/output before packaging." >&2; exit 2; }
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
dotnet publish src/PortPilot.Mac/PortPilot.Mac.csproj -c Release -r "$rid" --self-contained true \
  -p:Version="$version" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -o "$app/Contents/MacOS"
cat > "$app/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>PortPilot</string>
<key>CFBundleDisplayName</key><string>PortPilot</string>
<key>CFBundleIdentifier</key><string>io.github.liguoxia.portpilot</string>
<key>CFBundleExecutable</key><string>PortPilot.Mac</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>$version</string>
<key>CFBundleVersion</key><string>$version</string>
<key>CFBundleIconFile</key><string>PortPilot.icns</string>
<key>NSHighResolutionCapable</key><true/>
<key>LSMinimumSystemVersion</key><string>12.0</string>
<key>NSPrincipalClass</key><string>NSApplication</string>
</dict></plist>
EOF
printf 'APPL????' > "$app/Contents/PkgInfo"
iconset="$out/PortPilot.iconset"
mkdir -p "$iconset"
sips -s format png src/PortPilot/Resources/PortPilot.ico --out "$out/icon.png" >/dev/null
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$out/icon.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z "$double" "$double" "$out/icon.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/PortPilot.icns"
chmod +x "$app/Contents/MacOS/PortPilot.Mac"
# Local/ad-hoc signing supports Apple Silicon execution. It is not Developer ID signing or notarization.
find "$app/Contents/MacOS" -type f \( -name '*.dylib' -o -name '*.so' \) -print0 | while IFS= read -r -d '' file; do codesign --force --sign - "$file"; done
codesign --force --sign - "$app/Contents/MacOS/PortPilot.Mac"
codesign --force --sign - "$app"
codesign --verify --deep --strict --verbose=2 "$app"
plutil -lint "$app/Contents/Info.plist"
archive="PortPilot-$rid"
ditto -c -k --sequesterRsrc --keepParent "$app" "$out/$archive.zip"
stage="$out/dmg"
mkdir -p "$stage"
ditto "$app" "$stage/PortPilot.app"
ln -s /Applications "$stage/Applications"
cp docs/MACOS.md "$stage/README.md"
hdiutil create -volname PortPilot -srcfolder "$stage" -ov -format UDZO "$out/$archive.dmg"
(cd "$out" && shasum -a 256 "$archive.zip" "$archive.dmg" > "$archive.sha256")
echo "Packaged $rid: $out"
