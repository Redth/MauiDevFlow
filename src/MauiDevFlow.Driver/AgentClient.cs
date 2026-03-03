using System.Text;
using System.Text.Json;

namespace MauiDevFlow.Driver;

/// <summary>
/// HTTP client that communicates with the MauiDevFlow Agent running inside the MAUI app.
/// </summary>
public class AgentClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private bool _disposed;

    public string BaseUrl => _baseUrl;

    public AgentClient(string host = "localhost", int port = 9223)
    {
        _baseUrl = $"http://{host}:{port}";
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>
    /// Check if the agent is reachable.
    /// </summary>
    public async Task<AgentStatus?> GetStatusAsync(int? window = null)
    {
        var url = window != null ? $"/api/status?window={window}" : "/api/status";
        var response = await GetAsync<AgentStatus>(url);
        return response;
    }

    /// <summary>
    /// Get the visual tree from the running app.
    /// </summary>
    public async Task<List<ElementInfo>> GetTreeAsync(int maxDepth = 0, int? window = null)
    {
        var parts = new List<string>();
        if (maxDepth > 0) parts.Add($"depth={maxDepth}");
        if (window != null) parts.Add($"window={window}");
        var url = parts.Count > 0 ? $"/api/tree?{string.Join("&", parts)}" : "/api/tree";
        return await GetAsync<List<ElementInfo>>(url) ?? new();
    }

    /// <summary>
    /// Get a single element by ID.
    /// </summary>
    public async Task<ElementInfo?> GetElementAsync(string id)
    {
        return await GetAsync<ElementInfo>($"/api/element/{id}");
    }

    /// <summary>
    /// Query elements by type, automationId, and/or text.
    /// </summary>
    public async Task<List<ElementInfo>> QueryAsync(string? type = null, string? automationId = null, string? text = null)
    {
        var queryParts = new List<string>();
        if (type != null) queryParts.Add($"type={Uri.EscapeDataString(type)}");
        if (automationId != null) queryParts.Add($"automationId={Uri.EscapeDataString(automationId)}");
        if (text != null) queryParts.Add($"text={Uri.EscapeDataString(text)}");

        var url = $"/api/query?{string.Join("&", queryParts)}";
        return await GetAsync<List<ElementInfo>>(url) ?? new();
    }

    /// <summary>
    /// Query elements using a CSS selector string.
    /// </summary>
    public async Task<List<ElementInfo>> QueryCssAsync(string selector)
    {
        var url = $"{_baseUrl}/api/query?selector={Uri.EscapeDataString(selector)}";
        var response = await _http.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonSerializer.Deserialize<JsonElement>(body);
        if (json.ValueKind == JsonValueKind.Object &&
            json.TryGetProperty("success", out var s) && !s.GetBoolean())
        {
            var msg = json.TryGetProperty("error", out var e) ? e.GetString() : "Query failed";
            throw new InvalidOperationException(msg);
        }
        return json.Deserialize<List<ElementInfo>>() ?? new();
    }

    /// <summary>
    /// Tap an element.
    /// </summary>
    public async Task<bool> TapAsync(string elementId)
    {
        return await PostActionAsync("/api/action/tap", new { elementId });
    }

    /// <summary>
    /// Fill text into an element.
    /// </summary>
    public async Task<bool> FillAsync(string elementId, string text)
    {
        return await PostActionAsync("/api/action/fill", new { elementId, text });
    }

    /// <summary>
    /// Clear text from an element.
    /// </summary>
    public async Task<bool> ClearAsync(string elementId)
    {
        return await PostActionAsync("/api/action/clear", new { elementId });
    }

    /// <summary>
    /// Focus an element.
    /// </summary>
    public async Task<bool> FocusAsync(string elementId)
    {
        return await PostActionAsync("/api/action/focus", new { elementId });
    }

    /// <summary>
    /// Navigate to a Shell route.
    /// </summary>
    public async Task<bool> NavigateAsync(string route)
    {
        return await PostActionAsync("/api/action/navigate", new { route });
    }

    /// <summary>
    /// Scroll by delta or scroll an element into view.
    /// </summary>
    public async Task<bool> ScrollAsync(string? elementId = null, double deltaX = 0, double deltaY = 0, bool animated = true, int? window = null)
    {
        var url = "/api/action/scroll";
        if (window != null) url += $"?window={window}";
        return await PostActionAsync(url, new { elementId, deltaX, deltaY, animated });
    }

    /// <summary>
    /// Resize the app window.
    /// </summary>
    public async Task<bool> ResizeAsync(int width, int height, int? window = null)
    {
        var url = "/api/action/resize";
        if (window != null) url += $"?window={window}";
        return await PostActionAsync(url, new { width, height });
    }

    /// <summary>
    /// Take a screenshot (returns PNG bytes).
    /// Optionally target a specific element by ID or CSS selector.
    /// </summary>
    public async Task<byte[]?> ScreenshotAsync(int? window = null, string? elementId = null, string? selector = null)
    {
        try
        {
            var queryParams = new List<string>();
            if (window != null) queryParams.Add($"window={window}");
            if (elementId != null) queryParams.Add($"id={Uri.EscapeDataString(elementId)}");
            if (selector != null) queryParams.Add($"selector={Uri.EscapeDataString(selector)}");

            var url = queryParams.Count > 0
                ? $"{_baseUrl}/api/screenshot?{string.Join("&", queryParams)}"
                : $"{_baseUrl}/api/screenshot";

            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch { return null; }
    }

    /// <summary>
    /// Get a specific property value from an element.
    /// </summary>
    public async Task<string?> GetPropertyAsync(string elementId, string propertyName)
    {
        var result = await GetJsonAsync($"/api/property/{elementId}/{propertyName}");
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("value", out var val))
            return val.GetString();
        return null;
    }

    /// <summary>
    /// Gets the list of CDP WebViews registered with the agent.
    /// </summary>
    public async Task<JsonElement> GetCdpWebViewsAsync()
    {
        return await GetJsonAsync("/api/cdp/webviews");
    }

    private async Task<T?> GetAsync<T>(string path) where T : class
    {
        try
        {
            var response = await _http.GetStringAsync($"{_baseUrl}{path}");
            return JsonSerializer.Deserialize<T>(response);
        }
        catch { return null; }
    }

    private async Task<JsonElement> GetJsonAsync(string path)
    {
        try
        {
            var response = await _http.GetStringAsync($"{_baseUrl}{path}");
            return JsonSerializer.Deserialize<JsonElement>(response);
        }
        catch { return default; }
    }

    private async Task<bool> PostActionAsync(string path, object body)
    {
        try
        {
            var json = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{_baseUrl}{path}", content);
            if (!response.IsSuccessStatusCode) return false;

            var responseBody = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(responseBody);
            return result.TryGetProperty("success", out var success) && success.GetBoolean();
        }
        catch { return false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}

public class AgentStatus
{
    [System.Text.Json.Serialization.JsonPropertyName("agent")]
    public string? Agent { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("version")]
    public string? Version { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("platform")]
    public string? Platform { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("deviceType")]
    public string? DeviceType { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("idiom")]
    public string? Idiom { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("appName")]
    public string? AppName { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("running")]
    public bool Running { get; set; }
}
