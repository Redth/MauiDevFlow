namespace MauiDevFlow.Blazor;

/// <summary>
/// Base class for BlazorWebView debug services. Contains all shared logic for
/// CDP command handling and script injection. Platform-specific
/// subclasses provide the WebView capture and JavaScript evaluation.
/// </summary>
public abstract class BlazorWebViewDebugServiceBase : IDisposable
{
    protected bool IsInitialized;
    private bool _disposed;
    private bool _injecting;
    private CancellationTokenSource? _drainCts;

    /// <summary>Optional log callback for debug messages.</summary>
    public Action<string>? LogCallback { get; set; }

    /// <summary>
    /// Callback for routing WebView console logs to the native logging pipeline.
    /// Parameters: level, message, exception (nullable).
    /// </summary>
    public Action<string, string, string?>? WebViewLogCallback { get; set; }

    public bool IsReady => IsInitialized && HasWebView && _chobitsuLoaded;

    private bool _chobitsuLoaded;

    protected BlazorWebViewDebugServiceBase() { }

    /// <summary>
    /// Evaluates JavaScript in the WebView and returns the result.
    /// Must be called on the main/UI thread.
    /// </summary>
    protected abstract Task<string?> EvaluateJavaScriptAsync(string script);

    /// <summary>Whether the platform WebView has been captured and is ready.</summary>
    protected abstract bool HasWebView { get; }

    /// <summary>Reload the current page via the native WebView API.</summary>
    protected abstract void ReloadWebView();

    /// <summary>Navigate to a URL via the native WebView API.</summary>
    protected abstract void NavigateWebView(string url);

    /// <summary>
    /// Configures the BlazorWebViewHandler to capture the platform WebView reference.
    /// Called during service registration before the app starts.
    /// </summary>
    public abstract void ConfigureHandler();

