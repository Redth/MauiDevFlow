# MauiDevFlow

Unified tooling for automating and debugging .NET MAUI apps — both native MAUI and Blazor Hybrid.
Built to enable AI agents (and humans) to build, deploy, inspect, and debug MAUI apps entirely
from the terminal.

## What This Is (and Isn't)

MauiDevFlow is designed for **agentic development workflows** — giving AI coding agents full
autonomy over the MAUI dev loop: build, deploy, inspect, interact, diagnose, fix, and repeat.

It is **not** a UI testing framework, and it is **not** meant to ship in your app. The agent and
debug bridge are intended for `#if DEBUG` only. Think of it as giving your AI pair-programmer
eyes and hands inside the running app so it can close the feedback loop on its own — verify its
changes work, see what went wrong when they don't, and iterate without waiting for a human to
manually check the simulator.

## Features

- **Native MAUI Automation** — Visual tree inspection, element interaction (tap, fill, clear), screenshots via in-app Agent
- **Blazor WebView Debugging** — CDP bridge using Chobitsu for JavaScript evaluation, DOM manipulation, page navigation. Supports multiple BlazorWebViews per app with independent targeting
- **Unified Logging** — Native `ILogger` and WebView `console.log/warn/error` unified into a single log stream with source filtering
- **Broker Daemon** — Automatic port assignment and agent discovery for simultaneous multi-app debugging
- **CLI Tool** (`maui-devflow`) — Scriptable commands for both native and Blazor automation
- **Driver Library** — Platform-aware (Mac Catalyst, Android, iOS Simulator, Linux/GTK) orchestration
- **AI Skill** — Claude Code skill (`.claude/skills/maui-ai-debugging`) for AI-driven development workflows

## Quick Start

### AI-Assisted Setup (Recommended)

The fastest way to get started is to let your AI agent set everything up using the included skill:

**1. Install the CLI tool and download the skill:**

```bash
dotnet tool install --global Redth.MauiDevFlow.CLI
maui-devflow update-skill
```

**2. Ask your AI agent to set up MauiDevFlow:**

> Use the maui-ai-debugging skill to integrate MauiDevFlow into my MAUI app.
> Add the NuGet packages, configure MauiProgram.cs, and set up everything
> needed for debugging. Then build, run, and verify it works.

The skill includes a complete setup guide with a step-by-step checklist, platform-specific
configuration (entitlements, port forwarding), and the full command reference. The agent
will handle NuGet packages, code changes, Blazor script tags, and verification.

### Manual Setup

<details>
<summary>Click to expand manual setup steps</summary>

#### 1. Add NuGet Packages

```xml
<PackageReference Include="Redth.MauiDevFlow.Agent" Version="*" />
<PackageReference Include="Redth.MauiDevFlow.Blazor" Version="*" />  <!-- Blazor Hybrid only -->
<!-- For Linux/GTK apps, use Agent.Gtk and Blazor.Gtk instead -->
```

#### 2. Configure MauiProgram.cs

```csharp
// MauiProgram.cs
using MauiDevFlow.Agent;
using MauiDevFlow.Blazor;

var builder = MauiApp.CreateBuilder();
// ... your existing setup ...

#if DEBUG
builder.Services.AddBlazorWebViewDeveloperTools();
builder.AddMauiDevFlowAgent();
builder.AddMauiBlazorDevFlowTools(); // Blazor Hybrid only
#endif
```

**Agent options:** `Port` (default 9223), `Enabled` (default true), `MaxTreeDepth` (0 = unlimited). Port is also configurable via `.mauidevflow` or `-p:MauiDevFlowPort=XXXX`.

**Blazor options:** `Enabled` (default true), `EnableWebViewInspection` (default true), `EnableLogging` (default true in DEBUG). CDP commands are routed through the agent port — no separate Blazor port needed.

Both methods extend `MauiAppBuilder`.

#### 3. Port Configuration (optional)

