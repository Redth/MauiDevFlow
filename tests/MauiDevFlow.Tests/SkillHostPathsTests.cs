using MauiDevFlow.CLI;

namespace MauiDevFlow.Tests;

public class SkillHostPathsTests
{
    [Theory]
    [InlineData(null, ".claude/skills/maui-ai-debugging")]
    [InlineData("", ".claude/skills/maui-ai-debugging")]
    [InlineData("claude", ".claude/skills/maui-ai-debugging")]
    [InlineData("CLAUDE", ".claude/skills/maui-ai-debugging")]
    [InlineData("codex", ".agents/skills/maui-ai-debugging")]
    [InlineData("CODEX", ".agents/skills/maui-ai-debugging")]
    public void GetInstallBasePath_ReturnsExpectedPath(string? host, string expectedPath)
    {
        var path = SkillHostPaths.GetInstallBasePath(host);

        Assert.Equal(expectedPath, path);
    }

    [Fact]
    public void GetSourceBasePath_UsesClaudeSkillAsCanonicalSource()
    {
        var path = SkillHostPaths.GetSourceBasePath("codex");

        Assert.Equal(".claude/skills/maui-ai-debugging", path);
    }

    [Fact]
    public void GetInstallBasePath_RejectsUnknownHost()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => SkillHostPaths.GetInstallBasePath("copilot"));

        Assert.Contains("claude", ex.Message);
        Assert.Contains("codex", ex.Message);
    }
}