    public void Initialize()
    {
        if (IsInitialized && HasWebView)
        {
            Log("[BlazorDevFlow] WebView already initialized, injecting now");
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await InjectDebugScriptAsync();
            });
        }
    }

    /// <summary>
    /// Called by platform subclasses after capturing the WebView reference.
    /// </summary>
    protected async Task OnWebViewCapturedAsync()
    {
        IsInitialized = true;

        Log("[BlazorDevFlow] Waiting 2s for page to load...");
        await Task.Delay(2000);

        Log("[BlazorDevFlow] Injecting debug script...");
        await InjectDebugScriptAsync();
    }

    private async Task InjectDebugScriptAsync()
    {
        if (_injecting) return;
        _injecting = true;

        try
        {
            await InjectDebugScriptCoreAsync();
        }
        finally
        {
            _injecting = false;
        }
    }

    private async Task InjectDebugScriptCoreAsync()
    {
        if (!HasWebView)
        {
            Log("[BlazorDevFlow] InjectDebugScript: WebView is null");
            return;
        }

        // Wait for chobitsu to be available (loaded via <script> tag in index.html)
        for (int i = 0; i < 30; i++)
        {
            var check = await EvaluateJavaScriptAsync(
                "typeof chobitsu !== 'undefined' ? 'loaded' : 'waiting'");
            if (i == 0 || check?.ToString() == "loaded")
                Log($"[BlazorDevFlow] Chobitsu check #{i}: {check}");
            if (check?.ToString() == "loaded") break;

            // After a few attempts, check the HTML for the script tag to give an early error
            if (i == 5)
            {
                var hasTag = await EvaluateJavaScriptAsync(
                    "document.querySelector('script[src*=\"chobitsu\"]') ? 'found' : 'missing'");
                if (hasTag?.ToString() == "missing")
                {
                    Log("[BlazorDevFlow] ❌ chobitsu.js not found. The JS initializer should inject it automatically.");
                    Log("[BlazorDevFlow] Verify the Redth.MauiDevFlow.Blazor package/project is referenced and the Blazor WebView has loaded.");
                    return;
                }
            }

            if (i == 29)
            {
                Log("[BlazorDevFlow] Chobitsu not loaded after 15s — the script tag exists but chobitsu.js may not be loading correctly.");
                return;
            }
            await Task.Delay(500);
        }

        var script = ChobitsuDebugScript.GetInjectionScript();
        Log($"[BlazorDevFlow] Injecting init script ({script.Length} chars)...");

        try
        {
            var result = await EvaluateJavaScriptAsync(script);
            Log($"[BlazorDevFlow] Script injection result: {result?.ToString() ?? "null"}");
            _chobitsuLoaded = true;

            // Inject console interceptor to capture WebView logs
            await InjectConsoleInterceptAsync();
            StartLogDrain();
        }
        catch (Exception ex)
        {
            LogError("[BlazorDevFlow] Failed to inject script", ex);
        }
    }

    /// <summary>
    /// Sends a CDP command to chobitsu and returns the response synchronously via a single JS eval.
    /// Handles special methods (Input.insertText, Page.reload, Page.navigate, Browser.*) natively.
    /// </summary>
    public async Task<string> SendCdpCommandAsync(string cdpJson)
    {
        if (!IsReady)
            return "{\"error\":\"WebView not ready\"}";

        try
        {
            var json = System.Text.Json.JsonDocument.Parse(cdpJson);
            var id = json.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
            var method = json.RootElement.TryGetProperty("method", out var methodProp) ? methodProp.GetString() ?? "" : "";

            // Handle methods that need native implementation
            if (method == "Input.insertText")
                return await HandleInputInsertTextAsync(cdpJson, id);
            if (method == "Page.reload")
                return await HandlePageReloadAsync(id);
            if (method == "Page.navigate")
                return await HandlePageNavigateAsync(cdpJson, id);
            if (method.StartsWith("Browser."))
                return HandleBrowserMethod(method, id);

            // Send to chobitsu and get response via two JS evals
            // (chobitsu fires onMessage asynchronously, not in the same JS turn)
            var escaped = cdpJson.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
            var sendScript = ScriptResources.Load("cdp-send-receive.js")
                .Replace("%CDP_MESSAGE%", escaped);
            var readScript = ScriptResources.Load("cdp-read-response.js");

            Log($"[BlazorDevFlow] SendCdpCommand: method={method}");

            // First eval: send the command and set up response capture
            var sendResult = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                return await EvaluateJavaScriptAsync(sendScript);
            });

            // Brief delay for chobitsu to process (it uses microtasks internally)
            await Task.Delay(50);

            // Second eval: read the captured response (with retries)
            string? result = null;
            for (int i = 0; i < 60; i++) // up to 3 seconds
            {
                result = await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    return await EvaluateJavaScriptAsync(readScript);
                });

                var unescaped = UnescapeEvalResult(result);
                if (unescaped != null)
                {
                    Log($"[BlazorDevFlow] SendCdpCommand got response after {i + 1} poll(s)");
                    return unescaped;
                }

                await Task.Delay(50);
            }

            Log($"[BlazorDevFlow] SendCdpCommand: no response after polling");
            return "{\"error\":\"cdp timeout\"}";
        }
        catch (Exception ex)
        {
            LogError("[BlazorDevFlow] SendCdpCommandAsync failed", ex);
            return $"{{\"error\":\"{EscapeJsonString(ex.Message)}\"}}";
        }
    }

    private string HandleBrowserMethod(string method, int id)
    {
        if (method == "Browser.getVersion")
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                id,
                result = new
                {
                    protocolVersion = "1.3",
                    product = "MAUI Blazor WebView/1.0",
                    userAgent = "MauiDevFlow",
                    jsVersion = ""
                }
            });

        return $"{{\"id\":{id},\"result\":{{}}}}";
    }

    private async Task<string> HandleInputInsertTextAsync(string cdpJson, int id)
    {
        var json = System.Text.Json.JsonDocument.Parse(cdpJson);
        var text = json.RootElement.GetProperty("params").GetProperty("text").GetString() ?? "";
        var escapedText = EscapeJsString(text);

        var script = ScriptResources.Load("insert-text.js")
            .Replace("%TEXT%", escapedText)
            .Replace("%TEXT_LENGTH%", text.Length.ToString());

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await EvaluateJavaScriptAsync(script);
        });

        return $"{{\"id\":{id},\"result\":{{}}}}";
    }

    private async Task<string> HandlePageReloadAsync(int id)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            ReloadWebView();
        });
        await Task.Delay(1500);
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await InjectDebugScriptAsync();
        });

        return $"{{\"id\":{id},\"result\":{{}}}}";
    }

    private async Task<string> HandlePageNavigateAsync(string cdpJson, int id)
    {
        var json = System.Text.Json.JsonDocument.Parse(cdpJson);
        var url = json.RootElement.GetProperty("params").GetProperty("url").GetString() ?? "";

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            NavigateWebView(url);
        });
        await Task.Delay(1500);
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await InjectDebugScriptAsync();
        });

        return $"{{\"id\":{id},\"result\":{{\"frameId\":\"main\"}}}}";
    }

    /// <summary>Unescape the JSON-encoded string returned by EvaluateJavaScriptAsync.</summary>
    private static string? UnescapeEvalResult(string? result)
    {
        if (string.IsNullOrEmpty(result)) return null;
        // WKWebView wraps string results in quotes and escapes inner quotes
        if (result.StartsWith("\"") && result.EndsWith("\""))
        {
            try { return System.Text.Json.JsonSerializer.Deserialize<string>(result); }
            catch { /* fall through */ }
        }
        return result;
    }

    private static string EscapeJsString(string s)
    {
        return s.Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
    }

    private static string EscapeJsonString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    protected void Log(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
        Console.WriteLine(message);
        LogCallback?.Invoke(message);
    }

    protected void LogError(string message, Exception? ex = null)
    {
        Log($"[ERROR] {message}");
        if (ex != null)
            Log($"  Exception: {ex.GetType().Name}: {ex.Message}");
    }

    private async Task InjectConsoleInterceptAsync()
    {
        try
        {
            var script = ScriptResources.Load("console-intercept.js");
            var result = await EvaluateJavaScriptAsync(script);
            Log($"[BlazorDevFlow] Console intercept: {result ?? "null"}");
        }
        catch (Exception ex)
        {
            LogError("[BlazorDevFlow] Failed to inject console interceptor", ex);
        }
    }

    private void StartLogDrain()
    {
        _drainCts?.Cancel();
        _drainCts = new CancellationTokenSource();
        var ct = _drainCts.Token;

        Task.Run(async () =>
        {
            var drainScript = ScriptResources.Load("drain-console-logs.js");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(2000, ct);
                    if (!IsReady || WebViewLogCallback == null) continue;

                    var raw = await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        return await EvaluateJavaScriptAsync(drainScript);
                    });

                    var json = UnescapeEvalResult(raw);
                    if (string.IsNullOrEmpty(json) || json == "null") continue;

                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    foreach (var entry in doc.RootElement.EnumerateArray())
                    {
                        var jsLevel = entry.GetProperty("l").GetString() ?? "log";
                        var message = entry.GetProperty("m").GetString() ?? "";
                        var exception = entry.TryGetProperty("e", out var eProp) ? eProp.GetString() : null;

                        // Map JS console levels to .NET LogLevel names
                        var level = jsLevel switch
                        {
                            "error" => "Error",
                            "warn" => "Warning",
                            "debug" => "Debug",
                            "info" => "Information",
                            _ => "Information" // console.log → Information
                        };

                        WebViewLogCallback(level, message, exception);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[BlazorDevFlow] Log drain error: {ex.Message}");
                }
            }
        }, ct);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _drainCts?.Cancel();
        _drainCts?.Dispose();
        Log("[BlazorDevFlow] Disposed");
    }
}
