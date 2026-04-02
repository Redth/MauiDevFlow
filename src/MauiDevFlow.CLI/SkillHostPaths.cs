namespace MauiDevFlow.CLI;

public static class SkillHostPaths
{
    public const string CanonicalSourceBasePath = ".claude/skills/maui-ai-debugging";

    public static string GetSourceBasePath(string? host)
    {
        _ = NormalizeHost(host);
        return CanonicalSourceBasePath;
    }

    public static string GetInstallBasePath(string? host) => NormalizeHost(host) switch
    {
        "claude" => ".claude/skills/maui-ai-debugging",
        "codex" => ".agents/skills/maui-ai-debugging",
        _ => throw new ArgumentOutOfRangeException(nameof(host), "Supported hosts: claude, codex")
    };

    public static string NormalizeHost(string? host)
    {
        var normalized = string.IsNullOrWhiteSpace(host)
            ? "claude"
            : host.Trim().ToLowerInvariant();

        return normalized is "claude" or "codex"
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(host), "Supported hosts: claude, codex");
    }
}
