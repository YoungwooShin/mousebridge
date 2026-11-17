#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
if [[ $(uname -s) != Darwin ]]; then echo 'Build requires macOS 13+ and Xcode command line tools.' >&2; exit 1; fi
out="${1:-$PWD/artifacts}"
mkdir -p "$out/MouseBridge.app/Contents/MacOS" "$out/MouseBridge.app/Contents/Resources"
cc -std=c11 -Wall -Wextra -Werror tests/wire_test.c -o "$out/wire-tests"
"$out/wire-tests"
xcrun clang -fobjc-arc -fblocks -Wall -Wextra -Wno-unused-parameter -Wno-deprecated-declarations tests/bluetooth_test.m MouseBridge/BluetoothLink.m -framework Foundation -framework IOBluetooth -framework CoreBluetooth -o "$out/bluetooth-tests"
"$out/bluetooth-tests"
for arch in arm64 x86_64; do
  xcrun clang -arch "$arch" -mmacosx-version-min=13.0 -fobjc-arc -fblocks -Wall -Wextra -Wno-unused-parameter -Wno-deprecated-declarations MouseBridge/main.m MouseBridge/BluetoothLink.m MouseBridge/InputEngine.m -framework Cocoa -framework IOBluetooth -framework ApplicationServices -framework QuartzCore -o "$out/MouseBridge-$arch"
done
lipo -create "$out/MouseBridge-arm64" "$out/MouseBridge-x86_64" -output "$out/MouseBridge.app/Contents/MacOS/MouseBridge"
cp Info.plist "$out/MouseBridge.app/Contents/Info.plist"
cp ../LICENSE "$out/MouseBridge.app/Contents/Resources/LICENSE"
codesign --force --deep --sign - "$out/MouseBridge.app"
codesign --verify --deep --strict "$out/MouseBridge.app"
lipo -info "$out/MouseBridge.app/Contents/MacOS/MouseBridge"
ditto -c -k --sequesterRsrc --keepParent "$out/MouseBridge.app" "$out/MouseBridge-macOS-universal-v0.3.4.zip"
shasum -a 256 "$out/MouseBridge-macOS-universal-v0.3.4.zip"
