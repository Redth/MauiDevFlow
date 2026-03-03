using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MauiDevFlow.CLI;

/// <summary>
/// CDP-oriented CLI for automating MAUI Blazor WebViews.
/// Commands mirror CDP domain/method patterns for familiarity.
/// </summary>
class Program
{
    private static Parser? _parser;
    [ThreadStatic] private static bool _errorOccurred;

    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("MauiDevFlow CLI - automate MAUI apps via Agent API and Blazor WebViews via CDP");
        
        // Global agent connection options (available on all commands and subcommands)
        var agentPortOption = new Option<int>(
            ["--agent-port", "-ap"],
            () => ResolveAgentPort(),
            "Agent HTTP port (auto-discovered via broker, .mauidevflow, or default 9223)");
        var agentHostOption = new Option<string>(
            ["--agent-host", "-ah"],
            () => "localhost",
            "Agent HTTP host");
        var platformOption = new Option<string>(
            ["--platform", "-p"],
            () => "maccatalyst",
            "Target platform (maccatalyst, android, ios, windows)");

        rootCommand.AddGlobalOption(agentPortOption);
        rootCommand.AddGlobalOption(agentHostOption);
        rootCommand.AddGlobalOption(platformOption);

        // ===== CDP commands (Blazor WebView) =====
        
        var cdpCommand = new Command("cdp", "Blazor WebView automation via Chrome DevTools Protocol");

        var webviewOption = new Option<string?>(
            ["--webview", "-w"],
            () => null,
            "Target WebView by index, AutomationId, or element ID (default: first WebView)");
        cdpCommand.AddGlobalOption(webviewOption);
        
        // Browser domain commands
        var browserCommand = new Command("Browser", "Browser domain commands");
        
        var getVersionCmd = new Command("getVersion", "Get browser version info");
        getVersionCmd.SetHandler(async (host, port, wv) => await BrowserGetVersionAsync(host, port, wv), agentHostOption, agentPortOption, webviewOption);
        browserCommand.Add(getVersionCmd);
        
        cdpCommand.Add(browserCommand);
        
        // Runtime domain commands  
        var runtimeCommand = new Command("Runtime", "Runtime domain commands");
        
        var evaluateArg = new Argument<string>("expression", "JavaScript expression");
        var evaluateCmd = new Command("evaluate", "Evaluate JavaScript expression") { evaluateArg };
        evaluateCmd.SetHandler(async (host, port, expr, wv) => await RuntimeEvaluateAsync(host, port, expr, wv), agentHostOption, agentPortOption, evaluateArg, webviewOption);
        runtimeCommand.Add(evaluateCmd);
        
        cdpCommand.Add(runtimeCommand);
        
        // DOM domain commands
        var domCommand = new Command("DOM", "DOM domain commands");
        
        var getDocumentCmd = new Command("getDocument", "Get document root node");
        getDocumentCmd.SetHandler(async (host, port, wv) => await DomGetDocumentAsync(host, port, wv), agentHostOption, agentPortOption, webviewOption);
        domCommand.Add(getDocumentCmd);
        
        var querySelectorArg = new Argument<string>("selector", "CSS selector");
        var querySelectorCmd = new Command("querySelector", "Find element by CSS selector") { querySelectorArg };
        querySelectorCmd.SetHandler(async (host, port, selector, wv) => await DomQuerySelectorAsync(host, port, selector, wv), agentHostOption, agentPortOption, querySelectorArg, webviewOption);
        domCommand.Add(querySelectorCmd);
        
        var querySelectorAllArg = new Argument<string>("selector", "CSS selector");
        var querySelectorAllCmd = new Command("querySelectorAll", "Find all elements by CSS selector") { querySelectorAllArg };
        querySelectorAllCmd.SetHandler(async (host, port, selector, wv) => await DomQuerySelectorAllAsync(host, port, selector, wv), agentHostOption, agentPortOption, querySelectorAllArg, webviewOption);
        domCommand.Add(querySelectorAllCmd);
        
        var getOuterHtmlArg = new Argument<string>("selector", "CSS selector");
        var getOuterHtmlCmd = new Command("getOuterHTML", "Get element HTML") { getOuterHtmlArg };
        getOuterHtmlCmd.SetHandler(async (host, port, selector, wv) => await DomGetOuterHtmlAsync(host, port, selector, wv), agentHostOption, agentPortOption, getOuterHtmlArg, webviewOption);
        domCommand.Add(getOuterHtmlCmd);
        
        cdpCommand.Add(domCommand);
        
        // Page domain commands
        var pageCommand = new Command("Page", "Page domain commands");
        
        var navigateArg = new Argument<string>("url", "URL to navigate to");
        var navigateCmd = new Command("navigate", "Navigate to URL") { navigateArg };
        navigateCmd.SetHandler(async (host, port, url, wv) => await PageNavigateAsync(host, port, url, wv), agentHostOption, agentPortOption, navigateArg, webviewOption);
        pageCommand.Add(navigateCmd);
        
        var reloadCmd = new Command("reload", "Reload page");
        reloadCmd.SetHandler(async (host, port, wv) => await PageReloadAsync(host, port, wv), agentHostOption, agentPortOption, webviewOption);
        pageCommand.Add(reloadCmd);
        
        var captureScreenshotCmd = new Command("captureScreenshot", "Capture page screenshot (base64)");
        captureScreenshotCmd.SetHandler(async (host, port, wv) => await PageCaptureScreenshotAsync(host, port, wv), agentHostOption, agentPortOption, webviewOption);
        pageCommand.Add(captureScreenshotCmd);
        
        cdpCommand.Add(pageCommand);
        
        // Input domain commands
        var inputCommand = new Command("Input", "Input domain commands");
        
        var clickSelectorArg = new Argument<string>("selector", "CSS selector of element to click");
        var dispatchClickCmd = new Command("dispatchClickEvent", "Click element by selector") { clickSelectorArg };
        dispatchClickCmd.SetHandler(async (host, port, selector, wv) => await InputDispatchClickAsync(host, port, selector, wv), agentHostOption, agentPortOption, clickSelectorArg, webviewOption);
        inputCommand.Add(dispatchClickCmd);
        
        var insertTextArg = new Argument<string>("text", "Text to insert");
        var insertTextCmd = new Command("insertText", "Insert text at cursor") { insertTextArg };
        insertTextCmd.SetHandler(async (host, port, text, wv) => await InputInsertTextAsync(host, port, text, wv), agentHostOption, agentPortOption, insertTextArg, webviewOption);
        inputCommand.Add(insertTextCmd);
        
        var fillSelectorArg = new Argument<string>("selector", "CSS selector");
        var fillTextArg = new Argument<string>("text", "Text to fill");
        var fillCmd = new Command("fill", "Fill form field with text") { fillSelectorArg, fillTextArg };
        fillCmd.SetHandler(async (host, port, selector, text, wv) => await InputFillAsync(host, port, selector, text, wv), agentHostOption, agentPortOption, fillSelectorArg, fillTextArg, webviewOption);
        inputCommand.Add(fillCmd);
        
        cdpCommand.Add(inputCommand);
        
        // Convenience commands
        var statusCmd = new Command("status", "Check CDP connection status");
        statusCmd.SetHandler(async (host, port, wv) => await CdpStatusAsync(host, port, wv), agentHostOption, agentPortOption, webviewOption);
        cdpCommand.Add(statusCmd);
        
        var snapshotCmd = new Command("snapshot", "Get simplified DOM snapshot with element refs");
        snapshotCmd.SetHandler(async (host, port, wv) => await SnapshotAsync(host, port, wv), agentHostOption, agentPortOption, webviewOption);
        cdpCommand.Add(snapshotCmd);

        var webviewsCmd = new Command("webviews", "List available CDP WebViews");
        var webviewsJsonOption = new Option<bool>("--json", () => false, "Output as JSON");
        webviewsCmd.Add(webviewsJsonOption);
        webviewsCmd.SetHandler(async (host, port, json) => await CdpWebViewsAsync(host, port, json), agentHostOption, agentPortOption, webviewsJsonOption);
        cdpCommand.Add(webviewsCmd);
        
        rootCommand.Add(cdpCommand);
        
        // ===== MAUI Native commands =====

        var mauiCommand = new Command("MAUI", "Native MAUI app automation commands");

        // Shared window option for commands that target a specific window
        var windowOption = new Option<int?>("--window", "Window index (0-based, default: first window)");

        // MAUI status
        var mauiStatusCmd = new Command("status", "Check agent connection") { windowOption };
        mauiStatusCmd.SetHandler(async (host, port, window) => await MauiStatusAsync(host, port, window), agentHostOption, agentPortOption, windowOption);
        mauiCommand.Add(mauiStatusCmd);

        // MAUI tree
        var treeDepthOption = new Option<int>("--depth", () => 0, "Max tree depth (0=unlimited)");
        var mauiTreeCmd = new Command("tree", "Dump visual tree") { treeDepthOption, windowOption };
        mauiTreeCmd.SetHandler(async (host, port, depth, window) => await MauiTreeAsync(host, port, depth, window), agentHostOption, agentPortOption, treeDepthOption, windowOption);
        mauiCommand.Add(mauiTreeCmd);

        // MAUI query
        var queryTypeOption = new Option<string?>("--type", "Filter by element type");
        var queryAutoIdOption = new Option<string?>("--automationId", "Filter by AutomationId");
        var queryTextOption = new Option<string?>("--text", "Filter by text content");
        var querySelectorOption = new Option<string?>("--selector", "CSS selector (e.g. 'Button:visible', 'StackLayout > Label[Text^=\"Hello\"]')");
        var mauiQueryCmd = new Command("query", "Find elements") { queryTypeOption, queryAutoIdOption, queryTextOption, querySelectorOption };
        mauiQueryCmd.SetHandler(async (host, port, type, autoId, text, selector) =>
            await MauiQueryAsync(host, port, type, autoId, text, selector),
            agentHostOption, agentPortOption, queryTypeOption, queryAutoIdOption, queryTextOption, querySelectorOption);
        mauiCommand.Add(mauiQueryCmd);

