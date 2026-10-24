using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AnshanOverlay;

public sealed class ShortcutWindow : Window
{
    public ShortcutWindow(Controller controller)
    {
        Title = "安山守望 · 快捷键"; Width = 480; Height = 680; MinWidth = 400; MinHeight = 400;
        Owner = controller.Panel; WindowStartupLocation = WindowStartupLocation.CenterOwner; Topmost = true;
        Icon = Assets.AppIcon;
        var bindings = new Dictionary<string, Shortcut>(controller.Settings.Shortcuts);
        var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
        Loaded += (_, _) => Motion.Reveal(root);
        var hint = new TextBlock { Text = "点击输入框后按下组合键。全局快捷键在其他应用中也有效；编辑快捷键仅在本工具获得焦点时生效。", Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(hint, Dock.Top); root.Children.Add(hint);
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var error = new TextBlock { Foreground = BoardView.Brush("#C43A47"), Margin = new Thickness(0, 8, 0, 8) }; bottom.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        bottom.Children.Add(buttons);
        var reset = new Button { Content = "恢复默认", Margin = new Thickness(0, 0, 8, 0) }; buttons.Children.Add(reset);
        var save = new Button { Content = "保存快捷键", Background = BoardView.Brush("#FF9C27") }; buttons.Children.Add(save);
        var rows = new StackPanel(); root.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var fields = new Dictionary<string, TextBox>();
        foreach (var action in Preferences.Actions)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            row.Children.Add(new TextBlock { Text = action.Label + (action.Global ? " · 全局" : ""), VerticalAlignment = VerticalAlignment.Center });
            var field = new TextBox { Text = bindings[action.Id].ToString(), IsReadOnly = true, FontSize = 12, Padding = new Thickness(7), Margin = new Thickness(0, 0, 6, 0) };
            Grid.SetColumn(field, 1); row.Children.Add(field); fields[action.Id] = field;
            field.PreviewKeyDown += (_, e) => {
                e.Handled = true; var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
                bindings[action.Id] = new Shortcut(key, Keyboard.Modifiers); field.Text = bindings[action.Id].ToString();
                error.Text = Preferences.Validate(bindings) ?? "";
            };
            var clear = new Button { Content = "清除", Padding = new Thickness(3), FontSize = 11 }; Grid.SetColumn(clear, 2); row.Children.Add(clear);
            clear.Click += (_, _) => { bindings[action.Id] = new Shortcut(Key.None); field.Text = "未设置"; error.Text = Preferences.Validate(bindings) ?? ""; };
            rows.Children.Add(row);
        }
        reset.Click += (_, _) => { bindings = Preferences.Actions.ToDictionary(a => a.Id, a => a.Default); foreach (var pair in fields) pair.Value.Text = bindings[pair.Key].ToString(); error.Text = ""; };
        save.Click += (_, _) => { if (controller.ApplyShortcuts(bindings) is { } message) error.Text = message; else Close(); };
    }
}
