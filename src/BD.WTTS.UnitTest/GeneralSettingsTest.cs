using BD.WTTS.Settings;

namespace BD.WTTS.UnitTest;

/// <summary>
/// 通用设置项测试
/// </summary>
/// <remarks>
/// 启动诊断输出必须默认关闭，且开关状态要能随配置文件持久化，
/// 否则用户打开后重启又会沉默，或反过来默认刷屏。
/// </remarks>
public sealed class GeneralSettingsTest
{
    [Test]
    public void ShowStartupDiagnosticsDefaultsToFalse()
    {
        var settings = new GeneralSettings_();

        Assert.That(settings.ShowStartupDiagnostics, Is.False);
    }

    [Test]
    public void ShowStartupDiagnosticsIsPersisted()
    {
        var settings = new GeneralSettings_ { ShowStartupDiagnostics = true };

        var json = System.Text.Json.JsonSerializer.Serialize(settings, GeneralSettingsContext.Default.GeneralSettings_);
        TestContext.Out.WriteLine(json);

        Assert.That(json, Does.Contain(nameof(GeneralSettings_.ShowStartupDiagnostics)));

        var restored = System.Text.Json.JsonSerializer.Deserialize(json, GeneralSettingsContext.Default.GeneralSettings_);
        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.ShowStartupDiagnostics, Is.True);
    }
}
