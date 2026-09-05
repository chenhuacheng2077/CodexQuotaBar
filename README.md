# Codex Quota Bar

Windows 10/11 companion bar for Codex Desktop. Quota comes from the official local `codex app-server` JSON-RPC interface. Token totals are calculated locally from Codex's saved usage counters and workspace paths; nothing is uploaded.

# now like this
<img width="1661" height="256" alt="4b65124cc9aaa19310bed6533fcaadf1" src="https://github.com/user-attachments/assets/8b906345-a715-4c8b-92d1-e04c86cf5ffd" />

## Quota model (2026-09)

Codex can change which rate-limit windows it returns. This app **does not hardcode “5-hour + weekly”**.

It renders the windows from the `codex` limit pool returned by
`account/rateLimits/read` (and `account/rateLimits/updated`). Newer Codex
versions may also return auxiliary pools such as `base_model_inference`; those
are intentionally excluded because they are not the Plus Codex quota shown in
ChatGPT Settings:

| Situation | Bar behavior |
|-----------|----------------|
| Weekly only (`primary` ≈ 10080 min, `secondary` null) | One group, labeled **每周** |
| 5-hour + weekly (legacy dual windows) | Two groups |
| Monthly / other durations | Labeled **每月** / **N天** / **N小时** |
| Credits balance &gt; 0 | Optional **额度币** group |

Live Plus sample observed with Codex CLI `0.130.0-alpha.5`: only weekly primary, `secondary: null`.

## Token totals

- **本月**: tokens consumed by all saved Codex sessions during the current local calendar month.
- **会话**: cumulative `total_token_usage.total_tokens` for the most recently written saved session with a known workspace. This follows the active conversation even when Codex's global workspace state is briefly stale.

The bar uses compact K / M / B values; hover it for exact totals. Monthly totals use cumulative counter deltas to avoid double-counting repeated token events. Quota is polled every 15 seconds and also accepts Codex's live rate-limit notifications. Right-click **显示 Token** to hide or restore this group.

## Run

Download the compressed self-contained `CodexQuotaBar.exe` from GitHub Releases. It needs no separate .NET installation. `CodexQuotaBar-runtime-required.exe` is an optional much smaller build for computers that already have the .NET 8 Windows Desktop Runtime. Keep Codex Desktop running. The bar finds its window, follows moves/resizes, hides while minimized, and stays available in the system tray.

The application discovers the desktop-bundled CLI at `%LOCALAPPDATA%\OpenAI\Codex\bin\codex.exe`; an explicit CLI path can be saved in `%LOCALAPPDATA%\CodexQuotaBar\settings.json` as `CodexExecutablePath`.

Right-click the quota bar and enable **开机启动额度条** to register the lightweight companion in the current user's Windows startup. It stays hidden until it detects a Codex/ChatGPT Codex window, then attaches automatically. Manual hide remains in effect until **显示额度条** is selected from the tray. No administrator permission is required.

The main bar keeps quota values compact. Click **⋯** for exact percentages, reset timestamps, Token totals, update time, and connection status. If the local Codex app-server restarts, the bar keeps the last successful data marked as stale and reconnects automatically.

## Build

```powershell
dotnet restore
dotnet test
dotnet publish src/CodexQuotaBar/CodexQuotaBar.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Sanitized fixtures:

- `fixtures/real-rate-limits.sanitized.json` — current weekly-only payload
- `fixtures/legacy-dual-windows.sanitized.json` — previous 5-hour + weekly payload
