#if WINDOWS
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Web.WebView2.Core;
using WinUIWebView = Microsoft.UI.Xaml.Controls.WebView2;

namespace MauiDevFlow.Blazor;

/// <summary>
/// Windows implementation of the Blazor WebView debug service.
/// Uses WebView2's CoreWebView2.ExecuteScriptAsync for JavaScript evaluation.
/// All WebView2 API calls are marshalled to the UI thread.
/// </summary>
public class BlazorWebViewDebugService : BlazorWebViewDebugServiceBase
{
    public BlazorWebViewDebugService() { }

    /// <summary>
    /// Decodes the JSON-encoded result from WebView2's ExecuteScriptAsync.
    /// </summary>
    private static string? DecodeWebView2Result(string? result)
    {
        if (string.IsNullOrEmpty(result) || result == "null")
            return null;

        try
        {
            using var doc = JsonDocument.Parse(result);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.String => doc.RootElement.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => doc.RootElement.GetRawText()
            };
        }
        catch
        {
            return result;
        }
    }

    public override void ConfigureHandler()
    {
        Log("[BlazorDevFlow] ConfigureHandler called (Windows)");

        BlazorWebViewHandler.BlazorWebViewMapper.AppendToMapping("ChobitsuDebug", async (handler, view) =>
        {
            Log("[BlazorDevFlow] ChobitsuDebug mapper callback triggered (Windows)");

            if (handler.PlatformView is WinUIWebView webView2)
            {
                var automationId = (handler.VirtualView as VisualElement)?.AutomationId;

                async Task CaptureWebView(CoreWebView2 coreWebView)
                {
                    var idx = AddWebViewBridge(
                        async (script) =>
                        {
                            var result = await MainThread.InvokeOnMainThreadAsync(async () =>
                            {
                                return await coreWebView.ExecuteScriptAsync(script);
                            });
                            return DecodeWebView2Result(result);
                        },
                        () => MainThread.BeginInvokeOnMainThread(() => coreWebView.Reload()),
                        (url) => MainThread.BeginInvokeOnMainThread(() => coreWebView.Navigate(url)),
                        automationId);
                    Log($"[BlazorDevFlow] WebView2 captured as bridge {idx} (automationId={automationId})");
                    await InitializeBridgeAsync(idx);
                }

                if (webView2.CoreWebView2 != null)
                {
                    await CaptureWebView(webView2.CoreWebView2);
                }
                else
                {
                    webView2.CoreWebView2Initialized += async (s, e) =>
                    {
                        if (e.Exception != null)
                        {
                            Log($"[BlazorDevFlow] CoreWebView2 initialization failed: {e.Exception.Message}");
                            return;
                        }

                        await CaptureWebView(webView2.CoreWebView2);
                    };
                }
            }
            else
            {
                Log($"[BlazorDevFlow] PlatformView is not WebView2: {handler.PlatformView?.GetType().Name ?? "null"}");
            }
        });
    }
}
#endif
