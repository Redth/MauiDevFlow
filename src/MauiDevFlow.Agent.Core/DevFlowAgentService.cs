using System.Text.Json;
using System.Reflection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Dispatching;
using MauiDevFlow.Logging;

namespace MauiDevFlow.Agent.Core;

/// <summary>
/// The main agent service that hosts the HTTP API and coordinates
/// visual tree inspection and element interactions.
/// </summary>
public class DevFlowAgentService : IDisposable
{
    private readonly AgentOptions _options;
    private readonly AgentHttpServer _server;
    private readonly VisualTreeWalker _treeWalker;
    private FileLogProvider? _logProvider;
    private BrokerRegistration? _brokerRegistration;
    protected Application? _app;
    protected IDispatcher? _dispatcher;
    private bool _disposed;

    /// <summary>
    /// Delegate for sending CDP commands to the Blazor WebView.
    /// Set by the Blazor package when both are registered.
    /// </summary>
    public Func<string, Task<string>>? CdpCommandHandler { get; set; }

    /// <summary>Whether the CDP handler is ready to process commands.</summary>
    public Func<bool>? CdpReadyCheck { get; set; }

    public bool IsRunning => _server.IsRunning;
    public int Port => _options.Port;

    public DevFlowAgentService(AgentOptions? options = null)
    {
        _options = options ?? new AgentOptions();
        _server = new AgentHttpServer(_options.Port);
        _treeWalker = CreateTreeWalker();
        RegisterRoutes();
    }

    /// <summary>
    /// Parses the optional "window" query parameter as a 0-based window index.
    /// Returns null when not specified (callers should default to first window).
    /// </summary>
    private static int? ParseWindowIndex(HttpRequest request)
    {
        if (request.QueryParams.TryGetValue("window", out var ws) && int.TryParse(ws, out var wi))
            return wi;
        return null;
    }

    /// <summary>
    /// Gets the window at the given index, or the first window when index is null.
    /// </summary>
    private Window? GetWindow(int? index)
    {
        if (_app == null) return null;
        if (index == null) return _app.Windows.FirstOrDefault() as Window;
        if (index.Value < 0 || index.Value >= _app.Windows.Count) return null;
        return _app.Windows[index.Value] as Window;
    }

    /// <summary>
    /// Creates the visual tree walker. Override in platform-specific subclasses
    /// to return a walker with native info population.
    /// </summary>
    protected virtual VisualTreeWalker CreateTreeWalker() => new VisualTreeWalker();

    /// <summary>Platform name for status reporting. Override for platforms without DeviceInfo.</summary>
    protected virtual string PlatformName => DeviceInfo.Current.Platform.ToString();

    /// <summary>Device type for status reporting. Override for platforms without DeviceInfo.</summary>
    protected virtual string DeviceTypeName => DeviceInfo.Current.DeviceType.ToString();

    /// <summary>Device idiom for status reporting. Override for platforms without DeviceInfo.</summary>
    protected virtual string IdiomName => DeviceInfo.Current.Idiom.ToString();

    /// <summary>Gets native window dimensions when MAUI reports 0. Override for platform-specific access.</summary>
    protected virtual (double width, double height) GetNativeWindowSize(IWindow window) => (0, 0);

    /// <summary>
    /// Sets the file log provider for serving logs via the API.
    /// Called by AgentServiceExtensions during registration.
    /// </summary>
    public void SetLogProvider(FileLogProvider provider)
        => _logProvider = provider;

    public void SetBrokerRegistration(BrokerRegistration registration)
        => _brokerRegistration = registration;

    /// <summary>
    /// Writes a log entry originating from the WebView/Blazor console.
    /// Called by the Blazor package via reflection to route JS console output through ILogger.
    /// </summary>
    public void WriteWebViewLog(string level, string category, string message, string? exception = null)
    {
        if (_logProvider == null) return;

        var entry = new Logging.FileLogEntry(
            Timestamp: DateTime.UtcNow,
            Level: level,
            Category: category,
            Message: message,
            Exception: exception,
            Source: "webview"
        );
        _logProvider.Writer.Write(entry);
    }

    /// <summary>
    /// Starts the agent and binds to the running MAUI app.
    /// </summary>
    public void Start(Application app, IDispatcher dispatcher)
    {
        if (!_options.Enabled) return;
        _app = app;
        _dispatcher = dispatcher;
        try
        {
            _server.Start();
            Console.WriteLine($"[MauiDevFlow.Agent] HTTP server started on port {_options.Port}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MauiDevFlow.Agent] Failed to start HTTP server: {ex.Message}");
        }
    }

    public async Task StopAsync()
    {
        await _server.StopAsync();
    }