The CLI includes a **broker daemon** that automatically assigns ports to each running agent —
no manual configuration needed for most workflows. Run `maui-devflow list` to see all connected
agents and their ports.

For explicit port control (or when the broker isn't available), create a `.mauidevflow` in
your project directory:

```json
{ "port": 9347 }
```

Both the build and CLI auto-detect this file — no flags needed. See [broker documentation](docs/broker.md) for details.

#### 3. Blazor Hybrid: Automatic Setup

The `chobitsu.js` debugging library is automatically injected via a Blazor JS initializer — no manual script tag needed. Just add the NuGet package and register in `MauiProgram.cs`.

> **Note:** If auto-injection doesn't work in your setup, you can add the script tag manually in `wwwroot/index.html`:
> ```html
> <script src="chobitsu.js"></script>  <!-- Add before </body> -->
> ```

#### 4. Install CLI Tools

```bash
dotnet tool install --global Redth.MauiDevFlow.CLI
dotnet tool install --global androidsdk.tool    # android (SDK, AVD, device management)
dotnet tool install --global appledev.tools     # apple (simulators, provisioning, certificates)
```

#### 5. Platform-Specific Setup

- **Mac Catalyst:** Add `com.apple.security.network.server` entitlement (see [setup guide](.claude/skills/maui-ai-debugging/references/setup.md#5-mac-catalyst-entitlements))
- **Android Emulator:** Run `adb reverse tcp:19223 tcp:19223` (broker) and `adb forward tcp:<port> tcp:<port>` (agent — get port from `maui-devflow list`)
- **iOS Simulator:** No extra setup needed
- **Linux/GTK:** No extra setup needed (see [Linux guide](.claude/skills/maui-ai-debugging/references/linux.md))

</details>

### Verify It Works

```bash
# Wait for an agent to connect (blocks until ready)
maui-devflow wait

# List all connected agents (via broker)
maui-devflow list

# Check agent connection
maui-devflow MAUI status

# Dump visual tree (all windows)
maui-devflow MAUI tree

# Dump visual tree for a specific window (0-based index)
maui-devflow MAUI tree --window 1

# Take screenshot
maui-devflow MAUI screenshot --output screen.png

# Take screenshot of a specific element
maui-devflow MAUI screenshot --id HeaderLabel --output header.png
maui-devflow MAUI screenshot --selector "Button" --output button.png

# Fetch application logs
maui-devflow MAUI logs --limit 50

# Filter by source (native ILogger or Blazor WebView console)
maui-devflow MAUI logs --source webview
maui-devflow MAUI logs --source native

# Screen recording (start/stop)
maui-devflow MAUI recording start --timeout 30
# ... interact with the app ...
maui-devflow MAUI recording stop

# Live edit native properties (no rebuild)
maui-devflow MAUI set-property HeaderLabel TextColor "Tomato"
maui-devflow MAUI set-property HeaderLabel FontSize "32"

# Blazor WebView (if applicable)
maui-devflow cdp status
maui-devflow cdp snapshot
```

## Blazor WebView CDP Commands

For Blazor Hybrid apps with the CDP bridge configured:

```bash
# Check CDP connection and WebView count
maui-devflow cdp status

# List all registered WebViews (useful when app has multiple BlazorWebViews)
maui-devflow cdp webviews
maui-devflow cdp webviews --json

# Page snapshot (useful for AI agents — accessible text representation of DOM)
maui-devflow cdp snapshot

# Evaluate JavaScript
maui-devflow cdp Runtime evaluate "document.title"

# DOM queries
maui-devflow cdp DOM querySelector "button.btn-primary"
maui-devflow cdp DOM querySelectorAll "a.nav-link"
maui-devflow cdp DOM getOuterHTML "div.container"

# Input actions
maui-devflow cdp Input dispatchClickEvent "button"
maui-devflow cdp Input fill "input#name" "John"
maui-devflow cdp Input insertText "typed text"

# Page navigation
maui-devflow cdp Page navigate "https://example.com"
maui-devflow cdp Page reload
maui-devflow cdp Page captureScreenshot

# Live CSS/DOM editing (experiment without rebuilding)
maui-devflow cdp Runtime evaluate "document.querySelector('h1').style.color = 'tomato'"
maui-devflow cdp Runtime evaluate "document.querySelectorAll('.item').forEach(el => el.style.borderRadius = '20px')"
maui-devflow cdp Runtime evaluate "document.documentElement.style.setProperty('--bg-color', '#1a1a2e')"
maui-devflow cdp Runtime evaluate "document.head.insertAdjacentHTML('beforeend', '<style>.btn { background: hotpink !important; }</style>')"
```

### Multi-WebView Targeting

Apps with multiple `BlazorWebView` controls (e.g. side-by-side views, split layouts) are
fully supported. Each WebView registers independently with the agent using its `AutomationId`.

```bash
# List all WebViews — shows index, AutomationId, and ready status
maui-devflow cdp webviews

# Target a specific WebView by AutomationId
maui-devflow cdp --webview BlazorLeft snapshot
maui-devflow cdp -w BlazorRight Runtime evaluate "document.title"

# Target by index (0-based)
maui-devflow cdp -w 0 Runtime evaluate "1+1"
maui-devflow cdp -w 1 DOM querySelector "h1"
```

Without `--webview`, all CDP commands target the first registered WebView (index 0).
The `--webview` (`-w`) option is available on every `cdp` subcommand.

> **Tip:** Live CSS/DOM edits are immediate and non-destructive — changes are lost on page reload,
> making them safe for experimentation before committing to code.

> **Note:** On iOS/Mac Catalyst, `Page.reload`, `Page.navigate`, and `Input.insertText` are
> intercepted and handled via native WKWebView APIs. This is necessary because these operations
> destroy/recreate the JavaScript context — the Chobitsu debug bridge is automatically
> re-injected after reload/navigation completes.

## Agent API

The Agent runs inside the MAUI app and exposes an HTTP/JSON REST API. The port is
auto-assigned by the broker (range 10223–10899), or configurable via `.mauidevflow` / `--agent-port`.

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/status` | GET | Agent version, platform, app info, window count |
| `/api/tree?depth=N&window=W` | GET | Visual tree as JSON (window: 0-based index) |
| `/api/element/{id}` | GET | Single element by ID |
| `/api/query?type=&text=&automationId=` | GET | Find matching elements |
| `/api/action/tap` | POST | Tap element `{"elementId":"..."}` |
| `/api/action/fill` | POST | Fill text `{"elementId":"...","text":"..."}` |
| `/api/action/clear` | POST | Clear text `{"elementId":"..."}` |
| `/api/action/focus` | POST | Focus element `{"elementId":"..."}` |
| `/api/action/scroll` | POST | Scroll by delta or scroll element into view `{"elementId":"...","deltaX":0,"deltaY":200}` |
| `/api/action/resize?window=W` | POST | Resize window `{"width":800,"height":600}` |
| `/api/screenshot?window=W&id=ID&selector=SEL` | GET | PNG screenshot. Full window, or element by ID/selector |
| `/api/property/{id}/{name}` | GET | Get property value |
| `/api/property/{id}/{name}` | POST | Set property `{"value":"..."}` |
| `/api/logs?limit=N&skip=N&source=S` | GET | Application logs (source: `native`, `webview`, or omit for all) |
| `/api/cdp` | POST | Forward CDP command to Blazor WebView. Use `?webview=<id>` to target a specific WebView |
| `/api/cdp/webviews` | GET | List registered CDP WebViews (index, AutomationId, elementId, ready status) |

## Project Structure

```
MauiDevFlow.sln
.claude/skills/
└── maui-ai-debugging/          # AI skill for Claude Code
src/
├── MauiDevFlow.Agent/          # In-app agent (NuGet library for MAUI apps)
├── MauiDevFlow.Agent.Core/     # Platform-agnostic agent core (shared by Agent & Agent.Gtk)
├── MauiDevFlow.Agent.Gtk/      # GTK/Linux agent (NuGet library for Maui.Gtk apps)
├── MauiDevFlow.Blazor/         # Blazor WebView CDP bridge (Chobitsu)
├── MauiDevFlow.Blazor.Gtk/     # Blazor CDP bridge for WebKitGTK on Linux
├── MauiDevFlow.CLI/            # CLI tool (maui-devflow)
├── MauiDevFlow.Driver/         # Driver library (connects to Agent, platform tools)
└── SampleMauiApp/              # Sample todo app (native + Blazor Hybrid via Shell)
tests/
├── MauiDevFlow.Console/        # Console test app for CDP
└── MauiDevFlow.Tests/          # Unit and integration tests
docs/
├── broker.md                   # Broker daemon architecture and API
└── setup-guides/               # Platform-specific setup (Android, Apple, Windows)
```

### Sample App

The `SampleMauiApp` is a shared-state todo list with both a native MAUI page and a Blazor
Hybrid page, connected via Shell navigation (`//native` and `//blazor` routes). It demonstrates:

- **Shared `TodoService`** — both pages read/write the same data, changes reflect immediately
- **Shell navigation** — switch between tabs via `maui-devflow MAUI navigate "//native"` etc.
- **Dark mode support** — both native (AppThemeBinding) and Blazor (`@media prefers-color-scheme`) adapt to system theme
- **Description field** — todo items support title + optional description on both pages
- **Multi-WebView** — the `//multiblazor` route shows two side-by-side BlazorWebViews (`BlazorLeft` and `BlazorRight`) for testing multi-WebView CDP targeting

## Platform Support

| Platform | Agent | CDP/Blazor | Network Setup |
|----------|-------|------------|---------------|
| Mac Catalyst | ✅ | ✅ | Direct localhost |
| iOS Simulator | ✅ | ✅ | Shares host network |
| Android Emulator | ✅ | 🔄 | `adb reverse` (broker) + `adb forward` (agent) |
| Windows | 🔄 | 🔄 | Direct localhost |
| Linux/GTK | ✅ | ✅ | Direct localhost |

### Companion Tools

| Tool | Install | Purpose |
|------|---------|---------|
| `maui-devflow` | `dotnet tool install -g Redth.MauiDevFlow.CLI` | App automation & Blazor CDP |
| `android` | `dotnet tool install -g androidsdk.tool` | Android SDK, AVD, device management |
| `apple` | `dotnet tool install -g appledev.tools` | iOS simulators, provisioning, certificates |
| `adb` | Android SDK platform-tools | Device communication, port forwarding, logs |
| `xcrun simctl` | Xcode command-line tools | iOS simulator lifecycle |

## AI Agent Integration

This project includes a Claude Code skill (`.claude/skills/maui-ai-debugging`) that teaches AI
agents the complete build → deploy → inspect → fix feedback loop. The skill covers:

- Installing and configuring all required tools
- Building and deploying to iOS simulators, Android emulators, Mac Catalyst, and Linux/GTK
- Using `maui-devflow` to inspect visual trees, interact with elements, and take screenshots
- Using CDP to inspect and manipulate Blazor WebView content
- Automatic port discovery via the broker daemon for multi-app workflows
- Managing simulators/emulators with `apple`, `android`, `xcrun simctl`, and `adb`

### Installing the Skill

The CLI can download the latest skill files directly from GitHub into your project:

```bash
# Interactive — shows files and asks for confirmation
maui-devflow update-skill

# Skip confirmation
maui-devflow update-skill -y

# Download to a specific directory
maui-devflow update-skill -o /path/to/my-project

# Use a different branch
maui-devflow update-skill -b dev
```

This downloads the skill files into `.claude/skills/maui-ai-debugging/` relative to the output
directory (or current directory if `--output` is not specified). Existing files are overwritten.
The file list is discovered dynamically from the repository, so new reference docs are picked up
automatically.

## License

MIT