        // MAUI tap
        var tapIdArg = new Argument<string>("elementId", "Element ID to tap");
        var mauiTapCmd = new Command("tap", "Tap element") { tapIdArg };
        mauiTapCmd.SetHandler(async (host, port, id) => await MauiTapAsync(host, port, id), agentHostOption, agentPortOption, tapIdArg);
        mauiCommand.Add(mauiTapCmd);

        // MAUI fill
        var fillIdArg = new Argument<string>("elementId", "Element ID");
        var fillTextArg2 = new Argument<string>("text", "Text to fill");
        var mauiFillCmd = new Command("fill", "Fill text into element") { fillIdArg, fillTextArg2 };
        mauiFillCmd.SetHandler(async (host, port, id, text) => await MauiFillAsync(host, port, id, text), agentHostOption, agentPortOption, fillIdArg, fillTextArg2);
        mauiCommand.Add(mauiFillCmd);

        // MAUI clear
        var clearIdArg = new Argument<string>("elementId", "Element ID to clear");
        var mauiClearCmd = new Command("clear", "Clear text from element") { clearIdArg };
        mauiClearCmd.SetHandler(async (host, port, id) => await MauiClearAsync(host, port, id), agentHostOption, agentPortOption, clearIdArg);
        mauiCommand.Add(mauiClearCmd);

        // MAUI screenshot
        var screenshotOutputOption = new Option<string?>("--output", "Output file path");
        var screenshotIdOption = new Option<string?>("--id", "Element ID to capture");
        var screenshotSelectorOption = new Option<string?>("--selector", "CSS selector to capture (first match)");
        var mauiScreenshotCmd = new Command("screenshot", "Take screenshot") { screenshotOutputOption, windowOption, screenshotIdOption, screenshotSelectorOption };
        mauiScreenshotCmd.SetHandler(async (host, port, output, window, id, selector) => await MauiScreenshotAsync(host, port, output, window, id, selector), agentHostOption, agentPortOption, screenshotOutputOption, windowOption, screenshotIdOption, screenshotSelectorOption);
        mauiCommand.Add(mauiScreenshotCmd);

        // MAUI recording subcommands
        var recordingCommand = new Command("recording", "Screen recording (start/stop/status)");

        var recordingOutputOption = new Option<string?>("--output", "Output file path");
        var recordingTimeoutOption = new Option<int>("--timeout", () => 30, "Max recording duration in seconds");
        var recordingStartCmd = new Command("start", "Start screen recording") { recordingOutputOption, recordingTimeoutOption };
        recordingStartCmd.SetHandler(async (host, port, platform, output, timeout) =>
            await RecordingStartAsync(host, port, platform, output, timeout),
            agentHostOption, agentPortOption, platformOption, recordingOutputOption, recordingTimeoutOption);
        recordingCommand.Add(recordingStartCmd);

        var recordingStopCmd = new Command("stop", "Stop active recording");
        recordingStopCmd.SetHandler(async (host, port, platform) =>
            await RecordingStopAsync(host, port, platform),
            agentHostOption, agentPortOption, platformOption);
        recordingCommand.Add(recordingStopCmd);

        var recordingStatusCmd = new Command("status", "Check if a recording is in progress");
        recordingStatusCmd.SetHandler(() => RecordingStatusAsync());
        recordingCommand.Add(recordingStatusCmd);

        mauiCommand.Add(recordingCommand);

        // MAUI property
        var propIdArg = new Argument<string>("elementId", "Element ID");
        var propNameArg = new Argument<string>("propertyName", "Property name");
        var mauiPropertyCmd = new Command("property", "Get element property") { propIdArg, propNameArg };
        mauiPropertyCmd.SetHandler(async (host, port, id, name) => await MauiPropertyAsync(host, port, id, name), agentHostOption, agentPortOption, propIdArg, propNameArg);
        mauiCommand.Add(mauiPropertyCmd);

        // MAUI set-property
        var setPropIdArg = new Argument<string>("elementId", "Element ID");
        var setPropNameArg = new Argument<string>("propertyName", "Property name");
        var setPropValueArg = new Argument<string>("value", "Value to set");
        var mauiSetPropertyCmd = new Command("set-property", "Set element property (live editing)") { setPropIdArg, setPropNameArg, setPropValueArg };
        mauiSetPropertyCmd.SetHandler(async (host, port, id, name, value) => await MauiSetPropertyAsync(host, port, id, name, value), agentHostOption, agentPortOption, setPropIdArg, setPropNameArg, setPropValueArg);
        mauiCommand.Add(mauiSetPropertyCmd);

        // MAUI element
        var elementIdArg = new Argument<string>("elementId", "Element ID");
        var mauiElementCmd = new Command("element", "Get element details") { elementIdArg };
        mauiElementCmd.SetHandler(async (host, port, id) => await MauiElementAsync(host, port, id), agentHostOption, agentPortOption, elementIdArg);
        mauiCommand.Add(mauiElementCmd);

        // MAUI navigate (Shell)
        var navRouteArg = new Argument<string>("route", "Shell route (e.g. //blazor)");
        var mauiNavigateCmd = new Command("navigate", "Navigate to Shell route") { navRouteArg };
        mauiNavigateCmd.SetHandler(async (host, port, route) => await MauiNavigateAsync(host, port, route), agentHostOption, agentPortOption, navRouteArg);
        mauiCommand.Add(mauiNavigateCmd);

        // MAUI scroll
        var scrollElementIdOption = new Option<string?>("--element", "Element ID to scroll into view");
        var scrollDeltaXOption = new Option<double>("--dx", () => 0, "Horizontal scroll delta");
        var scrollDeltaYOption = new Option<double>("--dy", () => 0, "Vertical scroll delta");
        var scrollAnimatedOption = new Option<bool>("--animated", () => true, "Animate the scroll");
        var mauiScrollCmd = new Command("scroll", "Scroll content by delta or scroll element into view") { scrollElementIdOption, scrollDeltaXOption, scrollDeltaYOption, scrollAnimatedOption, windowOption };
        mauiScrollCmd.SetHandler(async (host, port, elementId, dx, dy, animated, window) =>
            await MauiScrollAsync(host, port, elementId, dx, dy, animated, window),
            agentHostOption, agentPortOption, scrollElementIdOption, scrollDeltaXOption, scrollDeltaYOption, scrollAnimatedOption, windowOption);
        mauiCommand.Add(mauiScrollCmd);

        // MAUI focus
        var focusIdArg = new Argument<string>("elementId", "Element ID to focus");
        var mauiFocusCmd = new Command("focus", "Set focus to element") { focusIdArg };
        mauiFocusCmd.SetHandler(async (host, port, id) => await MauiFocusAsync(host, port, id), agentHostOption, agentPortOption, focusIdArg);
        mauiCommand.Add(mauiFocusCmd);

        // MAUI resize
        var resizeWidthArg = new Argument<int>("width", "Window width");
        var resizeHeightArg = new Argument<int>("height", "Window height");
        var mauiResizeCmd = new Command("resize", "Resize app window") { resizeWidthArg, resizeHeightArg, windowOption };
        mauiResizeCmd.SetHandler(async (host, port, w, h, window) => await MauiResizeAsync(host, port, w, h, window), agentHostOption, agentPortOption, resizeWidthArg, resizeHeightArg, windowOption);
        mauiCommand.Add(mauiResizeCmd);

        // MAUI alert subcommands — supports iOS simulator (apple CLI) and Mac Catalyst (macOS AX API)
        var alertCommand = new Command("alert", "Detect and dismiss system/app dialogs");

        // detect
        var detectUdid = new Option<string?>("--udid", "Simulator UDID (auto-detects booted simulator if omitted)");
        var detectPid = new Option<int?>("--pid", "Mac Catalyst app PID (auto-detects if omitted)");
        var detectPlatform = new Option<string>("--platform", () => "auto", "Platform: maccatalyst, ios, android, windows, or auto");
        var detectHost = new Option<string>("--agent-host", () => "localhost", "Agent HTTP host");
        var detectPort = new Option<int>("--agent-port", () => 9223, "Agent HTTP port");
        var alertDetectCmd = new Command("detect", "Check if an alert/dialog is visible") { detectUdid, detectPid, detectPlatform, detectHost, detectPort };
        alertDetectCmd.SetHandler(async (udid, pid, platform, host, port) =>
            await AlertDetectAsync(udid, pid, platform, host, port), detectUdid, detectPid, detectPlatform, detectHost, detectPort);
        alertCommand.Add(alertDetectCmd);

        // dismiss
        var dismissUdid = new Option<string?>("--udid", "Simulator UDID (auto-detects booted simulator if omitted)");
        var dismissPid = new Option<int?>("--pid", "Mac Catalyst app PID (auto-detects if omitted)");
        var dismissPlatform = new Option<string>("--platform", () => "auto", "Platform: maccatalyst, ios, android, windows, or auto");
        var dismissHost = new Option<string>("--agent-host", () => "localhost", "Agent HTTP host");
        var dismissPort = new Option<int>("--agent-port", () => 9223, "Agent HTTP port");
        var dismissButtonArg = new Argument<string?>("button", () => null, "Button label to tap (default: first accept-style button)");
        var alertDismissCmd = new Command("dismiss", "Dismiss the current alert/dialog") { dismissButtonArg, dismissUdid, dismissPid, dismissPlatform, dismissHost, dismissPort };
        alertDismissCmd.SetHandler(async (udid, pid, platform, host, port, button) =>
            await AlertDismissAsync(udid, pid, platform, host, port, button), dismissUdid, dismissPid, dismissPlatform, dismissHost, dismissPort, dismissButtonArg);
        alertCommand.Add(alertDismissCmd);