    private void RegisterRoutes()
    {
        _server.MapGet("/api/status", HandleStatus);
        _server.MapGet("/api/tree", HandleTree);
        _server.MapGet("/api/element/{id}", HandleElement);
        _server.MapGet("/api/query", HandleQuery);
        _server.MapGet("/api/screenshot", HandleScreenshot);
        _server.MapGet("/api/property/{id}/{name}", HandleProperty);
        _server.MapGet("/api/properties/{id}", HandleProperties);
        _server.MapPost("/api/property/{id}/{name}", HandleSetProperty);
        _server.MapPost("/api/action/tap", HandleTap);
        _server.MapPost("/api/action/fill", HandleFill);
        _server.MapPost("/api/action/clear", HandleClear);
        _server.MapPost("/api/action/focus", HandleFocus);
        _server.MapPost("/api/action/navigate", HandleNavigate);
        _server.MapPost("/api/action/resize", HandleResize);
        _server.MapPost("/api/action/scroll", HandleScroll);
        _server.MapGet("/api/logs", HandleLogs);
        _server.MapPost("/api/cdp", HandleCdp);
    }

    private async Task<HttpResponse> HandleStatus(HttpRequest request)
    {
        var windowIndex = ParseWindowIndex(request);
        var result = await DispatchAsync(() =>
        {
            var window = GetWindow(windowIndex);
            var w = window?.Width ?? 0;
            var h = window?.Height ?? 0;

            // Try getting window size from native platform view if MAUI reports invalid values
            if (window != null && (!double.IsFinite(w) || !double.IsFinite(h) || w <= 0 || h <= 0))
            {
                var (nw, nh) = GetNativeWindowSize(window);
                if (nw > 0) w = nw;
                if (nh > 0) h = nh;
            }

            return new
            {
                agent = "MauiDevFlow.Agent",
                version = "1.0.0",
                platform = PlatformName,
                deviceType = DeviceTypeName,
                idiom = IdiomName,
                appName = _app?.GetType().Assembly.GetName().Name ?? "unknown",
                running = _app != null,
                cdpReady = CdpReadyCheck?.Invoke() ?? false,
                windowCount = _app?.Windows.Count ?? 0,
                windowWidth = double.IsFinite(w) ? w : 0,
                windowHeight = double.IsFinite(h) ? h : 0
            };
        });

        return HttpResponse.Json(result!);
    }

    private async Task<HttpResponse> HandleTree(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        int maxDepth = 0;
        if (request.QueryParams.TryGetValue("depth", out var depthStr))
            int.TryParse(depthStr, out maxDepth);

        var windowIndex = ParseWindowIndex(request);
        var tree = await DispatchAsync(() => _treeWalker.WalkTree(_app, maxDepth, windowIndex));
        return HttpResponse.Json(tree);
    }

    private async Task<HttpResponse> HandleElement(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");
        if (!request.RouteParams.TryGetValue("id", out var id))
            return HttpResponse.Error("Element ID required");

        var element = await DispatchAsync(() =>
        {
            var el = _treeWalker.GetElementById(id, _app);
            if (el is not IVisualTreeElement vte) return null;
            return _treeWalker.WalkElement(vte, null, 1, 2);
        });

        return element != null ? HttpResponse.Json(element) : HttpResponse.NotFound($"Element '{id}' not found");
    }

    private async Task<HttpResponse> HandleQuery(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        // CSS selector takes precedence over simple filters
        if (request.QueryParams.TryGetValue("selector", out var selector) && !string.IsNullOrWhiteSpace(selector))
        {
            try
            {
                var results = await DispatchAsync(() => _treeWalker.QueryCss(_app, selector));
                return HttpResponse.Json(results);
            }
            catch (FormatException ex)
            {
                return HttpResponse.Error($"Invalid CSS selector: {ex.Message}");
            }
        }

        request.QueryParams.TryGetValue("type", out var type);
        request.QueryParams.TryGetValue("automationId", out var automationId);
        request.QueryParams.TryGetValue("text", out var text);

        if (type == null && automationId == null && text == null)
            return HttpResponse.Error("At least one query parameter required: type, automationId, text, or selector");

        var simpleResults = await DispatchAsync(() => _treeWalker.Query(_app, type, automationId, text));
        return HttpResponse.Json(simpleResults);
    }

    protected virtual async Task<HttpResponse> HandleScreenshot(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        // Check for fullscreen mode (captures all windows including dialogs)
        if (request.QueryParams.TryGetValue("fullscreen", out var fs) &&
            fs.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var pngData = await CaptureFullScreenAsync();
                if (pngData != null)
                    return HttpResponse.Png(pngData);
                return HttpResponse.Error("Full-screen capture not supported on this platform");
            }
            catch (Exception ex)
            {
                return HttpResponse.Error($"Full-screen screenshot failed: {ex.Message}");
            }
        }

        // Element-level screenshot by ID
        if (request.QueryParams.TryGetValue("id", out var elementId) && !string.IsNullOrWhiteSpace(elementId))
        {
            try
            {
                var element = await DispatchAsync(() =>
                {
                    var el = _treeWalker.GetElementById(elementId, _app);
                    return el as VisualElement;
                });

                if (element == null)
                    return HttpResponse.Error($"Element '{elementId}' not found or not a VisualElement");

                var pngData = await DispatchAsync(() => CaptureElementScreenshotAsync(element));
                if (pngData == null)
                    return HttpResponse.Error($"Capture returned null for element '{elementId}'");

                return HttpResponse.Png(pngData);
            }
            catch (Exception ex)
            {
                return HttpResponse.Error($"Element screenshot failed: {ex.Message}");
            }
        }

