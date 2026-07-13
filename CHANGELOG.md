# Changelog

## 1.1.0 - 2026-07-13

- Adapt to the current Codex rate-limit payload: when the 5-hour (`secondary`) window is absent/null, the bar shows only the windows Codex returns (currently weekly-only on Plus).
- Render quota windows dynamically instead of hardcoding 5-hour + weekly slots, so a future dual-window or monthly layout still works.
- Surface plan type and optional credit balance; low remaining quota uses warning/critical colors.
- Show locally calculated token totals for the current calendar month and current Codex session, with a right-click display toggle.
- Give the full-width bar 20 px more room so the project Token suffix and refresh control are never edge-clipped.
- Add request timeouts and pending-request cleanup for `codex app-server` JSON-RPC.
- Narrow window-event hooks, skip own process, dispose process handles, and use non-blocking UI marshaling.
- Theme follows Windows when set to System; theme changes apply immediately.
- Keep the bar owned by the Codex window so other apps can cover it normally; prevent duplicate instances.

## 1.0.4 - 2026-07-12

- Keep the quota bar above Codex only, allowing Chrome, File Explorer, and other windows to cover it normally.
- Prevent duplicate quota-bar instances and exclude companion windows from Codex window discovery.

## 1.0.3 - 2026-07-11

- Serialize JSON-RPC writes to prevent the lightweight build from getting stuck at “data unavailable” during concurrent startup refreshes.

## 1.0.2 - 2026-07-10

- Compressed the self-contained Windows EXE from 162 MB to about 72 MB.
- Added an optional runtime-required EXE for users who already have the .NET 8 Windows Desktop Runtime.

## 1.0.1 - 2026-07-10

- Hide the quota bar when Codex closes to the taskbar, then restore it only when Codex is visible again.

## 1.0.0 - 2026-07-10

- Initial Windows release with official Codex app-server quota reads.
- Compact, window-following quota bar for five-hour and weekly limits.
- Context-menu settings, tray controls, low-quota notifications, and optional automatic appearance when Codex starts.
