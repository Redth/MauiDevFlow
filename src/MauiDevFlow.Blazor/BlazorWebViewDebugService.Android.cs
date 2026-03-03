#if ANDROID
using Android.Webkit;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Handlers;
using AWebView = Android.Webkit.WebView;

namespace MauiDevFlow.Blazor;

/// <summary>
/// Android implementation of the Blazor WebView debug service.
/// Uses Android.Webkit.WebView.EvaluateJavascript with a callback wrapper.
/// </summary>
public class BlazorWebViewDebugService : BlazorWebViewDebugServiceBase
{
    public BlazorWebViewDebugService() { }

    public override void ConfigureHandler()
    {
        Log("[BlazorDevFlow] ConfigureHandler called (Android)");

        BlazorWebViewHandler.BlazorWebViewMapper.AppendToMapping("ChobitsuDebug", async (handler, view) =>
        {
            Log("[BlazorDevFlow] ChobitsuDebug mapper callback triggered (Android)");

            if (handler.PlatformView is AWebView androidWebView)
            {
                androidWebView.Settings.JavaScriptEnabled = true;
                var automationId = (handler.VirtualView as VisualElement)?.AutomationId;
                var idx = AddWebViewBridge(
                    (script) =>
                    {
                        var tcs = new TaskCompletionSource<string?>();
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            try
                            {
                                androidWebView.EvaluateJavascript(script, new JsValueCallback(value =>
                                {
                                    if (value == "null" || value == null)
                                        tcs.TrySetResult(null);
                                    else
                                    {
                                        if (value.StartsWith("\"") && value.EndsWith("\""))
                                        {
                                            try { value = System.Text.Json.JsonSerializer.Deserialize<string>(value) ?? value; }
                                            catch { value = value[1..^1]; }
                                        }
                                        tcs.TrySetResult(value);
                                    }
                                }));
                            }
                            catch (Exception ex) { tcs.TrySetException(ex); }
                        });
                        return tcs.Task;
                    },
                    () => MainThread.BeginInvokeOnMainThread(() => androidWebView.Reload()),
                    (url) => MainThread.BeginInvokeOnMainThread(() => androidWebView.LoadUrl(url)),
                    automationId);
                Log($"[BlazorDevFlow] Android WebView captured as bridge {idx} (automationId={automationId})");
                await InitializeBridgeAsync(idx);
            }
            else
            {
                Log($"[BlazorDevFlow] PlatformView is not Android WebView: {handler.PlatformView?.GetType().Name ?? "null"}");
            }
        });
    }
}

/// <summary>
/// Wraps Android's IValueCallback for async JavaScript evaluation.
/// </summary>
internal class JsValueCallback : Java.Lang.Object, IValueCallback
{
    private readonly Action<string?> _callback;

    public JsValueCallback(Action<string?> callback)
    {
        _callback = callback;
    }

    public void OnReceiveValue(Java.Lang.Object? value)
    {
        _callback(value?.ToString());
    }
}
#endif
