# MouseBridge

**One keyboard and mouse for two computers, directly over Bluetooth.** No shared Wi-Fi, internet connection, VPN or relay server is needed for device-to-device communication.

[한국어 안내](README.ko.md) · [Website / Downloads](https://youngwooshin.github.io/mousebridge) · [MIT license](LICENSE) · [Test results](TEST-RESULTS.md)

## Platforms and downloads

Get the current **v0.3.0** downloads on the [website](https://youngwooshin.github.io/mousebridge/#downloads). Windows ZIP contains only `MouseBridge.exe`; Mac ZIP contains `MouseBridge.app` (Apple silicon + Intel). Source and SHA-256 checksums are provided alongside them.

| Pairing | Implementation | Verification |
| --- | --- | --- |
| Windows → Windows | Main and Secondary, custom shortcuts, edge handoff, text clipboard | Earlier user hardware tests confirmed input sharing; new indicator UI still requires Windows testing |
| Windows ↔ Mac | Both Main/Secondary roles implemented using common RFCOMM protocol | Universal Mac cross-build completed; **not hardware-tested** |
| Mac → Mac | Same Mac app on both computers, one Main and one Secondary | **Not hardware-tested** |

This is an experimental, unsigned/ad-hoc signed prototype, not a security-reviewed remote administration product. macOS support is an experimental port with feature gaps, not feature parity. See [Mac instructions](macos/README.md) and [security](SECURITY.md).

## Windows quick start

1. Pair your computers in Windows/macOS Bluetooth settings if needed.
2. Run the app on both. On Windows, extract and run **MouseBridge.exe**. No installer, separate .NET install or administrator launch is required.
3. Choose **Secondary PC** on the receiver, then **Main PC** on the computer with the keyboard/mouse. Windows Main discovers the paired receiver; choose once if several are available.
4. On Windows Main, drag the **Screen arrangement** cards to match the desk and **Save screen settings**.
5. Move through the facing outer edge's **middle 80%** to cross computers. Top/bottom 10% remain local. Entry height is mapped proportionally; local-monitor seams do not switch PCs. Release keys/buttons before automatic crossing.
6. **F8** also switches and returns (some keyboards need Fn+F8). On Windows Main, **Switch shortcut** saves a custom binding. F1–F24 and supported modifier combinations are available; reserved/unsafe combinations are rejected.
7. Copy → switch → paste shares plain Unicode text, up to 256KiB UTF-8. Files, images and formatting are not shared. Switching can transfer sensitive clipboard text.
8. Minimize to keep running. **Stop** ends sharing and automatic reconnect; closing exits.

## A quieter edge indicator

The source edge shows a click-through, non-activating inward gradient with a fine color rail and faded ends while control is on the other computer. On **each Windows PC**, set:

- Width slider: **8–120 physical pixels**, default 50.
- Opacity slider: **0–100%**.
- Six recommended colors, HEX input, or the native **Choose color** dialog.
- Live preview and **Show edge indicator**. Off/zero opacity hides it without disabling edge crossing.

Choose **Save screen settings** to apply. Arrangement is owned by Main; appearance is local. Existing edge settings without a width remain compatible and default to 50 pixels. A 600ms settling interval reduces immediate bounce. The control/clipboard handoff can still have Bluetooth latency.

Settings/logs: `%LOCALAPPDATA%\MouseBridge`. The self-contained Windows app extracts its runtime cache under the Windows temporary `.net` directory on first launch. It is not a zero-disk-write app. Keyboard contents and clipboard text are not logged; manually sent diagnostic messages are logged.

## Mac quick start

See [macos/README.md](macos/README.md). macOS 13+, Accessibility and (on Main) Input Monitoring permissions are required. Pair first; choose the paired device and connect manually on Mac Main. The receiver asks to approve the incoming paired computer. F8 is fixed; layout uses a selector. Mac lacks automatic reconnect, custom shortcut UI, Windows-style indicator editing and deferred fast paste. Korean/other IMEs and physical keyboard layouts require hardware testing.

## Build and test

Windows (.NET 8 SDK; Windows SDK references restore from NuGet):

```powershell
dotnet build MouseBridge.sln -c Release
dotnet test tests/MouseBridge.Tests -c Release
./scripts/Build-Portable.ps1
```

macOS (Xcode command line tools):

```sh
./macos/build.sh
```

The Mac app uses native AppKit, IOBluetooth and CoreGraphics. The common wire format retains a transport interface on Windows; no WebSocket relay is implemented. Both current apps use input protocol 3.

**Verified here:** Windows Release build with 0 warnings/errors and **111 passing xUnit tests**; compressed self-contained publish; portable Mac C wire tests under AddressSanitizer/UndefinedBehaviorSanitizer; Linux cross-compilation/linking of both Mac architectures and universal package inspection. **Not verified here:** actual Windows new UI/color dialog/DPI and Mac app launch, TCC permissions, Bluetooth interoperability/input/IME behavior. Added CI is a build definition, not proof of a completed CI run.

## Scope and contributing

General-user desktop apps only. Elevated apps, UAC/secure desktops, lock/login screens and Mac secure-input contexts are outside scope. Watchdogs and input release are best effort; forced termination can leave input states. Use only trusted paired devices and follow workplace policy. No telemetry, cloud relay or account system is part of MouseBridge.

Contributions and sanitized hardware reports are welcome: [CONTRIBUTING.md](CONTRIBUTING.md). No private development logs or historical binaries are included in the public source snapshot.

Original code is [MIT licensed](LICENSE). Third-party runtimes retain their licenses: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
