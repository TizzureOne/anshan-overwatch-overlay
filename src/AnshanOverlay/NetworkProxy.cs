using System;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace AnshanOverlay;

public enum NetworkProxyMode { System, Direct, Manual }

internal static class NetworkProxy
{
    internal const string AddressHint = "请输入 HTTP、HTTPS 或 SOCKS5 代理地址，例如 http://127.0.0.1:7890；不支持账号密码、路径或查询参数。";
    internal static bool TryParseAddress(string? text, out Uri? address)
    {
        text = text?.Trim();
        if (Uri.TryCreate(text, UriKind.Absolute, out address) &&
            address.Scheme is "http" or "https" or "socks5" && address.Host.Length > 0 && address.Port is > 0 and <= 65535 &&
            address.UserInfo.Length == 0 && address.AbsolutePath is "" or "/" && address.Query.Length == 0 && address.Fragment.Length == 0 &&
            !text!.Any(char.IsWhiteSpace) && !text.Contains('\\')) return true;
        address = null; return false;
    }
    internal static HttpClientHandler CreateHandler(Preferences settings, bool allowAutoRedirect = true)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = allowAutoRedirect, UseProxy = settings.ProxyMode != NetworkProxyMode.Direct };
        if (settings.ProxyMode == NetworkProxyMode.Manual)
        {
            if (!TryParseAddress(settings.ProxyAddress, out var address)) { handler.Dispose(); throw new InvalidOperationException(AddressHint); }
            handler.Proxy = new WebProxy(address!) { BypassProxyOnLocal = false };
        }
        return handler;
    }
    internal static HttpClient CreateClient(Preferences settings, bool heroData = false) =>
        new(CreateHandler(settings, allowAutoRedirect: !heroData)) { Timeout = heroData ? TimeSpan.FromSeconds(90) : TimeSpan.FromMinutes(10) };
}
