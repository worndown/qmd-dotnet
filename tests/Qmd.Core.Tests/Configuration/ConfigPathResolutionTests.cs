using FluentAssertions;
using Qmd.Core.Configuration;

namespace Qmd.Core.Tests.Configuration;

[Trait("Category", "Unit")]
public class ConfigPathResolutionTests : IDisposable
{
    private readonly string? _originalConfigDir;
    private readonly string? _originalXdgConfigHome;

    public ConfigPathResolutionTests()
    {
        _originalConfigDir = Environment.GetEnvironmentVariable("QMD_CONFIG_DIR");
        _originalXdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("QMD_CONFIG_DIR", _originalConfigDir);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _originalXdgConfigHome);
    }

    [Fact]
    public void GetConfigDir_DefaultsToLocalAppData()
    {
        Environment.SetEnvironmentVariable("QMD_CONFIG_DIR", null);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", null);
        var dir = ConfigManager.GetConfigDir();
        var expected = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        dir.Should().Be(Path.Combine(expected, "qmd"));
    }

    [Fact]
    public void GetConfigDir_QmdConfigDirTakesPriority()
    {
        Environment.SetEnvironmentVariable("QMD_CONFIG_DIR", "C:\\custom\\config");
        ConfigManager.GetConfigDir().Should().Be("C:\\custom\\config");
    }

    [Fact]
    public void SetIndexName_ChangesConfigFilePath()
    {
        Environment.SetEnvironmentVariable("QMD_CONFIG_DIR", null);
        var mgr = new ConfigManager();
        mgr.SetIndexName("custom");
        var path = mgr.GetConfigFilePath();
        path.Should().EndWith("custom.yml");
    }

    [Fact]
    public void GetConfigDir_XdgConfigHome_UsedWhenQmdConfigDirNotSet()
    {
        // XDG_CONFIG_HOME is used when QMD_CONFIG_DIR is not set
        Environment.SetEnvironmentVariable("QMD_CONFIG_DIR", null);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", "/xdg/config");
        var dir = ConfigManager.GetConfigDir();
        dir.Should().Be(Path.Combine("/xdg/config", "qmd"));
    }

    [Fact]
    public void GetConfigDir_XdgConfigHome_AppendsQmdSubdirectory()
    {
        // XDG_CONFIG_HOME appends qmd subdirectory
        Environment.SetEnvironmentVariable("QMD_CONFIG_DIR", null);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", "/home/agent/.config");
        var dir = ConfigManager.GetConfigDir();
        dir.Should().Be(Path.Combine("/home/agent/.config", "qmd"));
    }

    [Fact]
    public void GetConfigDir_QmdConfigDir_OverridesXdgConfigHome()
    {
        // QMD_CONFIG_DIR overrides XDG_CONFIG_HOME
        Environment.SetEnvironmentVariable("QMD_CONFIG_DIR", "/override");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", "/should-not-use");
        var dir = ConfigManager.GetConfigDir();
        dir.Should().Be("/override");
    }
}
