# Security

MouseBridge is an experimental input-sharing tool, not a security-reviewed remote administration product.

Only pair with computers you own or trust. Paired Bluetooth with authentication and encryption is required; macOS also refuses a channel whose reported encryption mode is disabled. macOS asks to approve each incoming connection. The Windows receiver currently relies on its active Secondary role and Bluetooth pairing policy, not a separate per-peer application allowlist. Do not leave Secondary enabled in an untrusted pairing environment.

Keyboard/mouse messages and plain-text clipboard contents cross the Bluetooth connection. Switching can send sensitive clipboard text. Stop sharing when not in use, and follow workplace policy. Logs must not contain captured keys, clipboard contents, or secrets. Diagnostic text sent explicitly by the user is logged in Windows.

Input leases, queue bounds, message validation and best-effort key release reduce failures; they do not provide guaranteed recovery after a crash, OS failure or forced termination. Elevated apps, secure desktops, login screens and secure-input contexts are unsupported.

Report a vulnerability privately using GitHub's private vulnerability reporting feature when enabled. If unavailable, contact the repository owner through an established private channel; do not post exploit details or personal clipboard contents in a public issue. Public bug reports should remove device addresses, names and personal logs.
