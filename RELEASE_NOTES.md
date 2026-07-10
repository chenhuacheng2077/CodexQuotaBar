# Codex Quota Bar 1.0.1

## Fixed

- The quota bar now hides when Codex is closed to the taskbar, matching normal minimization behavior.

## Highlights

- Reads Codex quota data only through the official local `codex app-server` JSON-RPC interface.
- Displays live five-hour and weekly remaining quota, with exact reset timestamps.
- Follows the Codex desktop window across moves, resizes, minimize/restore, and monitors.
- Right-click controls for refresh, position, visibility, and display options.
- Optional **随 Codex 启动** mode: starts for the current Windows user, remains hidden until Codex appears, then attaches automatically.

## Installation

Download `CodexQuotaBar.exe` from the release assets and run it while Codex Desktop is installed and signed in. No administrator permission or API key is required.

## Privacy

The app never reads browser cookies, captures traffic, uses OCR, injects DLLs, or changes Codex files. Logs exclude credentials and tokens.
