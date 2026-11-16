# MouseBridge for macOS — experimental preview

Native Objective-C/AppKit port using Apple's IOBluetooth Classic RFCOMM APIs. It uses the same service UUID, bounded length-prefixed framing and version-3 control/clipboard messages as the Windows app. The universal app contains Apple silicon and Intel binaries, targeting macOS 13+.

**Build success does not establish that Bluetooth service advertising, pairing/encryption, TCC permissions or input injection work on real Macs. Neither Mac–Windows nor Mac–Mac has been hardware-tested here.** The downloadable preview was cross-compiled on Linux with Zig 0.13 and macOS SDK headers/framework stubs. Native macOS build instructions and CI are included; that CI has not been run merely by adding its YAML.

## Run

1. Extract `MouseBridge.app` and move it to Applications before granting permissions. Keep its location stable.
2. This is an ad-hoc signed preview, without Developer ID signing or Apple notarization. Review the source/checksum. If macOS blocks it, follow the normal System Settings → Privacy & Security review/Open Anyway flow only if you trust it. Do not disable Gatekeeper, remove quarantine with shell commands, or disable other system protections. Building locally with Xcode command line tools is an alternative.
3. Pair the computers in system Bluetooth settings first. Turn Bluetooth on, and allow MouseBridge Bluetooth access when prompted.
4. Grant **Accessibility** on every Mac. On a Mac used as Main, also grant **Input Monitoring**. Reconnect or restart after granting permissions. Updates/rebuilds can require reauthorization.
5. On the receiving computer choose **Secondary PC**. A Mac receiver asks you to approve each incoming trusted paired computer before enabling input.
6. On the computer with keyboard/mouse choose **Main PC**, select the paired secondary, then **Connect / reconnect**. A Windows Main uses its existing discovery flow. Run Secondary before connecting.
7. Use **F8** to switch and return. Depending on keyboard settings, use Fn+F8 or enable standard function keys. Configurable shortcuts are currently Windows-only.
8. On Main choose Secondary-left/right. Middle 80% of the facing edge crosses; top/bottom corners do not. Changes end the connection and require reconnect. The Mac layout uses a selector rather than Windows' draggable cards.
9. Copy → switch → wait for clipboard status → paste. Plain text only, up to 256KiB UTF-8. Unlike Windows, this preview does not defer an immediate paste while the clipboard is still transferring.
10. **Stop** or Quit ends sharing; minimizing is intended to keep it active but needs real-Mac testing. Reconnect is manual in this preview.

Use one Mac app for all Mac roles: Mac Main → Windows Secondary; Windows Main → Mac Secondary; Mac Main → Mac Secondary. Only the Main computer's keyboard/mouse is shared.

## Key mapping and limitations

Command on a Mac Main maps to Ctrl in the shared wire format; Windows Ctrl maps to Command on a Mac receiver. This keeps standard Copy/Paste and common shortcuts familiar. Mac Control maps to the Windows-logo wire keys, which map back to Control on Mac. Alt maps to Option; Shift remains Shift. Physical key positions/common US keys are mapped, not arbitrary Unicode keystrokes. Korean/other IMEs, non-US keyboards, Caps Lock, Fn/media keys and application-specific shortcuts require separate testing. Some keys are unsupported; no full keyboard-layout equivalence is claimed.

Mac edge indicators currently have a fixed blue gradient/default width and opacity; the width/color/opacity editor is Windows-only. macOS display coordinates are logical points, so its 50-point default strip is not a promise of 50 physical Retina pixels. Windows uses physical pixel widths.

No auto reconnect/startup, custom Mac shortcut UI, clipboard files/images, screen streaming, login-screen control or secure-input bypass. The event tap restores local control if disabled or after heartbeat loss; forced process/OS failure can prevent clean key release. The Mac event loop and asynchronous bounded Bluetooth queue are a prototype and may need latency tuning.

Connections must be paired, authentication must succeed and reported encryption must be enabled. There is no insecure fallback if macOS does not report encryption. The SDP record is for discovery; it is not claimed to enforce security independently. The actual encryption behavior and API reports must be tested on hardware.

## Build on macOS

Install Xcode command line tools, then from repository root:

```sh
./macos/build.sh
```

This runs the portable C wire tests, builds arm64 and x86_64, creates a universal app, ad-hoc signs and verifies it, and packages the ZIP in `macos/artifacts/`. It does not notarize, use a developer certificate or upload anything.

On Linux, only the portable C tests are required for normal development:

```sh
cc -std=c11 -Wall -Wextra -Werror -fsanitize=address,undefined macos/tests/wire_test.c -o /tmp/mb-wire
/tmp/mb-wire
```

CI definition: `.github/workflows/build.yml`. Real-device acceptance tests are in `TEST-RESULTS.md`.
