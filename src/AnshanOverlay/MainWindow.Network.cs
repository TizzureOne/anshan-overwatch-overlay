using System.Windows;
using System.Windows.Controls;

namespace AnshanOverlay;

public partial class MainWindow
{
    private void ProxyModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        ProxyAddressBox.IsEnabled = ProxyModeChoice.SelectedIndex == (int)NetworkProxyMode.Manual;
        ProxyStateText.Text = "更改后请点击“保存代理设置”。";
    }
    private void ProxyAddressChanged(object sender, TextChangedEventArgs e)
    {
        if (ready) ProxyStateText.Text = "更改后请点击“保存代理设置”。";
    }
    private void SaveProxyClick(object sender, RoutedEventArgs e)
    {
        if (controller.ApplyProxyPreferences((NetworkProxyMode)ProxyModeChoice.SelectedIndex, ProxyAddressBox.Text) is { } error)
        { ProxyStateText.Text = error; return; }
        ProxyAddressBox.Text = controller.Settings.ProxyAddress;
        ProxyStateText.Text = "已保存，下次检查或下载时生效。可点击下方“检查软件更新”验证连接。";
    }
}
