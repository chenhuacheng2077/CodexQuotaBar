# Codex Quota Bar

Windows 10/11 companion bar for Codex Desktop. It uses only the official local `codex app-server` JSON-RPC interface; it does not inspect Codex UI, web pages, cookies, screenshots, or traffic.

## Run

Open `src/CodexQuotaBar/bin/Release/net8.0-windows/win-x64/publish/CodexQuotaBar.exe`. Keep Codex Desktop running. The bar finds its window, follows moves/resizes, hides while minimized, and stays available in the system tray.

The application discovers the desktop-bundled CLI at `%LOCALAPPDATA%\OpenAI\Codex\bin\codex.exe`; an explicit CLI path can be saved in `%LOCALAPPDATA%\CodexQuotaBar\settings.json` as `CodexExecutablePath`.

Right-click the quota bar and enable **随 Codex 启动** to register the lightweight companion in the current user's Windows startup. It stays hidden until it detects a Codex/ChatGPT Codex window, then attaches automatically. No administrator permission is required.

## Build

```powershell
dotnet restore
dotnet test
dotnet publish src/CodexQuotaBar/CodexQuotaBar.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The sanitized Phase 1 response fixture is `fixtures/real-rate-limits.sanitized.json`.
