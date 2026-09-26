using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AnshanOverlay;

public static class WindowGestures
{
    public static bool IsControl(DependencyObject? target)
    {
        while (target is not null)
        {
            if (target is ButtonBase or TextBoxBase or RangeBase or ComboBox or Thumb or TabItem) return true;
            target = target is Visual ? VisualTreeHelper.GetParent(target) : LogicalTreeHelper.GetParent(target);
        }
        return false;
    }
    public static void EnableBlankDrag(Window window)
    {
        window.PreviewMouseLeftButtonDown += (_, e) => {
            if (e.LeftButton != MouseButtonState.Pressed || (IsControl(e.OriginalSource as DependencyObject) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))) return;
            var p = e.GetPosition(window);
            if (p.X < 8 || p.Y < 8 || p.X > window.ActualWidth - 8 || p.Y > window.ActualHeight - 8) return;
            e.Handled = true; window.DragMove();
        };
    }
}
