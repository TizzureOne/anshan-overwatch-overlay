using System.Linq;
using System.Reflection;

namespace AnshanOverlay;

public static class BuildInfo
{
    public static string Version { get; } = typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
    public static bool IsBeta => Version.Contains('-');
    public static string Edition { get; } = typeof(BuildInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "AnshanEdition").Value!;
    public static string EditionName => Edition == "Lite" ? "精简版" : "完整版";
    public static string Label => Version + (IsBeta ? " · 测试版" : " · 正式版");
}