        // tree
        var treeUdid = new Option<string?>("--udid", "Simulator UDID (auto-detects booted simulator if omitted)");
        var treePid = new Option<int?>("--pid", "Mac Catalyst app PID (auto-detects if omitted)");
        var treePlatform = new Option<string>("--platform", () => "auto", "Platform: maccatalyst, ios, android, windows, or auto");
        var treeHost = new Option<string>("--agent-host", () => "localhost", "Agent HTTP host");
        var treePort = new Option<int>("--agent-port", () => 9223, "Agent HTTP port");
        var alertTreeCmd = new Command("tree", "Show raw accessibility tree") { treeUdid, treePid, treePlatform, treeHost, treePort };
        alertTreeCmd.SetHandler(async (udid, pid, platform, host, port) =>
            await AlertTreeAsync(udid, pid, platform, host, port), treeUdid, treePid, treePlatform, treeHost, treePort);
        alertCommand.Add(alertTreeCmd);

        mauiCommand.Add(alertCommand);

        // MAUI permission subcommands (iOS simulator only — uses xcrun simctl privacy)
        var permissionCommand = new Command("permission", "Manage iOS simulator permissions");

        var permGrantUdid = new Option<string?>("--udid", "Simulator UDID (auto-detects booted simulator if omitted)");
        var permGrantBundle = new Option<string?>("--bundle-id", "App bundle identifier");
        var permGrantServiceArg = new Argument<string>("service", "Permission service (camera, location, photos, contacts, microphone, calendar, all, etc.)");
        var permGrantCmd = new Command("grant", "Grant a permission (no dialog will appear)") { permGrantServiceArg, permGrantUdid, permGrantBundle };
        permGrantCmd.SetHandler(async (udid, bundleId, service) => await PermissionAsync("grant", udid, bundleId, service), permGrantUdid, permGrantBundle, permGrantServiceArg);
        permissionCommand.Add(permGrantCmd);

        var permRevokeUdid = new Option<string?>("--udid", "Simulator UDID (auto-detects booted simulator if omitted)");
        var permRevokeBundle = new Option<string?>("--bundle-id", "App bundle identifier");
        var permRevokeServiceArg = new Argument<string>("service", "Permission service");
        var permRevokeCmd = new Command("revoke", "Revoke a permission") { permRevokeServiceArg, permRevokeUdid, permRevokeBundle };
        permRevokeCmd.SetHandler(async (udid, bundleId, service) => await PermissionAsync("revoke", udid, bundleId, service), permRevokeUdid, permRevokeBundle, permRevokeServiceArg);
        permissionCommand.Add(permRevokeCmd);

        var permResetUdid = new Option<string?>("--udid", "Simulator UDID (auto-detects booted simulator if omitted)");
        var permResetBundle = new Option<string?>("--bundle-id", "App bundle identifier");
        var permResetServiceArg = new Argument<string>("service", () => "all", "Permission service (default: all)");
        var permResetCmd = new Command("reset", "Reset permission (app will be prompted again)") { permResetServiceArg, permResetUdid, permResetBundle };
        permResetCmd.SetHandler(async (udid, bundleId, service) => await PermissionAsync("reset", udid, bundleId, service), permResetUdid, permResetBundle, permResetServiceArg);
        permissionCommand.Add(permResetCmd);

        mauiCommand.Add(permissionCommand);

        // logs command
        var logsLimitOption = new Option<int>("--limit", () => 100, "Number of log entries to return");
        var logsSkipOption = new Option<int>("--skip", () => 0, "Number of newest entries to skip");
        var logsSourceOption = new Option<string?>("--source", () => null, "Filter by log source: native, webview, or all (default: all)");
        var mauiLogsCmd = new Command("logs", "Fetch application logs") { logsLimitOption, logsSkipOption, logsSourceOption };
        mauiLogsCmd.SetHandler(async (host, port, limit, skip, source) => await MauiLogsAsync(host, port, limit, skip, source),
            agentHostOption, agentPortOption, logsLimitOption, logsSkipOption, logsSourceOption);
        mauiCommand.Add(mauiLogsCmd);

        rootCommand.Add(mauiCommand);

        // ===== update-skill command =====
        var forceOption = new Option<bool>(
            ["--force", "-y"],
            "Skip confirmation prompt");
        var outputDirOption = new Option<string?>(
            ["--output", "-o"],
            "Output directory (defaults to current directory)");
        var branchOption = new Option<string>(
            ["--branch", "-b"],
            () => "main",
            "GitHub branch to download from");
        var updateSkillCmd = new Command("update-skill", "Download the latest maui-ai-debugging skill from GitHub")
        {
            forceOption, outputDirOption, branchOption
        };
        updateSkillCmd.SetHandler(async (force, output, branch) => await UpdateSkillAsync(force, output, branch), forceOption, outputDirOption, branchOption);
        rootCommand.Add(updateSkillCmd);

        // ===== skill-version command =====
        var skillVersionOutputOption = new Option<string?>(
            ["--output", "-o"],
            "Skill directory (defaults to current directory)");
        var skillVersionBranchOption = new Option<string>(
            ["--branch", "-b"],
            () => "main",
            "GitHub branch to check against");
        var skillVersionCmd = new Command("skill-version", "Check the installed skill version and compare with remote")
        {
            skillVersionOutputOption, skillVersionBranchOption
        };
        skillVersionCmd.SetHandler(async (output, branch) => await SkillVersionAsync(output, branch), skillVersionOutputOption, skillVersionBranchOption);
        rootCommand.Add(skillVersionCmd);

        // ===== broker commands =====
        var brokerCommand = new Command("broker", "Manage the MauiDevFlow broker daemon");

        var brokerForegroundOption = new Option<bool>("--foreground", "Run in foreground (don't detach)");
        var brokerStartCmd = new Command("start", "Start the broker daemon") { brokerForegroundOption };
        brokerStartCmd.SetHandler(async (foreground) => await BrokerStartAsync(foreground), brokerForegroundOption);
        brokerCommand.Add(brokerStartCmd);

        var brokerStopCmd = new Command("stop", "Stop the broker daemon");
        brokerStopCmd.SetHandler(async () => await BrokerStopAsync());
        brokerCommand.Add(brokerStopCmd);

        var brokerStatusCmd = new Command("status", "Show broker daemon status");
        brokerStatusCmd.SetHandler(async () => await BrokerStatusAsync());
        brokerCommand.Add(brokerStatusCmd);

        var brokerLogCmd = new Command("log", "Show broker log");
        brokerLogCmd.SetHandler(() => BrokerLogAsync());
        brokerCommand.Add(brokerLogCmd);

        rootCommand.Add(brokerCommand);

        // ===== list command (agent discovery) =====
        var listCmd = new Command("list", "List all connected agents");
        listCmd.SetHandler(async () => await ListAgentsCommandAsync());
        rootCommand.Add(listCmd);

        // ===== wait command (wait for agent to connect) =====
        var waitTimeoutOption = new Option<int>(
            ["--timeout", "-t"],
            () => 120,
            "Maximum seconds to wait for an agent to connect");
        var waitProjectOption = new Option<string?>(
            ["--project"],
            () => null,
            "Filter by project path (csproj). Resolves to full path for matching.");
        var waitPlatformOption = new Option<string?>(
            ["--wait-platform"],
            () => null,
            "Filter by platform (e.g., macOS, iOS, Android)");
        var waitJsonOption = new Option<bool>(
            "--json",
            () => false,
            "Output agent info as JSON instead of human-readable text");
        var waitCmd = new Command("wait", "Wait for an agent to connect to the broker")
        {
            waitTimeoutOption, waitProjectOption, waitPlatformOption, waitJsonOption
        };
        waitCmd.SetHandler(async (timeout, project, waitPlatform, json) =>
            await WaitForAgentCommandAsync(timeout, project, waitPlatform, json),
            waitTimeoutOption, waitProjectOption, waitPlatformOption, waitJsonOption);
        rootCommand.Add(waitCmd);

        // ===== batch command (interactive stdin/stdout) =====
        var batchDelayOption = new Option<int>("--delay", () => 250, "Delay in ms between commands");
        var batchContinueOption = new Option<bool>("--continue-on-error", () => false, "Continue executing after a command fails");
        var batchHumanOption = new Option<bool>("--human", () => false, "Human-readable output instead of JSONL");
        var batchCommand = new Command("batch", "Execute commands from stdin with JSONL responses on stdout")
        {
            batchDelayOption, batchContinueOption, batchHumanOption
        };
        batchCommand.SetHandler(async (host, port, delay, continueOnError, human) =>
            await BatchAsync(host, port, delay, continueOnError, human),
            agentHostOption, agentPortOption, batchDelayOption, batchContinueOption, batchHumanOption);
        rootCommand.Add(batchCommand);

        _parser = new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .Build();

