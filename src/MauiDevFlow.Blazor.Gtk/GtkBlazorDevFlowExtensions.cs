using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;

namespace MauiDevFlow.Blazor.Gtk;

/// <summary>
/// Extension methods for registering MauiDevFlow Blazor debug tools in Maui.Gtk apps.
/// </summary>
public static class GtkBlazorDevFlowExtensions
{
    /// <summary>
    /// Adds MauiDevFlow Blazor WebView debugging tools for WebKitGTK.
    /// Enables Chrome DevTools Protocol (CDP) access to BlazorWebView content on Linux.
    /// Automatically wires to the Agent and captures the WebView when a BlazorWebView is rendered.
    /// </summary>
    public static MauiAppBuilder AddMauiBlazorDevFlowTools(this MauiAppBuilder builder, bool enableLogging = false)
    {
        var service = new GtkBlazorWebViewDebugService();
        if (enableLogging)
            service.LogCallback = msg => System.Diagnostics.Debug.WriteLine(msg);

        builder.Services.AddSingleton(service);

        // Auto-wire to agent and capture WebView after app starts
        Task.Run(async () =>
        {
            // Wait for app to initialize
            await Task.Delay(2000);
            service.WireBlazorCdpToAgent();
            service.StartWebViewDiscovery();
        });

        return builder;
    }

    /// <summary>
    /// Wires the Blazor CDP service to the Agent's /api/cdp endpoint via reflection.
    /// Called automatically by AddMauiBlazorDevFlowTools after app initialization.
    /// </summary>
    public static void WireBlazorCdpToAgent(this GtkBlazorWebViewDebugService blazorService)
    {
        try
        {
            var app = Microsoft.Maui.Controls.Application.Current;
            if (app == null)
            {
                Console.WriteLine("[MauiDevFlow.Blazor.Gtk] Application.Current is null, cannot wire CDP");
                return;
            }

            var services = app.Handler?.MauiContext?.Services;
            if (services == null)
            {
                Console.WriteLine("[MauiDevFlow.Blazor.Gtk] No service provider available");
                return;
            }

            // Find DevFlowAgentService by scanning loaded assemblies
            Type? agentType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                agentType = asm.GetType("MauiDevFlow.Agent.Core.DevFlowAgentService");
                if (agentType != null) break;
            }

            if (agentType == null)
            {
                Console.WriteLine("[MauiDevFlow.Blazor.Gtk] Agent service type not found");
                return;
            }

            var agentService = services.GetService(agentType);
            if (agentService == null)
            {
                Console.WriteLine("[MauiDevFlow.Blazor.Gtk] Agent service not registered");
                return;
            }

            // Wire CdpCommandHandler and CdpReadyCheck — use RegisterCdpWebView if available
            var registerMethod = agentType.GetMethod("RegisterCdpWebView");

            if (registerMethod != null)
            {
                // Register existing bridges
                foreach (var bridge in blazorService.Bridges)
                {
                    var bridgeRef = bridge;
                    registerMethod.Invoke(agentService, new object?[]
                    {
                        new Func<string, Task<string>>(bridgeRef.SendCdpCommandAsync),
                        new Func<bool>(() => bridgeRef.IsReady),
                        bridgeRef.AutomationId,
                        bridgeRef.ElementId,
                        null // url
                    });
                }

                // Watch for new bridges
                var registeredCount = blazorService.Bridges.Count;
                _ = Task.Run(async () =>
                {
                    for (int i = 0; i < 600; i++)
                    {
                        await Task.Delay(1000);
                        while (registeredCount < blazorService.Bridges.Count)
                        {
                            var bridge = blazorService.Bridges[registeredCount];
                            try
                            {
                                registerMethod.Invoke(agentService, new object?[]
                                {
                                    new Func<string, Task<string>>(bridge.SendCdpCommandAsync),
                                    new Func<bool>(() => bridge.IsReady),
                                    bridge.AutomationId,
                                    bridge.ElementId,
                                    null
                                });
                                Console.WriteLine($"[MauiDevFlow.Blazor.Gtk] Registered CDP WebView bridge {registeredCount}");
                            }
                            catch { }
                            registeredCount++;
                        }
                    }
                });
            }
            else
            {
                // Fallback: legacy single-delegate wiring
                var handlerProp = agentType.GetProperty("CdpCommandHandler");
                var readyProp = agentType.GetProperty("CdpReadyCheck");

                if (handlerProp != null)
                    handlerProp.SetValue(agentService, new Func<string, Task<string>>(blazorService.SendCdpCommandAsync));

                if (readyProp != null)
                    readyProp.SetValue(agentService, new Func<bool>(() => blazorService.IsReady));
            }

            // Wire WebViewLogCallback
            var writeLogMethod = agentType.GetMethod("WriteWebViewLog");
            if (writeLogMethod != null)
            {
                blazorService.WebViewLogCallback = (level, message, exception) =>
                {
                    try { writeLogMethod.Invoke(agentService, new object?[] { level, "WebView.Console", message, exception }); }
                    catch { }
                };
            }

            Console.WriteLine("[MauiDevFlow.Blazor.Gtk] CDP wired to Agent");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MauiDevFlow.Blazor.Gtk] Wire failed: {ex.Message}");
        }
    }
}
