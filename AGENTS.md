# AGENTS.md

Instructions for AI agents working on the MauiDevFlow codebase.

## Project Overview

MauiDevFlow is a toolkit for AI-assisted .NET MAUI app development. It provides:
- **In-app Agent** (`MauiDevFlow.Agent`) — HTTP API running inside the MAUI app for visual tree inspection, element interaction, screenshots, and logging
- **Agent Core** (`MauiDevFlow.Agent.Core`) — Platform-agnostic agent core (HTTP server, visual tree walker, logging) shared by platform-specific agents
- **Agent GTK** (`MauiDevFlow.Agent.Gtk`) — GTK/Linux-specific agent for Maui.Gtk apps
- **Blazor CDP Bridge** (`MauiDevFlow.Blazor`) — Chrome DevTools Protocol support via Chobitsu for Blazor Hybrid WebView debugging
- **Blazor CDP GTK** (`MauiDevFlow.Blazor.Gtk`) — Blazor CDP bridge for WebKitGTK on Linux
- **CLI Tool** (`MauiDevFlow.CLI`) — Terminal commands for both native MAUI and Blazor automation
- **Driver Library** (`MauiDevFlow.Driver`) — Platform-aware orchestration (Mac Catalyst, Android, iOS, Windows, Linux)
- **AI Skill** (`maui-ai-debugging`) — Host-installable skill files teaching AI agents the full build→deploy→inspect→fix workflow

## Architecture

```
CLI (dotnet global tool) ──HTTP──▶ Agent (runs inside MAUI app, single port)
                                     ├── /api/tree, /api/screenshot, /api/logs, etc.
                                     ├── /api/cdp?webview=<id> ──EvalJS──▶ Chobitsu (per WebView)
                                     └── /api/cdp/webviews ──▶ List registered WebViews

Agent architecture:
  Agent.Core (net10.0) ← platform-agnostic HTTP server, tree walker, logging
    ├── Agent (MAUI TFMs) ← iOS, Android, macCatalyst, Windows platform code
    └── Agent.Gtk (net10.0) ← GTK/Linux platform code (GirCore.Gtk-4.0)
```

- **Single port** (default 9223, configurable via `.mauidevflow` file) serves both native MAUI commands, CDP, and WebSocket connections
- **WebSocket support** — `/ws/network` streams captured HTTP requests in real-time; `/ws/logs` streams log entries; `/ws/sensors?sensor=<name>` streams device sensor readings; CDP still uses HTTP POST via Chobitsu
- **Multi-WebView CDP** — Each `BlazorWebView` registers independently with the agent. CDP commands accept `?webview=<id>` to target by index, AutomationId, or element ID. The `BlazorWebViewDebugServiceBase` manages per-WebView state via inner `WebViewBridge` instances
- **Blazor→Agent wiring** uses reflection to avoid a direct package dependency between the two NuGet packages

## Building & Testing

```bash
# Restore, build, and test (use ci.slnf to avoid building the sample app)
dotnet restore ci.slnf
dotnet build ci.slnf
dotnet test ci.slnf

# Build GTK/Linux-specific projects only (no MAUI workloads needed)
dotnet build src/MauiDevFlow.Agent.Core
dotnet build src/MauiDevFlow.Agent.Gtk
dotnet build src/MauiDevFlow.Blazor.Gtk
dotnet build src/MauiDevFlow.Driver
dotnet build src/MauiDevFlow.CLI

# Build the sample app for Mac Catalyst
dotnet build src/SampleMauiApp -f net10.0-maccatalyst

# Build the sample app for Windows
dotnet build src/SampleMauiApp -f net10.0-windows10.0.19041.0

# Run the locally-built CLI
dotnet run --project src/MauiDevFlow.CLI -- <args>
```

The solution filter `ci.slnf` excludes `SampleMauiApp` (requires MAUI workloads). Use the full `MauiDevFlow.sln` when working on the sample app.

## Key Conventions

