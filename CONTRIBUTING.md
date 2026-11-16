# Contributing

Small, focused fixes and hardware compatibility reports are welcome. Open an issue with OS version, Bluetooth adapter/driver, roles and sanitized diagnostics. Do not include clipboard text, passwords, personal PC names or Bluetooth addresses.

Windows: `dotnet build MouseBridge.sln -c Release` and `dotnet test tests/MouseBridge.Tests -c Release`.
macOS: `./macos/build.sh`. Cross-platform wire-only tests: `cc -std=c11 -Wall -Wextra -Werror macos/tests/wire_test.c -o /tmp/mb-wire && /tmp/mb-wire`.

Describe actual hardware tests separately from compilation/unit tests. Changes to input wire protocol must preserve session validation, bounded queues, input release and safe fallback. Contributions are under the repository's MIT license.
