# MouseBridge v0.3.0 verification

## Native GitHub Actions verification (2026-11-16)

[Successful native build run](https://github.com/YoungwooShin/mousebridge/actions/runs/43866604334), source commit `1f73ff9a28fe4191e50f1abf555be37672bd0c8f`:

- Windows hosted runner: Release build 0 warnings/errors, xUnit111 passed, self-contained portable publish/upload succeeded.
- macOS26 hosted runner: C wire tests passed; Apple clang built arm64 and x86_64; lipo created the universal app; Apple's ad-hoc codesign and codesign --verify --deep --strict succeeded.
- The GitHub Pages Mac ZIP now uses that native CI artifact (inner ZIP SHA256 `74b270e5f60b8509bcccff66a2f28cab6b73dd1d0aa8816d26dd1af44c55a998`). No Developer ID signing or notarization.
- No app GUI launch, Accessibility/Input Monitoring consent, Bluetooth radio/interoperability, real input injection or IME test was run on the hosted runners.
- Public GitHub Pages English/Korean HTML and downloads returned HTTP200; local links, ZIP CRC and checksums checked. Browser visual testing remains unverified.

## Earlier Linux verification (historical cross-build)

- Windows .NET Release solution build: **PASS, 0 warnings / 0 errors**.
- Hardware-independent xUnit tests: **111 passed / 0 failed / 0 skipped**. Includes framing, leases, input queue/encoding, clipboard, exact shortcut handling, edge geometry/proportional height, settings color/opacity/width validation and old settings defaulting to width50.
- Windows compressed, self-contained single-file publish: PASS. One external `MouseBridge.exe`. 230 embedded bundle entries decompressed/checked; app assembly matches build; included .NET frameworks, WPF/WinRT/SDK libraries, extraction flag and no PDBs verified. ZIP CRC and EXE hash checks passed.
- Design agent implemented/reviewed soft gradient, thin rail, end fade, width/opacity sliders, six color presets, native color dialog and live preview. XAML compilation/name/handler and English app-source checks passed. This is not a Windows visual test.
- Portable Mac C wire test executable: PASS under AddressSanitizer/UndefinedBehaviorSanitizer. Checks endian encoding, all accepted frame kinds, length bounds, modifier/key mapping, action range/type validation.
- macOS Objective-C/AppKit port **cross-compiled and linked on Linux** for arm64 and x86_64 using Zig0.13 with macOS13.3 SDK. Both target minOS13.0. SDK-only availability warnings appeared; no source-code warnings/errors in final compile.
- Universal Mach-O package: both architectures, executable permissions, Info.plist, linked system frameworks and ZIP CRC inspected. Ad-hoc bundle signed using rcodesign0.29; SHA-256 executable-page hashes, Info.plist hashes and CodeResources envelopes verified independently for both slices. **No Developer ID signature or Apple notarization.**
- `rcodesign verify`'s CMS/trust verification does not pass for this ad-hoc signature (empty CMS / no cryptographic identity). This is not reported as a passed trust check. Apple's native `codesign --verify`, Gatekeeper and actual launch were NOT RUN. Native build script signs/verifies with Apple's tools when run on a Mac.
- English/Korean website: JavaScript syntax, static structure, local link targets and downloadable artifact/checksum integrity checked. Browser visual/interaction QA unavailable because the managed browser runtime was not available.
- Clean public source snapshot excludes original private Git history, development logs, device settings, build caches, binaries and credentials. License and dependency notices retained. This is a bounded publication hygiene check, not a complete security audit.

## Not run / actual-device acceptance checklist

1. Windows new indicator: physical width8/50/120, transparent fade/rail, opacity0/35/100, presets/native color picker/HEX, live preview, saved reload and click-through/no focus stealing at different DPI.
2. Both left/right arrangements; top/bottom excluded10%; matched relative height; no local-monitor-seam switch; edge bounce settling; held-button/key suppression; F8 and custom shortcut; source strip hide/cleanup; minimize/reconnect/link loss.
3. **Windows Main → Mac Secondary**, **Mac Main → Windows Secondary**, **Mac Main → Mac Secondary**: pair, service discovery/advertising, incoming approval on Mac, encryption mode check, connection/timeout/queue limits, real input and lease release. No such pairing has been tested here.
4. Mac first launch/Gatekeeper, ad-hoc signing acceptance, Bluetooth permission prompt, Accessibility/Input Monitoring, app restart after permissions, Fn+F8/media-key settings, actual UI scrolling/layout, stop/quit and minimize.
5. Mac/Windows key semantics: Command↔Ctrl, Option/Alt, both Shift keys, non-US layouts, Korean IME, modifier release, Caps Lock and app-specific shortcuts. Full equivalence is not claimed.
6. Clipboard both directions by edge and F8: Korean/emoji/newlines, no-text preservation, max256KiB, concurrent local copy protection. On Mac wait for transfer completion before pasting; fast-paste deferral is Windows-only.
7. Mac monitor coordinate mapping/Retina/scaling/hotplug and strip placement. The Mac default strip is 50 logical points; Windows widths are physical pixels.
8. Normal-user apps versus unsupported elevated/secure/login contexts. No secure-input bypass or security audit is claimed.

The original cross-build was superseded by the successful native CI Mac artifact listed above. Hosted build verification is not real-device Bluetooth or GUI testing. See macos/README.md for a native build.