- **Always verify changes with the sample app** — build, deploy (Mac Catalyst is fastest on macOS, Windows on Windows), and test end-to-end
- **Do NOT auto-commit or push** unless explicitly asked
- **`#if DEBUG` only** — Agent and Blazor packages are debug-only tools, never ship in release builds
- **Reflection for cross-package wiring** — `BlazorDevFlowExtensions.WireAgentCdp()` connects Blazor→Agent via reflection to avoid NuGet dependency
- **Embedded JS resources** — Scripts in `Resources/Scripts/*.js` are embedded at build time and loaded via `ScriptResources.Load()`
- **Port configuration** — `.mauidevflow` JSON file in project dir, read by both MSBuild targets and CLI

## NuGet Packaging

Six packages are published on release:
- `Redth.MauiDevFlow.Agent` — In-app agent (MAUI library, references Agent.Core)
- `Redth.MauiDevFlow.Agent.Core` — Platform-agnostic agent core (net10.0 library)
- `Redth.MauiDevFlow.Agent.Gtk` — GTK/Linux agent (net10.0 library, references Agent.Core + GirCore)
- `Redth.MauiDevFlow.Blazor` — Blazor CDP bridge (MAUI Razor library)
- `Redth.MauiDevFlow.Blazor.Gtk` — Blazor CDP bridge for WebKitGTK (net10.0 library)
- `Redth.MauiDevFlow.CLI` — Global dotnet tool

The Driver library (`Redth.MauiDevFlow.Driver`) conditionally references `Interop.UIAutomationClient` on Windows for UIA-based dialog detection.

**Versioning:** `Directory.Build.props` has the shared version for Agent/Blazor. CLI has its own version in its `.csproj`. The publish workflow uses the Git tag as `PackageVersion`, overriding both.

**Release process:** Create a GitHub release with tag `vX.Y.Z` → triggers `publish.yml` → builds, tests, packs, and pushes to NuGet.org.

## Skill System

The canonical skill source lives in `.claude/skills/maui-ai-debugging/` and is installable for
multiple hosts:
- `SKILL.md` — Main skill document with full command reference and workflows
- `references/setup.md` — Detailed setup guide
- `references/*.md` — Platform-specific guides

The CLI command `maui-devflow update-skill --host <claude|codex>` downloads the latest skill files
from GitHub into the host-specific install path (`.claude/...` for Claude, `.agents/...` for
Codex). When updating skill docs, keep `SKILL.md` as the authoritative command reference.

## Logging Architecture

- **Native logs**: `ILogger` → `FileLogProvider` → rotating JSONL files → `/api/logs` endpoint
- **WebView logs**: JS `console.*` → intercepted by `console-intercept.js` → buffered in `window.__webviewLogs` → drained every 2s by native timer → written to same JSONL files with `source: "webview"`
- Log entries have a `source` field (`"native"` or `"webview"`) for filtering via `?source=` query param

## Network Monitoring Architecture

- **Interception**: `DevFlowHttpHandler` (DelegatingHandler) wraps platform-specific handlers (AndroidMessageHandler, NSUrlSessionHandler, etc.)
- **Auto-injection**: `ConfigureHttpClientDefaults` in `AddMauiDevFlowAgent()` registers the handler for all `IHttpClientFactory` clients
- **Non-DI clients**: `DevFlowHttp.CreateClient()` helper wraps `new HttpClient()` with the interceptor
- **Storage**: `NetworkRequestStore` (ConcurrentQueue ring buffer, default 500 entries) with `OnRequestCaptured` event
- **Body capture**: Text bodies up to 256KB (configurable), binary as base64. Truncated bodies flagged
- **REST API**: `/api/network` (list), `/api/network/{id}` (detail), `/api/network/clear` (clear buffer)
- **WebSocket**: `/ws/network` sends replay of buffered history on connect, then streams new entries live
- **CLI**: `MAUI network` (live TUI), `MAUI network --json` (JSONL streaming), `MAUI network list`, `MAUI network detail`, `MAUI network clear`
- **Apple namespace conflict**: Agent.Core's `Network` namespace conflicts with Apple's `Network` framework — use fully-qualified `MauiDevFlow.Agent.Core.Network.DevFlowHttpHandler` in AgentServiceExtensions.cs

## Platform Features (Preferences, SecureStorage, Device Info, Sensors)

