using System.Windows;
using System.Windows.Controls;

namespace AnshanOverlay;

public sealed class CloseBehaviorWindow : Window
{
    internal readonly CheckBox RememberChoice = new() { Content = "记住我的选择（可在“设置”中修改）", IsChecked = true, Margin = new Thickness(0, 14, 0, 16) };
    internal readonly Button HideInterfaceButton = new() { Content = "仅关闭界面", IsDefault = true, Background = BoardView.Brush("#FF9C27"), Margin = new Thickness(0, 0, 8, 0) };
    internal readonly Button ExitButton = new() { Content = "退出程序", Margin = new Thickness(0, 0, 8, 0) };

    public CloseBehaviorWindow(Controller controller)
    {
        Style = (Style)FindResource(typeof(Window));
        Title = "关闭主面板"; Width = 440; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        Owner = controller.Panel; WindowStartupLocation = WindowStartupLocation.CenterOwner; Topmost = true; ShowInTaskbar = false; Icon = Assets.AppIcon;
        var root = new StackPanel { Margin = new Thickness(20) }; Content = root;
        Loaded += (_, _) => Motion.Reveal(root);
        root.Children.Add(new TextBlock { Text = "你希望如何关闭？", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
        root.Children.Add(new TextBlock { Text = "仅关闭界面：隐藏主面板，画布和工具栏继续运行。" + controller.PanelRestoreHint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        root.Children.Add(new TextBlock { Text = "退出程序：保存当前场景，关闭画布、工具栏及后台服务。", TextWrapping = TextWrapping.Wrap });
        root.Children.Add(RememberChoice);
        var error = new TextBlock { Foreground = BoardView.Brush("#C43A47"), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 10) }; root.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; root.Children.Add(buttons);
        buttons.Children.Add(HideInterfaceButton); buttons.Children.Add(ExitButton);
        var cancel = new Button { Content = "取消", IsCancel = true }; buttons.Children.Add(cancel);
        cancel.Click += (_, _) => Close();
        HideInterfaceButton.Click += (_, _) => Choose(PanelCloseBehavior.HidePanel);
        ExitButton.Click += (_, _) => Choose(PanelCloseBehavior.Exit);
        void Choose(PanelCloseBehavior behavior)
        {
            if (RememberChoice.IsChecked == true && controller.SetPanelCloseBehavior(behavior) is { } message)
            { error.Text = message; error.Visibility = Visibility.Visible; return; }
            Close();
            controller.Panel.RequestPanelClose(behavior);
        }
    }
}
