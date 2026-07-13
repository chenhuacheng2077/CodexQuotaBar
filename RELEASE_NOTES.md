# Codex Quota Bar 1.1.0

## Downloads

- `CodexQuotaBar.exe` is the compressed self-contained build. It works without installing .NET.
- `CodexQuotaBar-runtime-required.exe` is the small build for PCs with .NET 8 Windows Desktop Runtime already installed.

## Why this release

ChatGPT Codex recently changed how quota is returned through `codex app-server`. On current Plus accounts the short **5-hour** window is no longer present (`secondary: null`); only a longer window such as **weekly** (`primary.windowDurationMins = 10080`) is returned. Older builds still reserved a 5-hour slot and showed “暂未返回”.

## Fixed / improved

- Dynamic quota groups: show every window Codex returns (one or many). If the 5-hour window comes back later, it appears automatically.
- Labels cover 5-hour / weekly / daily / monthly / generic durations.
- Plan type in the status line; optional credit balance when non-zero.
- Local Token totals for the current calendar month and current session, plus exact values in the tooltip.
- Low-quota progress colors (warning / critical).
- JSON-RPC request timeout (15s) and clean disconnect handling.
- Lighter window tracking and owner-based z-order under the Codex window only.
- System theme follows Windows; theme switches apply without restart.

## Highlights

- Reads Codex quota data only through the official local `codex app-server` JSON-RPC interface.
- Follows the Codex desktop window across moves, resizes, minimize/restore, and monitors.
- Right-click controls for refresh, position, visibility, and display options.
- Optional **随 Codex 启动** mode for the current Windows user.

## Installation

Download `CodexQuotaBar.exe` from the release assets and run it while Codex Desktop is installed and signed in. No administrator permission or API key is required.

## Privacy

Quota is read through local `codex app-server`. Token totals are computed on-device from token counters and workspace paths in `%USERPROFILE%\.codex\sessions`; prompts and responses are not stored or transmitted by this app. It never reads browser cookies, captures traffic, uses OCR, injects DLLs, or changes Codex files. Logs exclude credentials and token counts.
