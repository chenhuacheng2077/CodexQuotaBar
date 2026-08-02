# Codex Quota Bar Design

## Intent
An unobtrusive companion bar that reads as part of the Codex desktop surface, never as a dashboard.

## Tokens
- Background: `#202123` dark / `#FFFFFF` light
- Text: `#ECECF1` dark / `#202123` light
- Muted: `#A7A7B4` dark / `#6B6B75` light
- Accent: `#10A37F`
- Warning: `#F59E0B`
- Critical: `#EF4444`
- Height: 34 px; width: 360–640 px; horizontal padding: 12 px; corner radius: 8 px

## Components
- Left: compact **Codex** title plus muted status (`更新于 …` / connection note / plan type).
- Center: **dynamic** quota groups (one per returned window), separated by a subtle divider. No empty “5-hour” placeholder when Codex omits that window.
- Each group: 76 px progress rail + label (`每周  96%  3天后重置` style).
- Optional credits group when balance &gt; 0.
- Optional token group: compact `Token 本月 12.3M · 会话 4.5M`; tooltip exposes exact totals and the active project name.
- Refresh control on the right; system tray mirrors primary actions.

## Quota windows
Labels are duration-based, not fixed slots:

- ~5 hours → 5小时
- ~1 day → 每天
- ~7 days → 每周
- ~30 days → 每月
- otherwise → N小时 / N天 / N分钟

Reset copy uses relative time under 24 hours, otherwise local date/time.

## Token totals
- 本月: sum of `last_token_usage.total_tokens` events whose timestamps fall in the local calendar month.
- 会话: cumulative `total_token_usage.total_tokens` from the most recently written saved session with a known workspace; this avoids relying solely on stale global workspace state.
- 本月: cumulative counter deltas from all session files in the current calendar month, avoiding duplicate `last_token_usage` events.
- Compact values use K / M / B in the bar; exact comma-separated values remain available in the tooltip.
- Below 520 px, the bar enters compact mode: hide title/status, rails, and reset copy; keep each quota percentage plus abbreviated month/session Token totals. Full details remain in the tooltip.

## Accessibility
High-contrast text via theme colors, non-color percentage labels, tooltip with exact quota state (used %, remaining %, absolute reset time). Progress fill shifts to warning/critical near thresholds without relying on color alone.
