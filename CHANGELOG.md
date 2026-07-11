# Changelog

## 1.0.3 - 2026-07-11

- Serialize JSON-RPC writes to prevent the lightweight build from getting stuck at “data unavailable” during concurrent startup refreshes.

## 1.0.2 - 2026-07-10

- Compressed the self-contained Windows EXE from 162 MB to about 72 MB.
- Added an optional runtime-required EXE for users who already have .NET 8 Windows Desktop Runtime.

## 1.0.1 - 2026-07-10

- Hide the quota bar when Codex closes to the taskbar, then restore it only when Codex is visible again.

## 1.0.0 - 2026-07-10

- Initial Windows release with official Codex app-server quota reads.
- Compact, window-following quota bar for five-hour and weekly limits.
- Context-menu settings, tray controls, low-quota notifications, and optional automatic appearance when Codex starts.