        // Element-level screenshot by CSS selector (captures first match)
        if (request.QueryParams.TryGetValue("selector", out var selector) && !string.IsNullOrWhiteSpace(selector))
        {
            try
            {
                var matchId = await DispatchAsync(() =>
                {
                    var results = _treeWalker.QueryCss(_app, selector);
                    return results.Count > 0 ? results[0].Id : null;
                });

                if (matchId == null)
                    return HttpResponse.Error($"No elements matching selector '{selector}'");

                var element = await DispatchAsync(() =>
                {
                    var el = _treeWalker.GetElementById(matchId);
                    return el as VisualElement;
                });

                if (element == null)
                    return HttpResponse.Error($"Element '{matchId}' not found or not a VisualElement");

                var pngData = await DispatchAsync(() => CaptureElementScreenshotAsync(element));
                if (pngData == null)
                    return HttpResponse.Error($"Capture returned null for element '{matchId}'");

                return HttpResponse.Png(pngData);
            }
            catch (FormatException ex)
            {
                return HttpResponse.Error($"Invalid CSS selector: {ex.Message}");
            }
            catch (Exception ex)
            {
                return HttpResponse.Error($"Element screenshot failed: {ex.Message}");
            }
        }

        try
        {
            var windowIndex = ParseWindowIndex(request);
            var pngData = await DispatchAsync(async () =>
            {
                var window = GetWindow(windowIndex);
                if (window == null) return null;

                // If a modal page is displayed, capture it instead of the underlying page
                VisualElement? topModal = null;
                try
                {
                    var modalStack = window.Page?.Navigation?.ModalStack;
                    if (modalStack?.Count > 0 && modalStack[^1] is VisualElement ms)
                        topModal = ms;
                }
                catch { }

                // Fallback: check Window's visual children for modal pages
                // (on some platforms like GTK, modals appear as direct children of the Window)
                if (topModal == null && window is IVisualTreeElement windowVte)
                {
                    var children = windowVte.GetVisualChildren();
                    for (int i = children.Count - 1; i >= 0; i--)
                    {
                        if (children[i] is Page page && page != window.Page)
                        {
                            topModal = page;
                            break;
                        }
                    }
                }

                if (topModal != null)
                    return await CaptureScreenshotAsync(topModal);

                if (window.Page is not VisualElement rootElement) return null;

                return await CaptureScreenshotAsync(rootElement);
            });

            if (pngData == null)
                return HttpResponse.Error("Failed to capture screenshot");

            return HttpResponse.Png(pngData);
        }
        catch (Exception ex)
        {
            return HttpResponse.Error($"Screenshot failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Captures a screenshot of the given root element. Override in platform-specific subclasses.
    /// </summary>
    protected virtual async Task<byte[]?> CaptureScreenshotAsync(VisualElement rootElement)
    {
        return await VisualDiagnostics.CaptureAsPngAsync(rootElement);
    }

    /// <summary>
    /// Captures a screenshot of a specific element in the visual tree.
    /// Override in platform-specific subclasses when VisualDiagnostics.CaptureAsPngAsync
    /// is not supported (e.g. macOS AppKit).
    /// </summary>
    protected virtual async Task<byte[]?> CaptureElementScreenshotAsync(VisualElement element)
    {
        return await VisualDiagnostics.CaptureAsPngAsync(element);
    }

    /// <summary>
    /// Captures a full-screen screenshot including all windows (dialogs, popups, etc.).
    /// Override in platform-specific subclasses for native support.
    /// Returns null if not supported.
    /// </summary>
    protected virtual Task<byte[]?> CaptureFullScreenAsync()
    {
        return Task.FromResult<byte[]?>(null);
    }

    private async Task<HttpResponse> HandleProperty(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");
        if (!request.RouteParams.TryGetValue("id", out var id))
            return HttpResponse.Error("Element ID required");
        if (!request.RouteParams.TryGetValue("name", out var propName))
            return HttpResponse.Error("Property name required");

        var value = await DispatchAsync(() =>
        {
            var el = _treeWalker.GetElementById(id, _app);
            if (el == null) return (object?)null;

            // Support dot-path notation (e.g., "Shadow.Radius")
            var parts = propName.Split('.');
            object? current = el;
            PropertyInfo? prop = null;
            foreach (var part in parts)
            {
                if (current == null) return null;
                var type = current.GetType();
                prop = type.GetProperty(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop == null) return null;
                current = prop.GetValue(current);
            }
            return FormatPropertyValue(current);
        });

        return value != null
            ? HttpResponse.Json(new { id, property = propName, value })
            : HttpResponse.NotFound($"Property '{propName}' not found on element '{id}'");
    }

    private async Task<HttpResponse> HandleProperties(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");
        if (!request.RouteParams.TryGetValue("id", out var id))
            return HttpResponse.Error("Element ID required");

        request.QueryParams.TryGetValue("category", out var categoryFilter);
        request.QueryParams.TryGetValue("interface", out var interfaceFilter);

        var result = await DispatchAsync(() =>
        {
            var el = _treeWalker.GetElementById(id, _app);
            if (el == null) return (PropertiesResponse?)null;

            var elType = el.GetType();
            var response = new PropertiesResponse
            {
                Id = id,
                Type = elType.Name,
                FullType = elType.FullName ?? elType.Name,
            };

            // Collect interface names (filtered to MAUI/Microsoft namespaces for relevance)
            var interfaces = elType.GetInterfaces()
                .Where(i => i.Namespace?.StartsWith("Microsoft.Maui") == true)
                .ToList();
            response.Interfaces = interfaces.Select(i => i.Name).Distinct().OrderBy(n => n).ToList();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var properties = new List<PropertyDetail>();

            // 1. BindableProperties — discover via static fields on type hierarchy
            if (categoryFilter == null || categoryFilter.Equals("bindable", StringComparison.OrdinalIgnoreCase))
            {
                var bindableObj = el as BindableObject;
                if (bindableObj != null)
                {
                    var bpFields = GetBindablePropertyFields(elType);
                    foreach (var (fieldName, bp) in bpFields)
                    {
                        if (!seen.Add(bp.PropertyName)) continue;

                        var clrProp = elType.GetProperty(bp.PropertyName, BindingFlags.Public | BindingFlags.Instance);
                        bool isReadOnly = bp.IsReadOnly || clrProp?.CanWrite != true;

                        string? formattedValue;
                        try { formattedValue = FormatPropertyValue(bindableObj.GetValue(bp)); }
                        catch (Exception ex) { formattedValue = $"<error: {ex.Message}>"; }

                        properties.Add(new PropertyDetail
                        {
                            Name = bp.PropertyName,
                            Value = formattedValue,
                            TypeName = bp.ReturnType?.Name ?? "Unknown",
                            Category = "bindable",
                            IsReadOnly = isReadOnly,
                        });
                    }
                }
            }

            // 2. Interface properties
            if (categoryFilter == null || categoryFilter.Equals("interface", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var iface in interfaces)
                {
                    if (interfaceFilter != null &&
                        !iface.Name.Equals(interfaceFilter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    foreach (var iProp in iface.GetProperties())
                    {
                        if (!seen.Add(iProp.Name)) continue;

                        string? formattedValue;
                        try
                        {
                            var concreteProp = elType.GetProperty(iProp.Name, BindingFlags.Public | BindingFlags.Instance)
                                ?? elType.GetProperty(iProp.Name, BindingFlags.NonPublic | BindingFlags.Instance);
                            var val = concreteProp?.GetValue(el);
                            formattedValue = FormatPropertyValue(val);
                        }
                        catch (Exception ex) { formattedValue = $"<error: {ex.Message}>"; }

                        properties.Add(new PropertyDetail
                        {
                            Name = iProp.Name,
                            Value = formattedValue,
                            TypeName = iProp.PropertyType.Name,
                            Category = "interface",
                            InterfaceName = iface.Name,
                            IsReadOnly = !iProp.CanWrite,
                        });
                    }
                }
            }

            // 3. Remaining CLR properties
            if (categoryFilter == null || categoryFilter.Equals("clr", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var prop in elType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!seen.Add(prop.Name)) continue;
                    // Skip indexers
                    if (prop.GetIndexParameters().Length > 0) continue;

                    string? formattedValue;
                    try { formattedValue = FormatPropertyValue(prop.GetValue(el)); }
                    catch (Exception ex) { formattedValue = $"<error: {ex.Message}>"; }

                    properties.Add(new PropertyDetail
                    {
                        Name = prop.Name,
                        Value = formattedValue,
                        TypeName = prop.PropertyType.Name,
                        Category = "clr",
                        IsReadOnly = !prop.CanWrite,
                    });
                }
            }

            response.Properties = properties;
            return response;
        });

        return result != null
            ? HttpResponse.Json(result)
            : HttpResponse.NotFound($"Element '{id}' not found");
    }

    private static List<(string FieldName, BindableProperty Property)> GetBindablePropertyFields(Type type)
    {
        var results = new List<(string, BindableProperty)>();
        var seen = new HashSet<string>();

        // Walk the type hierarchy to collect all BindableProperty fields
        var current = type;
        while (current != null && current != typeof(object))
        {
            foreach (var field in current.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (field.FieldType == typeof(BindableProperty) || field.FieldType == typeof(BindablePropertyKey))
                {
                    var bp = field.GetValue(null) as BindableProperty;
                    if (bp == null && field.GetValue(null) is BindablePropertyKey bpk)
                        bp = bpk.BindableProperty;
                    if (bp != null && seen.Add(bp.PropertyName))
                        results.Add((field.Name, bp));
                }
            }
            current = current.BaseType;
        }

        return results.OrderBy(r => r.Item2.PropertyName).ToList();
    }

    private static string? FormatPropertyValue(object? value)
    {
        if (value == null) return null;
        if (value is string s) return s;

        // Try TypeConverter first — handles Thickness, CornerRadius, Color, enums, etc.
        var converter = System.ComponentModel.TypeDescriptor.GetConverter(value.GetType());
        if (converter.CanConvertTo(typeof(string))
            && converter.GetType() != typeof(System.ComponentModel.TypeConverter)
            && converter is not System.ComponentModel.CollectionConverter)
        {
            try
            {
                var result = converter.ConvertToString(value);
                if (result != null) return result;
            }
            catch { }
        }

        // Fallback for complex types that lack TypeConverter ConvertTo support
        return value switch
        {
            Shadow shadow => FormatShadow(shadow),
            SolidColorBrush scb => $"SolidColorBrush Color={scb.Color?.ToArgbHex() ?? "(null)"}",
            LinearGradientBrush lgb => $"LinearGradientBrush StartPoint={lgb.StartPoint}, EndPoint={lgb.EndPoint}, Stops={lgb.GradientStops?.Count ?? 0}",
            RadialGradientBrush rgb => $"RadialGradientBrush Center={rgb.Center}, Radius={rgb.Radius}, Stops={rgb.GradientStops?.Count ?? 0}",
            Brush brush => brush.GetType().Name,
            Microsoft.Maui.Controls.Shapes.RoundRectangle rr => $"RoundRectangle CornerRadius={FormatPropertyValue(rr.CornerRadius)}",
            Microsoft.Maui.Controls.Shapes.Shape shape => shape.GetType().Name,
            ColumnDefinitionCollection cols => string.Join(", ", cols.Select(c => FormatGridLength(c.Width))),
            RowDefinitionCollection rows => string.Join(", ", rows.Select(r => FormatGridLength(r.Height))),
            FileImageSource fis => $"File: {fis.File}",
            UriImageSource uis => $"Uri: {uis.Uri}",
            FontImageSource fontIs => $"Font: {fontIs.Glyph} ({fontIs.FontFamily})",
            ImageSource img => img.GetType().Name,
            System.Collections.ICollection col => $"{col.GetType().Name} ({col.Count} items)",
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? value.GetType().Name,
        };
    }

    private static string FormatGridLength(GridLength gl) => gl.IsStar
        ? (gl.Value == 1 ? "*" : $"{gl.Value}*")
        : gl.IsAbsolute ? gl.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "Auto";

    private static string FormatShadow(Shadow shadow)
    {
        var parts = new List<string>();
        if (shadow.Brush is SolidColorBrush scb)
            parts.Add($"Brush={scb.Color?.ToArgbHex()}");
        else if (shadow.Brush != null)
            parts.Add($"Brush={shadow.Brush.GetType().Name}");
        parts.Add($"Offset=({shadow.Offset.X},{shadow.Offset.Y})");
        parts.Add($"Radius={shadow.Radius}");
        parts.Add($"Opacity={shadow.Opacity}");
        return string.Join(", ", parts);
    }

    private async Task<HttpResponse> HandleSetProperty(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");
        if (!request.RouteParams.TryGetValue("id", out var id))
            return HttpResponse.Error("Element ID required");
        if (!request.RouteParams.TryGetValue("name", out var propName))
            return HttpResponse.Error("Property name required");

        var body = request.BodyAs<SetPropertyRequest>();
        if (body?.Value == null)
            return HttpResponse.Error("value is required");

        var result = await DispatchAsync(() =>
        {
            var el = _treeWalker.GetElementById(id, _app);
            if (el == null) return "Element not found";

            var type = el.GetType();
            var prop = type.GetProperty(propName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (prop == null || !prop.CanWrite)
                return $"Property '{propName}' not found or read-only";

            try
            {
                var converted = ConvertPropertyValue(prop.PropertyType, body.Value);
                prop.SetValue(el, converted);
                return "ok";
            }
            catch (Exception ex)
            {
                return $"Failed to set property: {ex.Message}";
            }
        });

        return result == "ok"
            ? HttpResponse.Json(new { id, property = propName, value = body.Value })
            : HttpResponse.Error(result);
    }

    private static object? ConvertPropertyValue(Type targetType, string value)
    {
        // Handle nullable types
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlying == typeof(string)) return value;
        if (underlying == typeof(bool)) return bool.Parse(value);
        if (underlying == typeof(int)) return int.Parse(value);
        if (underlying == typeof(double)) return double.Parse(value);
        if (underlying == typeof(float)) return float.Parse(value);

        // MAUI Color - supports named colors and hex
        if (underlying == typeof(Microsoft.Maui.Graphics.Color))
        {
            // Try hex format (#RRGGBB or #AARRGGBB)
            if (value.StartsWith('#'))
                return Microsoft.Maui.Graphics.Color.FromArgb(value);

            // Try named colors via reflection on Colors class (check both properties and fields)
            var colorsType = typeof(Microsoft.Maui.Graphics.Colors);
            var colorProp = colorsType.GetProperty(value,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.IgnoreCase);
            if (colorProp != null)
                return colorProp.GetValue(null);

            var colorField = colorsType.GetField(value,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.IgnoreCase);
            if (colorField != null)
                return colorField.GetValue(null);

            // Try Color.FromArgb as last resort (for rgb hex without #)
            try { return Microsoft.Maui.Graphics.Color.FromArgb($"#{value}"); }
            catch { }

            throw new ArgumentException($"Unknown color: '{value}'. Use hex (#FF6347) or a named color (Red, Blue, Green, etc.).");
        }

        // MAUI Thickness (uniform or "left,top,right,bottom")
        if (underlying == typeof(Microsoft.Maui.Thickness))
        {
            var parts = value.Split(',');
            return parts.Length switch
            {
                1 => new Microsoft.Maui.Thickness(double.Parse(parts[0])),
                2 => new Microsoft.Maui.Thickness(double.Parse(parts[0]), double.Parse(parts[1])),
                4 => new Microsoft.Maui.Thickness(double.Parse(parts[0]), double.Parse(parts[1]),
                    double.Parse(parts[2]), double.Parse(parts[3])),
                _ => throw new ArgumentException($"Invalid Thickness format: {value}")
            };
        }

        // Enum types
        if (underlying.IsEnum)
            return Enum.Parse(underlying, value, ignoreCase: true);

        // Fallback: TypeConverter
        var converter = System.ComponentModel.TypeDescriptor.GetConverter(underlying);
        if (converter.CanConvertFrom(typeof(string)))
            return converter.ConvertFromString(value);

        throw new ArgumentException($"Cannot convert '{value}' to {targetType.Name}");
    }

    private async Task<HttpResponse> HandleTap(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<ActionRequest>();
        if (body?.ElementId == null)
            return HttpResponse.Error("elementId is required");

        var result = await DispatchAsync(() =>
        {
            var el = _treeWalker.GetElementById(body.ElementId, _app);
            if (el == null) return "Element not found";

            switch (el)
            {
                case Button btn:
                    try { btn.SendClicked(); }
                    catch { if (btn is VisualElement ve && !TryNativeTap(ve)) return $"Native tap failed on Button"; }
                    return "ok";
                case ImageButton imgBtn:
                    try { imgBtn.SendClicked(); }
                    catch { if (imgBtn is VisualElement ve && !TryNativeTap(ve)) return $"Native tap failed on ImageButton"; }
                    return "ok";
                case CheckBox cb:
                    cb.IsChecked = !cb.IsChecked;
                    return "ok";
                case Switch sw:
                    sw.IsToggled = !sw.IsToggled;
                    return "ok";
                case RadioButton rb:
                    rb.IsChecked = true;
                    return "ok";
                case ToolbarItem ti:
                    ((IMenuItemController)ti).Activate();
                    return "ok";
                case VisualTreeWalker.BackButtonMarker back:
                    back.Navigation.PopAsync();
                    return "ok";
                case MenuItem mi:
                    ((IMenuItemController)mi).Activate();
                    return "ok";
                case Picker picker:
                    picker.Focus();
                    return "ok";
                case DatePicker datePicker:
                    datePicker.Focus();
                    return "ok";
                case TimePicker timePicker:
                    timePicker.Focus();
                    return "ok";
                case Page page when page.Parent is TabbedPage tabbed:
                    tabbed.CurrentPage = page;
                    return "ok";
                case ShellContent sc:
                    if (Shell.Current != null)
                    {
                        sc.IsVisible = true;
                        Shell.Current.CurrentItem = sc.Parent as ShellSection ?? Shell.Current.CurrentItem;
                    }
                    return "ok";
                case ShellSection ss:
                    if (Shell.Current != null)
                        Shell.Current.CurrentItem = ss;
                    return "ok";
                case IView view when view is View v:
                    // Try TapGestureRecognizer: Command first, then Tapped event via reflection
                    var tapGesture = v.GestureRecognizers.OfType<TapGestureRecognizer>().FirstOrDefault();
                    if (tapGesture != null)
                    {
                        if (tapGesture.Command != null)
                        {
                            tapGesture.Command.Execute(tapGesture.CommandParameter);
                            return "ok";
                        }
                        // Fire the Tapped event via reflection (SendTapped is internal)
                        if (TryInvokeTapped(tapGesture, v))
                            return "ok";
                        return $"TapGestureRecognizer found but SendTapped reflection failed on {el.GetType().FullName}";
                    }

                    // Native platform fallback for UIControl/Android.Views.View
                    if (v is VisualElement nativeVe && TryNativeTap(nativeVe))
                        return "ok";

                    return $"No tap handler on {el.GetType().FullName} (gestures:{v.GestureRecognizers.Count}, type:{v.GetType().Name})";
                default:
                    return $"Unhandled type: {el.GetType().FullName}";
            }
        });

        return result == "ok" ? HttpResponse.Ok("Tapped") : HttpResponse.Error(result);
    }

    /// <summary>
    /// Invokes the Tapped event on a TapGestureRecognizer via reflection.
    /// Calls internal SendTapped(View sender, Func&lt;IElement?, Point?&gt;? getPosition) method.
    /// </summary>
    private static bool TryInvokeTapped(TapGestureRecognizer tapGesture, View sender)
    {
        try
        {
            // SendTapped is internal on TapGestureRecognizer itself
            var sendTapped = typeof(TapGestureRecognizer).GetMethod("SendTapped",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (sendTapped != null)
            {
                var paramCount = sendTapped.GetParameters().Length;
                var args = paramCount switch
                {
                    0 => Array.Empty<object>(),
                    1 => new object[] { sender },
                    _ => new object?[] { sender, null }
                };
                sendTapped.Invoke(tapGesture, args);
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MauiDevFlow] TryInvokeTapped failed: {ex.GetBaseException().Message}");
        }
        return false;
    }

    /// <summary>
    /// Attempts to tap a native platform view as a fallback.
    /// Override in platform-specific subclasses for native tap support.
    /// </summary>
    protected virtual bool TryNativeTap(VisualElement ve)
    {
        return false;
    }

    private async Task<HttpResponse> HandleFill(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<FillRequest>();
        if (body?.ElementId == null || body.Text == null)
            return HttpResponse.Error("elementId and text are required");

        var result = await DispatchAsync(() =>
        {
            var el = _treeWalker.GetElementById(body.ElementId, _app);
            if (el == null) return "Element not found";

            switch (el)
            {
                case Entry entry:
                    entry.Text = body.Text;
                    entry.Unfocus();
                    return "ok";
                case Editor editor:
                    editor.Text = body.Text;
                    editor.Unfocus();
                    return "ok";
                case SearchBar searchBar:
                    searchBar.Text = body.Text;
                    searchBar.Unfocus();
                    return "ok";
                default:
                    return $"Unhandled type: {el.GetType().FullName}";
            }
        });

        return result == "ok" ? HttpResponse.Ok("Text set") : HttpResponse.Error(result);
    }

    private async Task<HttpResponse> HandleClear(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<ActionRequest>();
        if (body?.ElementId == null)
            return HttpResponse.Error("elementId is required");

        var success = await DispatchAsync(() =>
        {
            var el = _treeWalker.GetElementById(body.ElementId, _app);
            if (el == null) return false;

            switch (el)
            {
                case Entry entry:
                    entry.Text = string.Empty;
                    return true;
                case Editor editor:
                    editor.Text = string.Empty;
                    return true;
                case SearchBar searchBar:
                    searchBar.Text = string.Empty;
                    return true;
                default:
                    return false;
            }
        });

        return success ? HttpResponse.Ok("Cleared") : HttpResponse.Error("Element does not accept text input");
    }

    private async Task<HttpResponse> HandleFocus(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<ActionRequest>();
        if (body?.ElementId == null)
            return HttpResponse.Error("elementId is required");

        var success = await DispatchAsync(() =>
        {
            var el = _treeWalker.GetElementById(body.ElementId, _app);
            if (el is not VisualElement ve) return false;
            ve.Focus();
            return true;
        });

        return success ? HttpResponse.Ok("Focused") : HttpResponse.Error("Cannot focus element");
    }

    private async Task<HttpResponse> HandleNavigate(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<NavigateRequest>();
        if (string.IsNullOrEmpty(body?.Route))
            return HttpResponse.Error("route is required");

        var result = await DispatchAsync(async () =>
        {
            try
            {
                if (Shell.Current != null)
                {
                    await Shell.Current.GoToAsync(body.Route);
                    return "ok";
                }
                return "No Shell.Current available";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        });

        return result == "ok" ? HttpResponse.Ok($"Navigated to {body.Route}") : HttpResponse.Error(result ?? "Navigation failed");
    }

    private async Task<HttpResponse> HandleResize(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<ResizeRequest>();
        if (body == null || body.Width <= 0 || body.Height <= 0)
            return HttpResponse.Error("width and height are required (positive integers)");

        var windowIndex = ParseWindowIndex(request);
        var result = await DispatchAsync(() =>
        {
            var window = GetWindow(windowIndex);
            if (window?.Handler?.PlatformView == null)
                return "No window available";

            try
            {
                // Use platform-specific resize
                TryNativeResize(window, body.Width, body.Height);
                return "ok";
            }
            catch (Exception ex)
            {
                return $"Resize failed: {ex.Message}";
            }
        });

        return result == "ok"
            ? HttpResponse.Json(new { success = true, width = body.Width, height = body.Height })
            : HttpResponse.Error(result);
    }

    /// <summary>
    /// Platform-specific window resize. Override in platform agents for native support.
    /// </summary>
    protected virtual void TryNativeResize(IWindow window, int width, int height)
    {
        // Default: try casting to MAUI Window which has settable Width/Height
        if (window is Window mauiWindow)
        {
            mauiWindow.Width = width;
            mauiWindow.Height = height;
        }
    }

    private record ResizeRequest(int Width, int Height);

    private async Task<HttpResponse> HandleScroll(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<ScrollRequest>();
        if (body == null)
            return HttpResponse.Error("Request body is required");

        var result = await DispatchAsync(async () =>
        {
            // If elementId is given, find the nearest ScrollView ancestor and scroll to that element
            if (!string.IsNullOrEmpty(body.ElementId))
            {
                var el = _treeWalker.GetElementById(body.ElementId, _app);
                if (el == null) return "Element not found";

                if (el is VisualElement ve)
                {
                    // Find the nearest ScrollView ancestor
                    var scrollView = FindAncestor<ScrollView>(ve);
                    if (scrollView != null)
                    {
                        await ScrollWithTimeoutAsync(
                            () => scrollView.ScrollToAsync(ve, ScrollToPosition.MakeVisible, body.Animated),
                            () => scrollView.ScrollToAsync(ve, ScrollToPosition.MakeVisible, false));
                        return "ok";
                    }

                    // Maybe the element itself is a ScrollView
                    if (el is ScrollView sv)
                    {
                        await ScrollWithTimeoutAsync(
                            () => sv.ScrollToAsync(body.DeltaX, body.DeltaY, body.Animated),
                            () => sv.ScrollToAsync(body.DeltaX, body.DeltaY, false));
                        return "ok";
                    }
                }

                return $"No ScrollView ancestor found for element '{body.ElementId}'";
            }

            // Otherwise scroll by delta on the first ScrollView we find
            var window = GetWindow(ParseWindowIndex(request));
            if (window?.Page == null) return "No page available";

            var targetScroll = FindDescendant<ScrollView>(window.Page);
            if (targetScroll == null) return "No ScrollView found on page";

            var newX = targetScroll.ScrollX + body.DeltaX;
            var newY = targetScroll.ScrollY + body.DeltaY;
            var x = Math.Max(0, newX);
            var y = Math.Max(0, newY);
            await ScrollWithTimeoutAsync(
                () => targetScroll.ScrollToAsync(x, y, body.Animated),
                () => targetScroll.ScrollToAsync(x, y, false));
            return "ok";
        });

        return result == "ok" ? HttpResponse.Ok("Scrolled") : HttpResponse.Error(result ?? "Scroll failed");
    }

    /// <summary>
    /// Animated ScrollToAsync can deadlock on iOS when dispatched.
    /// Fall back to non-animated scroll if the animated version doesn't complete in time.
    /// </summary>
    private static async Task ScrollWithTimeoutAsync(Func<Task> animatedScroll, Func<Task> fallbackScroll)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var scrollTask = animatedScroll();
        var completed = await Task.WhenAny(scrollTask, Task.Delay(3000, cts.Token));
        if (completed == scrollTask)
        {
            cts.Cancel();
            return;
        }
        // Animated scroll timed out — fall back to non-animated
        await fallbackScroll();
    }

    private static T? FindAncestor<T>(Element element) where T : Element
    {
        var current = element.Parent;
        while (current != null)
        {
            if (current is T match) return match;
            current = current.Parent;
        }
        return null;
    }

    private static T? FindDescendant<T>(Element element) where T : Element
    {
        if (element is T match) return match;
        if (element is IVisualTreeElement vte)
        {
            foreach (var child in vte.GetVisualChildren())
            {
                if (child is Element childElement)
                {
                    var found = FindDescendant<T>(childElement);
                    if (found != null) return found;
                }
            }
        }
        return null;
    }

    protected async Task<T> DispatchAsync<T>(Func<T> func)
    {
        if (_dispatcher == null || _dispatcher.IsDispatchRequired == false)
            return func();

        var tcs = new TaskCompletionSource<T>();
        _dispatcher.Dispatch(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return await tcs.Task;
    }

    protected async Task<T?> DispatchAsync<T>(Func<Task<T?>> func) where T : class
    {
        if (_dispatcher == null || _dispatcher.IsDispatchRequired == false)
            return await func();

        var tcs = new TaskCompletionSource<T?>();
        _dispatcher.Dispatch(async () =>
        {
            try { tcs.SetResult(await func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return await tcs.Task;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _brokerRegistration?.Dispose();
        _server.Dispose();
        _logProvider?.Dispose();
    }

    private Task<HttpResponse> HandleLogs(HttpRequest request)
    {
        if (_logProvider == null)
            return Task.FromResult(HttpResponse.Error("File logging is not enabled"));

        var limitStr = request.QueryParams.GetValueOrDefault("limit", "100");
        var skipStr = request.QueryParams.GetValueOrDefault("skip", "0");
        var source = request.QueryParams.TryGetValue("source", out var s) ? s : null;

        if (!int.TryParse(limitStr, out var limit)) limit = 100;
        if (!int.TryParse(skipStr, out var skip)) skip = 0;

        var entries = _logProvider.Reader.Read(limit, skip, source);
        return Task.FromResult(HttpResponse.Json(entries));
    }

    private async Task<HttpResponse> HandleCdp(HttpRequest request)
    {
        if (CdpCommandHandler == null)
            return HttpResponse.Error("CDP not available (Blazor debug service not registered)");

        if (!(CdpReadyCheck?.Invoke() ?? false))
            return HttpResponse.Error("CDP not ready (WebView not initialized)");

        if (string.IsNullOrEmpty(request.Body))
            return HttpResponse.Error("Missing CDP command body");

        try
        {
            var result = await CdpCommandHandler(request.Body);
            return new HttpResponse
            {
                ContentType = "application/json",
                Body = result
            };
        }
        catch (Exception ex)
        {
            return HttpResponse.Error($"CDP command failed: {ex.Message}");
        }
    }
}

// Request DTOs
public class ActionRequest
{
    public string? ElementId { get; set; }
}

public class FillRequest
{
    public string? ElementId { get; set; }
    public string? Text { get; set; }
}

public class NavigateRequest
{
    public string? Route { get; set; }
}

public class SetPropertyRequest
{
    public string? Value { get; set; }
}

public class ScrollRequest
{
    public string? ElementId { get; set; }
    public double DeltaX { get; set; }
    public double DeltaY { get; set; }
    public bool Animated { get; set; } = true;
}