- **Preferences CRUD**: `/api/preferences` endpoints for list/get/set/delete/clear. MAUI's `Preferences` API has no "list all" method — the agent tracks known keys via an internal registry key (`__devflow_known_keys`) stored as a unit-separator-delimited string. Only keys set via the agent are tracked.
- **SecureStorage CRUD**: `/api/secure-storage` endpoints for get/set/delete/clear via `SecureStorage` static API.
- **Platform Info**: Read-only endpoints under `/api/platform/` for app-info, device-info, device-display, battery, connectivity, version-tracking, permissions, geolocation. All use MAUI's static API classes (`AppInfo.Current`, `DeviceInfo.Current`, `DeviceDisplay.MainDisplayInfo`, `Battery.Default`, `Connectivity.Current`, `VersionTracking.Default`, `Geolocation.GetLocationAsync()`).
- **Permissions**: The agent maintains a `KnownPermissions` dictionary mapping friendly names to `Permissions.BasePermission` factories. Check one or all permissions via `/api/platform/permissions[/{name}]`.
- **Sensor Streaming**: `SensorManager` class manages sensor lifecycle (start/stop) and WebSocket broadcasting. Supports: accelerometer, barometer, compass, gyroscope, magnetometer, orientation. WebSocket at `/ws/sensors?sensor=<name>` auto-starts the sensor on connect and broadcasts JSON readings to all subscribers. REST endpoints for list/start/stop at `/api/sensors`.
- **DELETE routes**: `AgentHttpServer` supports DELETE method via `_deleteRoutes` dictionary and `MapDelete()`, used by preferences and secure-storage delete endpoints.
- **CLI**: `MAUI preferences`, `MAUI secure-storage`, `MAUI platform`, `MAUI sensors` command groups with full subcommands.

## Windows Support

- **Agent**: Reports `platform: "WinUI"`, `idiom: "Desktop"`. Startup uses `OnActivated` lifecycle event because `Application.Current` is not available during `OnLaunched`.
- **Blazor CDP**: Uses WebView2's `CoreWebView2.ExecuteScriptAsync()`. Results are JSON-decoded via `DecodeWebView2Result`. All WebView2 calls are marshaled to the UI thread.
- **Driver**: `WindowsAppDriver` uses Windows UI Automation (UIA) via `Interop.UIAutomationClient` NuGet package for dialog detection, dismissal, and accessibility tree dumping. Key simulation uses `SendInput` P/Invoke.
- **CLI**: `--platform windows` (or auto-detected via `OperatingSystem.IsWindows()`). PID resolution uses `Process.GetProcessesByName()` instead of `pgrep`.
- **WinUI3 dialogs**: MAUI `DisplayAlert()` renders as child `window` elements inside the main app window. Detection strategy: find child windows containing both buttons and text elements.

## Linux/GTK Support

- **Agent.Core** (`MauiDevFlow.Agent.Core`): Platform-agnostic `net10.0` library containing HTTP server, visual tree walker base, logging, DTOs. Shared by both `Agent` (MAUI TFMs) and `Agent.Gtk` (GTK/Linux).
- **Agent.Gtk** (`MauiDevFlow.Agent.Gtk`): GTK-specific agent using `GirCore.Gtk-4.0`. Native tap via `Gtk.Button.Activate()` / `Gtk.Widget.Activate()`. Native info collects GTK widget name, tooltip, sensitive, visible, type. Screenshots use `Gtk.WidgetPaintable` → `Gdk.Texture.SaveToPng()` fallback.
- **Blazor CDP GTK** (`MauiDevFlow.Blazor.Gtk`): WebKitGTK-based CDP bridge using `GirCore.WebKit-6.0`. JS evaluation via `WebView.EvaluateJavascriptAsync()`. Same Chobitsu injection and CDP polling pattern as other platforms.
- **Driver**: `LinuxAppDriver` extends `AppDriverBase`. Direct localhost connection (no port forwarding). Dialog detection via agent tree inspection. Key simulation via `xdotool`. Process management via `pgrep`.
- **CLI**: `--platform linux` (or auto-detected via `OperatingSystem.IsLinux()`).
- **Integration with Maui.Gtk**: In MauiProgram.cs: `builder.AddMauiDevFlowAgent()`. After app startup: `app.StartDevFlowAgent()`. For Blazor: `builder.AddMauiBlazorDevFlowTools()`.
