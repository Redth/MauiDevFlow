using System.Text.Json.Serialization;

namespace MauiDevFlow.Agent.Core;

/// <summary>
/// Represents a MAUI visual tree element with all inspectable properties.
/// </summary>
public class ElementInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("parentId")]
    public string? ParentId { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("fullType")]
    public string FullType { get; set; } = string.Empty;

    [JsonPropertyName("automationId")]
    public string? AutomationId { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value { get; set; }

    [JsonPropertyName("isVisible")]
    public bool IsVisible { get; set; }

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("isFocused")]
    public bool IsFocused { get; set; }

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 1.0;

    [JsonPropertyName("bounds")]
    public BoundsInfo? Bounds { get; set; }

    [JsonPropertyName("windowBounds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BoundsInfo? WindowBounds { get; set; }

    [JsonPropertyName("gestures")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Gestures { get; set; }

    [JsonPropertyName("styleClass")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? StyleClass { get; set; }

    [JsonPropertyName("nativeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NativeType { get; set; }

    [JsonPropertyName("nativeProperties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string?>? NativeProperties { get; set; }

    /// <summary>
    /// Effective text color as rendered by the platform (after theme/style resolution).
    /// Format: #AARRGGBB hex string. Only populated for text-rendering elements.
    /// Use this instead of fetching TextColor via /api/property — MAUI TextColor is often
    /// null (theme default), while this reflects the actual rendered color.
    /// </summary>
    [JsonPropertyName("effectiveTextColor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EffectiveTextColor { get; set; }

    /// <summary>
    /// Effective background color as rendered by the platform (after theme/style resolution).
    /// Format: #AARRGGBB hex string.
    /// </summary>
    [JsonPropertyName("effectiveBackgroundColor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EffectiveBackgroundColor { get; set; }

    [JsonPropertyName("accessibility")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AccessibilityInfo? Accessibility { get; set; }

    [JsonPropertyName("children")]
    public List<ElementInfo>? Children { get; set; }
}

/// <summary>
/// Element bounding rectangle in screen coordinates.
/// </summary>
public class BoundsInfo
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }
}

/// <summary>
/// Native accessibility properties extracted from the platform accessibility APIs.
/// </summary>
public class AccessibilityInfo
{
    [JsonPropertyName("isAccessibilityElement")]
    public bool IsAccessibilityElement { get; set; }

    [JsonPropertyName("label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; set; }

    [JsonPropertyName("hint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hint { get; set; }

    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value { get; set; }

    [JsonPropertyName("role")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Role { get; set; }

    [JsonPropertyName("traits")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Traits { get; set; }

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("isFocusable")]
    public bool IsFocusable { get; set; }

    [JsonPropertyName("isFocused")]
    public bool IsFocused { get; set; }

    [JsonPropertyName("isHeading")]
    public bool IsHeading { get; set; }

    [JsonPropertyName("order")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Order { get; set; }

    [JsonPropertyName("childCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ChildCount { get; set; }

    [JsonPropertyName("liveRegion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LiveRegion { get; set; }
}

/// <summary>
/// An element in the native screen reader traversal order, as the platform accessibility
/// framework would present it (VoiceOver on iOS, TalkBack on Android, etc.).
/// </summary>
public class NativeScreenReaderEntry
{
    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; set; }

    [JsonPropertyName("hint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hint { get; set; }

    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value { get; set; }

    [JsonPropertyName("role")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Role { get; set; }

    [JsonPropertyName("traits")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Traits { get; set; }

    [JsonPropertyName("isHeading")]
    public bool IsHeading { get; set; }

    [JsonPropertyName("windowBounds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BoundsInfo? WindowBounds { get; set; }

    [JsonPropertyName("elementId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ElementId { get; set; }

    [JsonPropertyName("nativeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NativeType { get; set; }
}

/// <summary>
/// Metadata for a registered CDP-capable WebView.
/// </summary>
public class CdpWebViewInfo
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("automationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AutomationId { get; set; }

    [JsonPropertyName("elementId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ElementId { get; set; }

    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }

    [JsonPropertyName("isReady")]
    public bool IsReady => ReadyCheck?.Invoke() ?? false;

    [JsonIgnore]
    public Func<string, Task<string>> CommandHandler { get; set; } = null!;

    [JsonIgnore]
    public Func<bool> ReadyCheck { get; set; } = () => false;
}