        _errorOccurred = false;
        var result = await _parser.InvokeAsync(args);
        return _errorOccurred ? 1 : result;
    }
    
    // ===== CDP Helper: Send command via HTTP POST /api/cdp =====

    private static async Task<JsonElement?> SendCdpCommandAsync(string host, int port, string method, object? parameters = null, string? webview = null)
    {
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        var command = new Dictionary<string, object>
        {
            ["id"] = 1,
            ["method"] = method
        };
        if (parameters != null)
            command["params"] = parameters;

        var json = JsonSerializer.Serialize(command);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var url = string.IsNullOrEmpty(webview)
            ? $"http://{host}:{port}/api/cdp"
            : $"http://{host}:{port}/api/cdp?webview={Uri.EscapeDataString(webview)}";
        var response = await http.PostAsync(url, content);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"CDP request failed ({response.StatusCode}): {body}");

        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    private static async Task<string> CdpEvaluateAsync(string host, int port, string expression, string? webview = null)
    {
        var result = await SendCdpCommandAsync(host, port, "Runtime.evaluate", new
        {
            expression,
            returnByValue = true
        }, webview);

        if (result == null) return "null";
        var root = result.Value;

        if (root.TryGetProperty("result", out var evalResult))
        {
            if (evalResult.TryGetProperty("result", out var resultProp))
            {
                if (resultProp.TryGetProperty("value", out var value))
                {
                    if (value.ValueKind == JsonValueKind.String)
                        return value.GetString() ?? "null";
                    if (value.ValueKind == JsonValueKind.Object || value.ValueKind == JsonValueKind.Array)
                        return JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
                    return value.ToString();
                }
            }
            if (evalResult.TryGetProperty("exceptionDetails", out var exception))
            {
                var text = exception.TryGetProperty("text", out var t) ? t.GetString() : "Unknown error";
                return $"Error: {text}";
            }
        }

        // Response might be the raw chobitsu response
        if (root.TryGetProperty("result", out var rawResult) && rawResult.TryGetProperty("value", out var rawValue))
            return rawValue.GetString() ?? rawValue.ToString();

        return root.ToString();
    }

    // ===== Browser Domain =====
    
    private static async Task BrowserGetVersionAsync(string host, int port, string? webview = null)
    {
        try
        {
            var result = await SendCdpCommandAsync(host, port, "Browser.getVersion", webview: webview);
            Console.WriteLine(result.HasValue ? FormatJson(result.Value) : "null");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    // ===== Runtime Domain =====
    
    private static async Task RuntimeEvaluateAsync(string host, int port, string expression, string? webview = null)
    {
        try
        {
            var result = await CdpEvaluateAsync(host, port, expression, webview);
            Console.WriteLine(result);
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    // ===== DOM Domain =====
    
    private static async Task DomGetDocumentAsync(string host, int port, string? webview = null)
    {
        try
        {
            var result = await SendCdpCommandAsync(host, port, "DOM.getDocument", webview: webview);
            Console.WriteLine(result.HasValue ? FormatJson(result.Value) : "null");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    private static async Task DomQuerySelectorAsync(string host, int port, string selector, string? webview = null)
    {
        try
        {
            var result = await CdpEvaluateAsync(host, port, $@"
                JSON.stringify((function() {{
                    const el = document.querySelector({JsonSerializer.Serialize(selector)}, webview);
                    if (!el) return null;
                    return {{
                        tagName: el.tagName.toLowerCase(),
                        id: el.id || null,
                        className: el.className || null,
                        textContent: el.textContent?.trim().substring(0, 100) || null
                    }};
                }})())
            ");
            Console.WriteLine(result);
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    private static async Task DomQuerySelectorAllAsync(string host, int port, string selector, string? webview = null)
    {
        try
        {
            var result = await CdpEvaluateAsync(host, port, $@"
                JSON.stringify((function() {{
                    const els = document.querySelectorAll({JsonSerializer.Serialize(selector)}, webview);
                    return Array.from(els).map((el, i) => ({{
                        index: i,
                        tagName: el.tagName.toLowerCase(),
                        id: el.id || null,
                        className: el.className || null,
                        textContent: el.textContent?.trim().substring(0, 50) || null
                    }}));
                }})(), null, 2)
            ");
            Console.WriteLine(result);
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    private static async Task DomGetOuterHtmlAsync(string host, int port, string selector, string? webview = null)
    {
        try
        {
            var result = await CdpEvaluateAsync(host, port, $@"document.querySelector({JsonSerializer.Serialize(selector)})?.outerHTML || null", webview);
            Console.WriteLine(result);
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    // ===== Page Domain =====
    
    private static async Task PageNavigateAsync(string host, int port, string url, string? webview = null)
    {
        try
        {
            await SendCdpCommandAsync(host, port, "Page.navigate", new { url }, webview);
            Console.WriteLine($"Navigated to: {url}");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    private static async Task PageReloadAsync(string host, int port, string? webview = null)
    {
        try
        {
            await SendCdpCommandAsync(host, port, "Page.reload", webview: webview);
            Console.WriteLine("Page reloaded");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    private static async Task PageCaptureScreenshotAsync(string host, int port, string? webview = null)
    {
        try
        {
            var result = await SendCdpCommandAsync(host, port, "Page.captureScreenshot", webview: webview);
            if (result.HasValue &&
                result.Value.TryGetProperty("result", out var resultProp) && 
                resultProp.TryGetProperty("data", out var dataProp))
            {
                Console.WriteLine(dataProp.GetString());
            }
            else
            {
                Console.WriteLine(result.HasValue ? FormatJson(result.Value) : "null");
            }
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    // ===== Input Domain =====
    
    private static async Task InputDispatchClickAsync(string host, int port, string selector, string? webview = null)
    {
        try
        {
            var result = await CdpEvaluateAsync(host, port, $@"
                (function() {{
                    const el = document.querySelector({JsonSerializer.Serialize(selector)}, webview);
                    if (!el) return 'Error: Element not found';
                    el.click();
                    return 'Clicked: ' + el.tagName.toLowerCase() + (el.id ? '#' + el.id : '');
                }})()
            ");
            Console.WriteLine(result);
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    private static async Task InputInsertTextAsync(string host, int port, string text, string? webview = null)
    {
        try
        {
            var result = await SendCdpCommandAsync(host, port, "Input.insertText", new { text }, webview);
            Console.WriteLine($"Inserted: {text.Length} characters");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    private static async Task InputFillAsync(string host, int port, string selector, string text, string? webview = null)
    {
        try
        {
            var result = await CdpEvaluateAsync(host, port, $@"
                (function() {{
                    const el = document.querySelector({JsonSerializer.Serialize(selector)}, webview);
                    if (!el) return 'Error: Element not found';
                    
                    const text = {JsonSerializer.Serialize(text)};
                    if (el.isContentEditable) {{
                        el.textContent = text;
                    }} else {{
                        el.value = text;
                        el.focus();
                    }}
                    el.dispatchEvent(new Event('input', {{ bubbles: true }}));
                    el.dispatchEvent(new Event('change', {{ bubbles: true }}));
                    return 'Filled: ' + el.tagName.toLowerCase() + (el.id ? '#' + el.id : '') + ' with ' + text.length + ' chars';
                }})()
            ");
            Console.WriteLine(result);
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    // ===== Convenience Commands =====
    
    private static async Task CdpStatusAsync(string host, int port, string? webview = null)
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            var response = await http.GetAsync($"http://{host}:{port}/api/status");
            var body = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var cdpReady = root.TryGetProperty("cdpReady", out var cdpProp) && cdpProp.GetBoolean();
            var cdpCount = root.TryGetProperty("cdpWebViewCount", out var countProp) ? countProp.GetInt32() : 0;
            Console.WriteLine(cdpReady
                ? $"Connected: CDP ready ({cdpCount} WebView{(cdpCount != 1 ? "s" : "")})"
                : "Agent connected but CDP not ready");
        }
        catch (Exception ex)
        {
            WriteError($"Not connected: {ex.Message}");
        }
    }

    private static async Task CdpWebViewsAsync(string host, int port, bool json)
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            var response = await http.GetAsync($"http://{host}:{port}/api/cdp/webviews");
            var body = await response.Content.ReadAsStringAsync();

            if (json)
            {
                Console.WriteLine(body);
                return;
            }

            var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("webviews", out var webviews))
            {
                if (webviews.GetArrayLength() == 0)
                {
                    Console.WriteLine("No CDP WebViews registered");
                    return;
                }

                Console.WriteLine($"{"Index",-6} {"AutomationId",-20} {"ElementId",-12} {"Ready",-6} {"URL"}");
                Console.WriteLine(new string('-', 70));
                foreach (var wv in webviews.EnumerateArray())
                {
                    var index = wv.TryGetProperty("index", out var idx) ? idx.GetInt32().ToString() : "-";
                    var autoId = wv.TryGetProperty("automationId", out var aid) ? aid.GetString() ?? "-" : "-";
                    var elemId = wv.TryGetProperty("elementId", out var eid) ? eid.GetString() ?? "-" : "-";
                    var ready = wv.TryGetProperty("isReady", out var rdy) && rdy.GetBoolean() ? "Yes" : "No";
                    var url = wv.TryGetProperty("url", out var urlProp) ? urlProp.GetString() ?? "-" : "-";
                    Console.WriteLine($"{index,-6} {autoId,-20} {elemId,-12} {ready,-6} {url}");
                }
            }
        }
        catch (Exception ex)
        {
            WriteError($"Failed to list WebViews: {ex.Message}");
        }
    }
    
    private static async Task SnapshotAsync(string host, int port, string? webview = null)
    {
        try
        {
            var result = await CdpEvaluateAsync(host, port, @"
                (function() {
                    function walk(node, depth) {
                        if (depth > 8) return '';
                        let result = '';
                        const indent = '  '.repeat(depth, webview);
                        
                        if (node.nodeType === 1) {
                            const tag = node.tagName.toLowerCase();
                            const text = node.childNodes.length === 1 && node.childNodes[0].nodeType === 3 
                                ? node.textContent?.trim().substring(0, 80) : null;
                            const isClickable = node.onclick || tag === 'button' || tag === 'a' || 
                                               node.getAttribute('role') === 'button' ||
                                               (tag === 'input' && node.type === 'submit');
                            const isInput = tag === 'input' || tag === 'textarea' || tag === 'select';
                            
                            result += indent + '<' + tag;
                            if (node.id) result += ' id=""' + node.id + '""';
                            if (node.className && typeof node.className === 'string') result += ' class=""' + node.className.split(' ').slice(0,2).join(' ') + '""';
                            if (isClickable) result += ' [clickable]';
                            if (isInput) result += ' [input]';
                            if (tag === 'a' && node.href) result += ' href=""' + node.getAttribute('href') + '""';
                            if (tag === 'input') result += ' type=""' + (node.type || 'text') + '""';
                            result += '>';
                            if (text) result += ' ' + text;
                            result += '\n';
                            
                            for (const child of node.children) {
                                result += walk(child, depth + 1);
                            }
                        }
                        return result;
                    }
                    
                    return 'Title: ' + document.title + '\nURL: ' + location.href + '\n\n' + walk(document.body, 0);
                })()
            ");
            
            Console.WriteLine(result);
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }
    
    private static void WriteError(string message)
    {
        _errorOccurred = true;
        Console.Error.WriteLine($"Error: {message}");
    }
    
    private class CommandErrorException : Exception
    {
        public CommandErrorException(string message) : base(message) { }
    }
    
    private static string FormatJson(JsonElement element)
    {
        return JsonSerializer.Serialize(element, new JsonSerializerOptions { WriteIndented = true });
    }

    // ===== Update Skill Command =====

    private const string SkillRepo = "Redth/MauiDevFlow";
    private const string SkillBasePath = ".claude/skills/maui-ai-debugging";

    private static async Task UpdateSkillAsync(bool force, string? outputDir, string branch)
    {
        var root = outputDir ?? Directory.GetCurrentDirectory();
        var destBase = Path.Combine(root, SkillBasePath);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MauiDevFlow-CLI", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        // Discover files via GitHub Trees API (recursive)
        Console.WriteLine("Fetching skill file list from GitHub...");
        List<string> files;
        try
        {
            files = await GetSkillFilesFromGitHubAsync(http, branch);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to fetch file list: {ex.Message}");
            return;
        }

        if (files.Count == 0)
        {
            Console.Error.WriteLine("No skill files found in the repository.");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("maui-devflow update-skill");
        Console.WriteLine($"  Source: https://github.com/{SkillRepo}/tree/{branch}/{SkillBasePath}");
        Console.WriteLine($"  Destination: {destBase}");
        Console.WriteLine();
        Console.WriteLine("Files to download:");
        foreach (var file in files)
        {
            var destPath = Path.Combine(destBase, file);
            var exists = File.Exists(destPath);
            Console.WriteLine($"  {SkillBasePath}/{file}{(exists ? " (overwrite)" : " (new)")}");
        }
        Console.WriteLine();

        if (!force)
        {
            Console.Write("Existing files will be overwritten. Continue? [y/N] ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (response is not ("y" or "yes"))
            {
                Console.WriteLine("Cancelled.");
                return;
            }
        }

        var success = 0;
        foreach (var file in files)
        {
            var url = $"https://raw.githubusercontent.com/{SkillRepo}/{branch}/{SkillBasePath}/{file}";
            var destPath = Path.Combine(destBase, file);

            try
            {
                var content = await http.GetStringAsync(url);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                await File.WriteAllTextAsync(destPath, content);
                Console.WriteLine($"  ✓ {file}");
                success++;
            }
            catch (HttpRequestException ex)
            {
                Console.Error.WriteLine($"  ✗ {file}: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(success == files.Count
            ? $"Done. {success} files updated."
            : $"Done. {success}/{files.Count} files updated.");

        // Write .skill-version with the latest commit SHA
        await WriteSkillVersionAsync(http, destBase, branch);
    }

    private static async Task WriteSkillVersionAsync(HttpClient http, string destBase, string branch)
    {
        try
        {
            var sha = await GetRemoteSkillCommitShaAsync(http, branch);
            if (sha == null) return;

            var versionInfo = new
            {
                commit = sha,
                updatedAt = DateTime.UtcNow.ToString("o"),
                branch
            };
            var versionPath = Path.Combine(destBase, ".skill-version");
            await File.WriteAllTextAsync(versionPath,
                JsonSerializer.Serialize(versionInfo, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-fatal — version tracking is best-effort */ }
    }

    private static async Task<string?> GetRemoteSkillCommitShaAsync(HttpClient http, string branch)
    {
        var url = $"https://api.github.com/repos/{SkillRepo}/commits?path={SkillBasePath}&sha={branch}&per_page=1";
        var json = await http.GetStringAsync(url);
        var commits = JsonSerializer.Deserialize<JsonElement>(json);
        foreach (var commit in commits.EnumerateArray())
            return commit.GetProperty("sha").GetString();
        return null;
    }

    private static async Task SkillVersionAsync(string? outputDir, string branch)
    {
        var root = outputDir ?? Directory.GetCurrentDirectory();
        var destBase = Path.Combine(root, SkillBasePath);
        var versionPath = Path.Combine(destBase, ".skill-version");

        // Read local version
        string? localSha = null;
        string? localDate = null;
        string? localBranch = null;
        if (File.Exists(versionPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(versionPath);
                var doc = JsonSerializer.Deserialize<JsonElement>(json);
                localSha = doc.TryGetProperty("commit", out var c) ? c.GetString() : null;
                localDate = doc.TryGetProperty("updatedAt", out var d) ? d.GetString() : null;
                localBranch = doc.TryGetProperty("branch", out var b) ? b.GetString() : null;
            }
            catch { /* corrupt file */ }
        }

        if (localSha == null)
        {
            Console.WriteLine("No local skill version found.");
            Console.WriteLine("Run 'maui-devflow update-skill' to install the skill and track its version.");
            return;
        }

        Console.WriteLine($"Installed: {localSha[..12]} (branch: {localBranch ?? "unknown"})");
        if (localDate != null && DateTime.TryParse(localDate, out var dt))
            Console.WriteLine($"Updated:   {dt:yyyy-MM-dd HH:mm:ss} UTC");

        // Check remote
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MauiDevFlow-CLI", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        try
        {
            var remoteSha = await GetRemoteSkillCommitShaAsync(http, branch);
            if (remoteSha == null)
            {
                Console.WriteLine("Could not fetch remote version.");
                return;
            }

            Console.WriteLine($"Remote:    {remoteSha[..12]} (branch: {branch})");

            if (string.Equals(localSha, remoteSha, StringComparison.OrdinalIgnoreCase))
                Console.WriteLine("\n✓ Skill is up to date.");
            else
                Console.WriteLine("\n⚠ Update available! Run 'maui-devflow update-skill' to get the latest version.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not check remote: {ex.Message}");
        }
    }

    private static async Task<List<string>> GetSkillFilesFromGitHubAsync(HttpClient http, string branch)
    {
        var files = new List<string>();
        await ListGitHubDirectoryAsync(http, SkillBasePath, "", files, branch);
        return files;
    }

    private static async Task ListGitHubDirectoryAsync(HttpClient http, string basePath, string relativePath, List<string> files, string branch)
    {
        var apiPath = string.IsNullOrEmpty(relativePath) ? basePath : $"{basePath}/{relativePath}";
        var url = $"https://api.github.com/repos/{SkillRepo}/contents/{apiPath}?ref={branch}";
        var json = await http.GetStringAsync(url);
        var items = JsonSerializer.Deserialize<JsonElement>(json);

        foreach (var item in items.EnumerateArray())
        {
            var name = item.GetProperty("name").GetString()!;
            var type = item.GetProperty("type").GetString()!;
            var itemRelative = string.IsNullOrEmpty(relativePath) ? name : $"{relativePath}/{name}";

            if (type == "file")
                files.Add(itemRelative);
            else if (type == "dir")
                await ListGitHubDirectoryAsync(http, basePath, itemRelative, files, branch);
        }
    }

    // ===== MAUI Agent Commands =====

    private static async Task MauiStatusAsync(string host, int port, int? window)
    {
        try
        {
            using var client = new MauiDevFlow.Driver.AgentClient(host, port);
            var status = await client.GetStatusAsync(window);
            if (status == null)
            {
                WriteError($"Cannot connect to agent at {host}:{port}");
                return;
            }
            Console.WriteLine($"Agent: {status.Agent} v{status.Version}");
            Console.WriteLine($"Platform: {status.Platform}");
            Console.WriteLine($"Device: {status.DeviceType} ({status.Idiom})");
            Console.WriteLine($"App: {status.AppName}");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    private static async Task MauiTreeAsync(string host, int port, int depth, int? window)
    {
        try
        {
            using var client = new MauiDevFlow.Driver.AgentClient(host, port);
            var tree = await client.GetTreeAsync(depth, window);
            PrintTree(tree, 0);
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    private static async Task MauiQueryAsync(string host, int port, string? type, string? autoId, string? text, string? selector)
    {
        try
        {
            using var client = new MauiDevFlow.Driver.AgentClient(host, port);
            List<MauiDevFlow.Driver.ElementInfo> results;

            if (!string.IsNullOrWhiteSpace(selector))
                results = await client.QueryCssAsync(selector);
            else
                results = await client.QueryAsync(type, autoId, text);

            if (results.Count == 0)
            {
                Console.WriteLine("No elements found");
                return;
            }
            Console.WriteLine($"Found {results.Count} element(s):");
            foreach (var el in results)
            {
                Console.WriteLine($"  [{el.Id}] {el.Type}" +
                    (el.AutomationId != null ? $" automationId=\"{el.AutomationId}\"" : "") +
                    (el.Text != null ? $" text=\"{el.Text}\"" : "") +
                    (el.IsVisible ? "" : " [hidden]") +
                    (el.IsEnabled ? "" : " [disabled]"));
            }
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    private static async Task MauiTapAsync(string host, int port, string elementId)
    {
        try
        {
            using var client = new MauiDevFlow.Driver.AgentClient(host, port);
            var success = await client.TapAsync(elementId);
            Console.WriteLine(success ? $"Tapped: {elementId}" : $"Failed to tap: {elementId}");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    private static async Task MauiFillAsync(string host, int port, string elementId, string text)
    {
        try
        {
            using var client = new MauiDevFlow.Driver.AgentClient(host, port);
            var success = await client.FillAsync(elementId, text);
            Console.WriteLine(success ? $"Filled: {elementId}" : $"Failed to fill: {elementId}");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    private static async Task MauiClearAsync(string host, int port, string elementId)
    {
        try
        {
            using var client = new MauiDevFlow.Driver.AgentClient(host, port);
            var success = await client.ClearAsync(elementId);
            Console.WriteLine(success ? $"Cleared: {elementId}" : $"Failed to clear: {elementId}");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    private static async Task MauiScreenshotAsync(string host, int port, string? output, int? window, string? id, string? selector)
    {
        try
        {
            using var client = new MauiDevFlow.Driver.AgentClient(host, port);
            var data = await client.ScreenshotAsync(window, id, selector);
            if (data == null)
            {
                WriteError("Failed to capture screenshot");
                return;
            }
            var filename = output ?? $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            await File.WriteAllBytesAsync(filename, data);
            var target = id != null ? $" (element: {id})" : selector != null ? $" (selector: {selector})" : "";
            Console.WriteLine($"Screenshot saved: {Path.GetFullPath(filename)} ({data.Length} bytes){target}");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    private static async Task RecordingStartAsync(string host, int port, string platform, string? output, int timeout)
    {
        try
        {
            var filename = output ?? $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
            using var driver = MauiDevFlow.Driver.AppDriverFactory.Create(platform);
            await driver.StartRecordingAsync(filename, timeout);
            Console.WriteLine($"Recording started (timeout: {timeout}s)");
            Console.WriteLine($"Output: {Path.GetFullPath(filename)}");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    private static async Task RecordingStopAsync(string host, int port, string platform)
    {
        try
        {
            using var driver = MauiDevFlow.Driver.AppDriverFactory.Create(platform);
            var outputFile = await driver.StopRecordingAsync();
            var size = File.Exists(outputFile) ? new FileInfo(outputFile).Length : 0;
            Console.WriteLine($"Recording saved: {outputFile} ({size} bytes)");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    private static void RecordingStatusAsync()
    {
        var state = MauiDevFlow.Driver.RecordingStateManager.Load();
        if (state == null || !MauiDevFlow.Driver.RecordingStateManager.IsRecording())
        {
            Console.WriteLine("No active recording.");
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - state.StartedAt;
        Console.WriteLine($"Recording in progress:");
        Console.WriteLine($"  Platform: {state.Platform}");
        Console.WriteLine($"  Output:   {state.OutputFile}");
        Console.WriteLine($"  Elapsed:  {elapsed.TotalSeconds:F0}s / {state.TimeoutSeconds}s");
        Console.WriteLine($"  PID:      {state.RecordingPid}");
    }

    private static async Task MauiPropertyAsync(string host, int port, string elementId, string propertyName)
    {
        try
        {
            using var client = new MauiDevFlow.Driver.AgentClient(host, port);
            var value = await client.GetPropertyAsync(elementId, propertyName);
            Console.WriteLine(value != null ? $"{propertyName}: {value}" : $"Property '{propertyName}' not found");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    private static async Task MauiSetPropertyAsync(string host, int port, string elementId, string propertyName, string value)
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(10);
            var json = JsonSerializer.Serialize(new { value });
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await http.PostAsync($"http://{host}:{port}/api/property/{elementId}/{propertyName}", content);
            var body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
                Console.WriteLine($"Set {propertyName} = {value}");
            else
                WriteError($"Failed: {body}");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    private static async Task MauiElementAsync(string host, int port, string elementId)
    {
        try
        {
            using var client = new MauiDevFlow.Driver.AgentClient(host, port);
            var el = await client.GetElementAsync(elementId);
            if (el == null)
            {
                WriteError($"Element '{elementId}' not found");
                return;
            }
            Console.WriteLine(JsonSerializer.Serialize(el, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    private static async Task MauiNavigateAsync(string host, int port, string route)
    {
        try
        {
            using var client = new MauiDevFlow.Driver.AgentClient(host, port);
            var success = await client.NavigateAsync(route);
            Console.WriteLine(success ? $"Navigated to: {route}" : $"Failed to navigate to: {route}");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    private static async Task MauiScrollAsync(string host, int port, string? elementId, double dx, double dy, bool animated, int? window)
    {
        try
        {
            using var client = new MauiDevFlow.Driver.AgentClient(host, port);
            var success = await client.ScrollAsync(elementId, dx, dy, animated, window);
            if (elementId != null)
                Console.WriteLine(success ? $"Scrolled to element: {elementId}" : $"Failed to scroll to element: {elementId}");
            else
                Console.WriteLine(success ? $"Scrolled by dx={dx}, dy={dy}" : "Failed to scroll");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    private static async Task MauiFocusAsync(string host, int port, string elementId)
    {
        try
        {
            using var client = new MauiDevFlow.Driver.AgentClient(host, port);
            var success = await client.FocusAsync(elementId);
            Console.WriteLine(success ? $"Focused: {elementId}" : $"Failed to focus: {elementId}");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    private static async Task MauiResizeAsync(string host, int port, int width, int height, int? window)
    {
        try
        {
            using var client = new MauiDevFlow.Driver.AgentClient(host, port);
            var success = await client.ResizeAsync(width, height, window);
            Console.WriteLine(success ? $"Resized to: {width}x{height}" : $"Failed to resize");
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    private static async Task MauiLogsAsync(string host, int port, int limit, int skip, string? source)
    {
        try
        {
            using var http = new HttpClient();
            http.BaseAddress = new Uri($"http://{host}:{port}");
            var url = $"/api/logs?limit={limit}&skip={skip}";
            if (!string.IsNullOrEmpty(source))
                url += $"&source={Uri.EscapeDataString(source)}";
            var response = await http.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                WriteError($"Failed to fetch logs: {response.StatusCode} {json}");
                return;
            }

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                Console.WriteLine(json);
                return;
            }

            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                var ts = entry.GetProperty("t").GetString() ?? "";
                var level = entry.GetProperty("l").GetString() ?? "";
                var category = entry.GetProperty("c").GetString() ?? "";
                var message = entry.GetProperty("m").GetString() ?? "";
                var exception = entry.TryGetProperty("e", out var eProp) ? eProp.GetString() : null;
                var logSource = entry.TryGetProperty("s", out var sProp) ? sProp.GetString() : null;

                // Color-code by level
                var color = level switch
                {
                    "Critical" or "Error" => ConsoleColor.Red,
                    "Warning" => ConsoleColor.Yellow,
                    "Debug" or "Trace" => ConsoleColor.DarkGray,
                    _ => ConsoleColor.White
                };

                var saved = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.Write($"[{ts}] ");
                Console.Write($"{level,-12} ");

                // Show source tag for webview logs
                if (logSource == "webview")
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.Write("[WebView] ");
                }

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"{category}: ");
                Console.ForegroundColor = color;
                Console.WriteLine(message);
                if (!string.IsNullOrEmpty(exception))
                {
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.WriteLine($"  Exception: {exception}");
                }
                Console.ForegroundColor = saved;
            }
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    private static void PrintTree(List<MauiDevFlow.Driver.ElementInfo> elements, int indent)
    {
        foreach (var el in elements)
        {
            var prefix = new string(' ', indent * 2);
            var info = $"{prefix}[{el.Id}] {el.Type}";
            if (el.AutomationId != null) info += $" automationId=\"{el.AutomationId}\"";
            if (el.Text != null) info += $" text=\"{el.Text}\"";
            if (!el.IsVisible) info += " [hidden]";
            if (!el.IsEnabled) info += " [disabled]";
            if (el.Bounds != null) info += $" ({el.Bounds.X:F0},{el.Bounds.Y:F0} {el.Bounds.Width:F0}x{el.Bounds.Height:F0})";
            Console.WriteLine(info);
            if (el.Children != null)
                PrintTree(el.Children, indent + 1);
        }
    }

    // ===== Alert & Permission Commands (iOS Simulator) =====

    private static async Task<string> ResolveUdidAsync(string? udid)
    {
        if (!string.IsNullOrEmpty(udid)) return udid;

        // Auto-detect booted simulator
        var psi = new System.Diagnostics.ProcessStartInfo("xcrun", "simctl list devices booted -j")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var output = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();

        using var doc = JsonDocument.Parse(output);
        if (doc.RootElement.TryGetProperty("devices", out var devices))
        {
            foreach (var runtime in devices.EnumerateObject())
            {
                foreach (var device in runtime.Value.EnumerateArray())
                {
                    var state = device.TryGetProperty("state", out var s) ? s.GetString() : null;
                    if (state == "Booted")
                    {
                        var resolved = device.GetProperty("udid").GetString()!;
                        return resolved;
                    }
                }
            }
        }
        throw new InvalidOperationException("No booted simulator found. Specify --udid or boot a simulator.");
    }

    private static async Task<string> ResolveAlertPlatformAsync(string platform, string host, int port)
    {
        var p = platform.ToLowerInvariant();
        if (p.Contains("catalyst")) return "maccatalyst";
        if (p.Contains("ios") || p.Contains("simulator")) return "ios-simulator";
        if (p.Contains("android")) return "android";
        if (p.Contains("windows") || p.Contains("win")) return "windows";
        if (p.Contains("linux") || p.Contains("gtk")) return "linux";

        // Auto-detect from agent
        try
        {
            using var client = new MauiDevFlow.Driver.AgentClient(host, port);
            var status = await client.GetStatusAsync();
            if (status?.Platform != null)
            {
                var sp = status.Platform.ToLowerInvariant();
                if (sp.Contains("catalyst")) return "maccatalyst";
                if (sp.Contains("android")) return "android";
                if (sp.Contains("ios")) return "ios-simulator";
                if (sp.Contains("windows")) return "windows";
                if (sp.Contains("linux") || sp.Contains("gtk")) return "linux";
            }
        }
        catch { }

        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsLinux()) return "linux";
        return "maccatalyst";
    }

    private static async Task<int> ResolveMacCatalystPidAsync(int? pid, string host, int port)
    {
        if (pid.HasValue) return pid.Value;

        // Try to find the PID by checking what's listening on the agent port
        try
        {
            using var client = new MauiDevFlow.Driver.AgentClient(host, port);
            var status = await client.GetStatusAsync();
            if (status?.AppName != null)
            {
                // Find process by app name
                var psi = new System.Diagnostics.ProcessStartInfo("pgrep", $"-f {status.AppName}")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                using var proc = System.Diagnostics.Process.Start(psi)!;
                var output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();
                var lines = output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 0 && int.TryParse(lines[0].Trim(), out var resolved))
                    return resolved;
            }
        }
        catch { }

        throw new InvalidOperationException("Cannot determine Mac Catalyst app PID. Specify --pid.");
    }

    private static async Task<int> ResolveWindowsPidAsync(int? pid, string host, int port)
    {
        if (pid.HasValue) return pid.Value;

        try
        {
            using var client = new MauiDevFlow.Driver.AgentClient(host, port);
            var status = await client.GetStatusAsync();
            if (status?.AppName != null)
            {
                var processes = System.Diagnostics.Process.GetProcessesByName(status.AppName);
                if (processes.Length > 0)
                    return processes[0].Id;

                var match = System.Diagnostics.Process.GetProcesses()
                    .FirstOrDefault(p =>
                    {
                        try { return p.ProcessName.Contains(status.AppName, StringComparison.OrdinalIgnoreCase); }
                        catch { return false; }
                    });
                if (match != null)
                    return match.Id;
            }
        }
        catch { }

        throw new InvalidOperationException("Cannot determine Windows app PID. Specify --pid.");
    }

    private static async Task AlertDetectAsync(string? udid, int? pid, string platform, string host, int port)
    {
        try
        {
            var plat = await ResolveAlertPlatformAsync(platform, host, port);

            if (plat == "maccatalyst")
            {
                var resolvedPid = await ResolveMacCatalystPidAsync(pid, host, port);
                var driver = new MauiDevFlow.Driver.MacCatalystAppDriver { ProcessId = resolvedPid };
                var alert = await driver.DetectAlertAsync();
                if (alert is null) { Console.WriteLine("No alert detected"); return; }
                Console.WriteLine($"Alert: {alert.Title ?? "(no title)"}");
                foreach (var btn in alert.Buttons)
                    Console.WriteLine($"  Button: \"{btn.Label}\"");
            }
            else if (plat == "android")
            {
                var driver = new MauiDevFlow.Driver.AndroidAppDriver { Serial = udid };
                var alert = await driver.DetectAlertAsync();
                if (alert is null) { Console.WriteLine("No alert detected"); return; }
                Console.WriteLine($"Alert: {alert.Title ?? "(no title)"}");
                foreach (var btn in alert.Buttons)
                    Console.WriteLine($"  Button: \"{btn.Label}\" at ({btn.CenterX}, {btn.CenterY})");
            }
            else if (plat == "windows")
            {
                var resolvedPid = await ResolveWindowsPidAsync(pid, host, port);
                var driver = new MauiDevFlow.Driver.WindowsAppDriver { ProcessId = resolvedPid };
                var alert = await driver.DetectAlertAsync();
                if (alert is null) { Console.WriteLine("No alert detected"); return; }
                Console.WriteLine($"Alert: {alert.Title ?? "(no title)"}");
                foreach (var btn in alert.Buttons)
                    Console.WriteLine($"  Button: \"{btn.Label}\"");
            }
            else
            {
                var resolved = await ResolveUdidAsync(udid);
                var driver = new MauiDevFlow.Driver.iOSSimulatorAppDriver { DeviceUdid = resolved };
                var alert = await driver.DetectAlertAsync();
                if (alert is null) { Console.WriteLine("No alert detected"); return; }
                Console.WriteLine($"Alert: {alert.Title ?? "(no title)"}");
                foreach (var btn in alert.Buttons)
                    Console.WriteLine($"  Button: \"{btn.Label}\" at ({btn.CenterX}, {btn.CenterY})");
            }
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    private static async Task AlertDismissAsync(string? udid, int? pid, string platform, string host, int port, string? buttonLabel)
    {
        try
        {
            var plat = await ResolveAlertPlatformAsync(platform, host, port);

            if (plat == "maccatalyst")
            {
                var resolvedPid = await ResolveMacCatalystPidAsync(pid, host, port);
                var driver = new MauiDevFlow.Driver.MacCatalystAppDriver { ProcessId = resolvedPid };
                var alert = await driver.HandleAlertIfPresentAsync(buttonLabel);
                if (alert is null) Console.WriteLine("No alert to dismiss");
                else Console.WriteLine($"Dismissed: {alert.Title ?? "(alert)"}");
            }
            else if (plat == "android")
            {
                var driver = new MauiDevFlow.Driver.AndroidAppDriver { Serial = udid };
                var alert = await driver.HandleAlertIfPresentAsync(buttonLabel);
                if (alert is null) Console.WriteLine("No alert to dismiss");
                else Console.WriteLine($"Dismissed: {alert.Title ?? "(alert)"}");
            }
            else if (plat == "windows")
            {
                var resolvedPid = await ResolveWindowsPidAsync(pid, host, port);
                var driver = new MauiDevFlow.Driver.WindowsAppDriver { ProcessId = resolvedPid };
                var alert = await driver.HandleAlertIfPresentAsync(buttonLabel);
                if (alert is null) Console.WriteLine("No alert to dismiss");
                else Console.WriteLine($"Dismissed: {alert.Title ?? "(alert)"}");
            }
            else
            {
                var resolved = await ResolveUdidAsync(udid);
                var driver = new MauiDevFlow.Driver.iOSSimulatorAppDriver { DeviceUdid = resolved };
                var alert = await driver.HandleAlertIfPresentAsync(buttonLabel);
                if (alert is null) Console.WriteLine("No alert to dismiss");
                else Console.WriteLine($"Dismissed: {alert.Title ?? "(alert)"}");
            }
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    private static async Task AlertTreeAsync(string? udid, int? pid, string platform, string host, int port)
    {
        try
        {
            var plat = await ResolveAlertPlatformAsync(platform, host, port);

            if (plat == "maccatalyst")
            {
                var resolvedPid = await ResolveMacCatalystPidAsync(pid, host, port);
                var driver = new MauiDevFlow.Driver.MacCatalystAppDriver { ProcessId = resolvedPid };
                var tree = await driver.GetAccessibilityTreeAsync();
                Console.WriteLine(tree);
            }
            else if (plat == "android")
            {
                var driver = new MauiDevFlow.Driver.AndroidAppDriver { Serial = udid };
                var tree = await driver.GetAccessibilityTreeAsync();
                Console.WriteLine(tree);
            }
            else if (plat == "windows")
            {
                var resolvedPid = await ResolveWindowsPidAsync(pid, host, port);
                var driver = new MauiDevFlow.Driver.WindowsAppDriver { ProcessId = resolvedPid };
                var tree = await driver.GetAccessibilityTreeAsync();
                Console.WriteLine(tree);
            }
            else
            {
                var resolved = await ResolveUdidAsync(udid);
                var driver = new MauiDevFlow.Driver.iOSSimulatorAppDriver { DeviceUdid = resolved };
                var json = await driver.GetAccessibilityTreeAsync();
                using var doc = JsonDocument.Parse(json);
                Console.WriteLine(JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    private static async Task PermissionAsync(string action, string? udid, string? bundleId, string service)
    {
        try
        {
            var resolved = await ResolveUdidAsync(udid);
            // Run xcrun simctl privacy directly (driver methods require BundleId which may not be set)
            var args = string.IsNullOrEmpty(bundleId)
                ? $"simctl privacy {resolved} {action} {service}"
                : $"simctl privacy {resolved} {action} {service} {bundleId}";

            var psi = new System.Diagnostics.ProcessStartInfo("xcrun", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0)
            {
                var stderr = await proc.StandardError.ReadToEndAsync();
                WriteError($"simctl privacy failed: {stderr.Trim()}");
                return;
            }
            Console.WriteLine($"Permission {action}: {service}" + (bundleId != null ? $" for {bundleId}" : ""));
        }
        catch (Exception ex) { WriteError(ex.Message); }
    }

    /// <summary>
    /// Reads the port from .mauidevflow in the current directory.
    /// </summary>
    private static int? ReadConfigPort()
    {
        try
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), ".mauidevflow");
            if (!File.Exists(path)) return null;

            var json = File.ReadAllText(path);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("port", out var portProp) && portProp.ValueKind == JsonValueKind.Number)
                return portProp.GetInt32();
        }
        catch { /* ignore parse failures */ }
        return null;
    }

    /// <summary>
    /// Resolves the agent port: broker discovery → .mauidevflow config → default 9223.
    /// </summary>
    private static int ResolveAgentPort()
    {
        // Try broker discovery
        try
        {
            var brokerPort = Broker.BrokerClient.ReadBrokerPortPublic() ?? Broker.BrokerServer.DefaultPort;

            // Quick TCP check if broker is alive; auto-start if not
            bool brokerAlive = false;
            try
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                tcp.ConnectAsync("localhost", brokerPort).Wait(TimeSpan.FromMilliseconds(300));
                brokerAlive = tcp.Connected;
            }
            catch { /* connect failed or timed out — broker not alive */ }

            if (!brokerAlive)
            {
                // Auto-start broker in background
                try
                {
                    var brokerResult = Broker.BrokerClient.EnsureBrokerRunningAsync().GetAwaiter().GetResult();
                    if (brokerResult.HasValue)
                    {
                        brokerPort = brokerResult.Value;
                        brokerAlive = true;
                    }
                }
                catch { }
            }

            if (brokerAlive)
            {
                // Find project in current directory
                var csproj = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj").FirstOrDefault();
                if (csproj != null)
                {
                    var port = Broker.BrokerClient.ResolveAgentPortAsync(brokerPort, Path.GetFullPath(csproj)).GetAwaiter().GetResult();
                    if (port.HasValue) return port.Value;
                }

                // Try auto-select (single agent)
                var autoPort = Broker.BrokerClient.ResolveAgentPortAsync(brokerPort).GetAwaiter().GetResult();
                if (autoPort.HasValue) return autoPort.Value;

                // Multiple agents, can't disambiguate — show them so the caller
                // (human or AI agent) can re-run with --agent-port
                // Only show if we won't have a config file fallback
                var configPort = ReadConfigPort();
                if (configPort.HasValue) return configPort.Value;

                var agents = Broker.BrokerClient.ListAgentsAsync(brokerPort).GetAwaiter().GetResult();
                if (agents != null && agents.Length > 1)
                {
                    Console.Error.WriteLine("Multiple agents connected. Use --agent-port to specify which one:");
                    Console.Error.WriteLine();
                    Console.Error.WriteLine($"{"ID",-15}{"App",-20}{"Platform",-15}{"TFM",-25}{"Port",-7}");
                    Console.Error.WriteLine(new string('-', 82));
                    foreach (var a in agents)
                        Console.Error.WriteLine($"{a.Id,-15}{a.AppName,-20}{a.Platform,-15}{a.Tfm,-25}{a.Port,-7}");
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("Example: maui-devflow MAUI status --agent-port <port>");
                }
            }
        }
        catch { /* broker unavailable, fall through */ }

        // Fall back to config file (already checked above if broker was alive)
        return ReadConfigPort() ?? 9223;
    }

    // ===== Broker Commands =====

    private static async Task BrokerStartAsync(bool foreground)
    {
        if (foreground)
        {
            Console.CancelKeyPress += (_, e) => e.Cancel = true;
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, _) => cts.Cancel();

            using var server = new Broker.BrokerServer(
                log: msg => Console.WriteLine(msg));
            await server.RunAsync(cts.Token);
        }
        else
        {
            var port = await Broker.BrokerClient.EnsureBrokerRunningAsync();
            if (port.HasValue)
                Console.WriteLine($"Broker running on port {port.Value}");
            else
                Console.WriteLine("Failed to start broker");
        }
    }

    private static async Task BrokerStopAsync()
    {
        var success = await Broker.BrokerClient.ShutdownBrokerAsync();
        Console.WriteLine(success ? "Broker shutdown requested" : "Broker is not running");
    }

    private static async Task BrokerStatusAsync()
    {
        var port = Broker.BrokerClient.ReadBrokerPortPublic();

        // No state file — try default port as fallback (broker may still be alive)
        if (port == null)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var response = await http.GetStringAsync($"http://localhost:{Broker.BrokerServer.DefaultPort}/api/health");
                var doc = JsonDocument.Parse(response);
                var agents = doc.RootElement.GetProperty("agents").GetInt32();
                Console.WriteLine($"Broker: running on port {Broker.BrokerServer.DefaultPort} ({agents} agent(s) connected) [no state file]");
                return;
            }
            catch { }

            Console.WriteLine("Broker: not running");
            return;
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = await http.GetStringAsync($"http://localhost:{port}/api/health");
            var doc = JsonDocument.Parse(response);
            var agents = doc.RootElement.GetProperty("agents").GetInt32();
            Console.WriteLine($"Broker: running on port {port} ({agents} agent(s) connected)");
        }
        catch
        {
            Console.WriteLine($"Broker: not responding on port {port} (stale state file?)");
        }
    }

    private static void BrokerLogAsync()
    {
        var logPath = Broker.BrokerPaths.LogFile;
        if (!File.Exists(logPath))
        {
            Console.WriteLine("No broker log found");
            return;
        }

        // Show last 50 lines
        var lines = File.ReadAllLines(logPath);
        var start = Math.Max(0, lines.Length - 50);
        for (int i = start; i < lines.Length; i++)
            Console.WriteLine(lines[i]);
    }

    private static async Task ListAgentsCommandAsync()
    {
        var port = await Broker.BrokerClient.EnsureBrokerRunningAsync();
        if (port == null)
        {
            Console.WriteLine("Broker unavailable");
            return;
        }

        var agents = await Broker.BrokerClient.ListAgentsAsync(port.Value);
        if (agents == null || agents.Length == 0)
        {
            Console.WriteLine("No agents connected");
            return;
        }

        Console.WriteLine($"{"ID",-14} {"App",-20} {"Platform",-14} {"TFM",-24} {"Port",-6} {"Uptime"}");
        Console.WriteLine(new string('-', 90));
        foreach (var a in agents)
        {
            var uptime = DateTime.UtcNow - a.ConnectedAt;
            var uptimeStr = uptime.TotalHours >= 1
                ? $"{uptime.Hours}h {uptime.Minutes}m"
                : $"{uptime.Minutes}m {uptime.Seconds}s";
            Console.WriteLine($"{a.Id,-14} {a.AppName,-20} {a.Platform,-14} {a.Tfm,-24} {a.Port,-6} {uptimeStr}");
        }
    }

    private static async Task WaitForAgentCommandAsync(int timeoutSeconds, string? projectFilter, string? platformFilter, bool json)
    {
        var brokerPort = await Broker.BrokerClient.EnsureBrokerRunningAsync();
        if (brokerPort == null)
        {
            Console.Error.WriteLine("Error: Broker unavailable");
            Environment.ExitCode = 1;
            return;
        }

        // Resolve project filter to full path for matching
        string? resolvedProject = null;
        if (projectFilter != null)
        {
            resolvedProject = Path.GetFullPath(projectFilter);
        }

        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var pollInterval = TimeSpan.FromMilliseconds(500);
        Broker.AgentRegistration? matched = null;

        while (DateTime.UtcNow < deadline)
        {
            var agents = await Broker.BrokerClient.ListAgentsAsync(brokerPort.Value);
            if (agents != null && agents.Length > 0)
            {
                matched = FindMatchingAgent(agents, resolvedProject, platformFilter);
                if (matched != null)
                    break;
            }

            await Task.Delay(pollInterval);
        }

        if (matched == null)
        {
            Console.Error.WriteLine($"Timeout: no matching agent connected within {timeoutSeconds}s");
            Environment.ExitCode = 1;
            return;
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(matched));
        }
        else
        {
            Console.WriteLine(matched.Port);
        }
    }

    private static Broker.AgentRegistration? FindMatchingAgent(Broker.AgentRegistration[] agents, string? projectFilter, string? platformFilter)
    {
        foreach (var agent in agents)
        {
            if (projectFilter != null && !string.Equals(agent.Project, projectFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (platformFilter != null && !agent.Platform.Contains(platformFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            return agent;
        }
        return null;
    }

    // ===== Batch command: interactive stdin/stdout with JSONL responses =====

    private static async Task BatchAsync(string host, int port, int delayMs, bool continueOnError, bool human)
    {
        var commandIndex = 0;
        var succeeded = 0;
        var failed = 0;
        var originalOut = Console.Out;
        var originalErr = Console.Error;

        using var stdin = Console.In;
        string? line;
        while ((line = stdin.ReadLine()) != null)
        {
            var commands = SplitBatchLine(line);
            foreach (var rawCmd in commands)
            {
                commandIndex++;
                var args = TokenizeCommand(rawCmd);
                if (args.Length == 0) continue;

                var prefix = args[0].ToUpperInvariant();
                if (prefix != "MAUI" && prefix != "CDP")
                {
                    var errMsg = $"Only MAUI and cdp commands are supported in batch mode, got: {args[0]}";
                    if (human)
                    {
                        originalOut.WriteLine($"[{commandIndex}] {rawCmd}");
                        originalErr.WriteLine($"Error: {errMsg}");
                    }
                    else
                    {
                        var errJson = JsonSerializer.Serialize(new { command = rawCmd, exit_code = 1, output = $"Error: {errMsg}" });
                        originalOut.WriteLine(errJson);
                        originalOut.Flush();
                    }
                    failed++;
                    if (!continueOnError) goto done;
                    continue;
                }

                // Inject resolved port/host so sub-commands don't re-query broker
                var fullArgs = new List<string>(args) { "--agent-port", port.ToString(), "--agent-host", host };

                // Capture stdout/stderr from the sub-command
                var outCapture = new StringWriter();
                var errCapture = new StringWriter();
                Console.SetOut(outCapture);
                Console.SetError(errCapture);

                _errorOccurred = false;
                int exitCode;
                try
                {
                    exitCode = await _parser!.InvokeAsync(fullArgs.ToArray());
                }
                finally
                {
                    Console.SetOut(originalOut);
                    Console.SetError(originalErr);
                }
                if (_errorOccurred) exitCode = 1;

                var output = outCapture.ToString().TrimEnd('\r', '\n');
                var errOutput = errCapture.ToString().TrimEnd('\r', '\n');
                var combinedOutput = string.IsNullOrEmpty(errOutput) ? output : $"{output}\n{errOutput}".TrimStart('\n');

                if (exitCode == 0) succeeded++;
                else failed++;

                if (human)
                {
                    originalOut.WriteLine($"[{commandIndex}] {rawCmd}");
                    if (!string.IsNullOrEmpty(combinedOutput))
                        originalOut.WriteLine(combinedOutput);
                }
                else
                {
                    var jsonResponse = JsonSerializer.Serialize(new { command = rawCmd, exit_code = exitCode, output = combinedOutput });
                    originalOut.WriteLine(jsonResponse);
                    originalOut.Flush();
                }

                if (exitCode != 0 && !continueOnError) goto done;

                if (delayMs > 0)
                    await Task.Delay(delayMs);
            }
        }

    done:
        if (human)
        {
            originalOut.WriteLine();
            if (failed == 0)
                originalOut.WriteLine($"Batch complete: {succeeded}/{succeeded + failed} commands succeeded");
            else
                originalOut.WriteLine($"Batch stopped: {succeeded}/{succeeded + failed} commands succeeded, {failed} failed");
        }
    }

    private static List<string> SplitBatchLine(string line)
    {
        var commands = new List<string>();
        var inQuote = false;
        var current = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"') { inQuote = !inQuote; current.Append(c); }
            else if (c == ';' && !inQuote)
            {
                var cmd = current.ToString().Trim();
                if (cmd.Length > 0 && !cmd.StartsWith('#'))
                    commands.Add(cmd);
                current.Clear();
            }
            else { current.Append(c); }
        }

        var last = current.ToString().Trim();
        if (last.Length > 0 && !last.StartsWith('#'))
            commands.Add(last);

        return commands;
    }

    private static string[] TokenizeCommand(string command)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuote = false;

        for (int i = 0; i < command.Length; i++)
        {
            var c = command[i];
            if (c == '"')
            {
                inQuote = !inQuote;
            }
            else if (char.IsWhiteSpace(c) && !inQuote)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens.ToArray();
    }
}
