using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AnshanOverlay;

internal static class PopupInteractionTests
{
    internal static async Task RunAsync(Action<bool, string> check, string output)
    {
        var controller = new Controller(test: true);
        var findings = new List<object>();
        bool attached = true;
        try
        {
            var hero = new SceneItem { HeroId = "ana", X = 400, Y = 300 };
            controller.Store.Preview(new Scene { Width = 1000, Height = 640, Items = new() { hero } });
            controller.SetTool(EditorTool.Move); controller.Editor.SelectedId = hero.Id; controller.Notify();
            var toolbar = controller.Toolbar;
            var content = (FrameworkElement)toolbar.Content; toolbar.Content = null;
            using var host = new HwndSource(new HwndSourceParameters("Anshan hidden popup anchor test") { Width = 584, Height = 260,
                WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = Native.ExNoActivate | Native.ExToolWindow }) { RootVisual = content };
            await Layout();
            var button = ComfortTests.Descendants<Button>(content).Single(b => b.Name == "HeroTrailButton");
            string sceneBeforeTrails = controller.Store.Serialize();
            var menu = controller.Overlay.CreateTrailMenu(); menu.PlacementTarget = button;
            using var menuHost = new HwndSource(new HwndSourceParameters("Anshan hidden trail menu test") { Width = 264, Height = 160,
                WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = Native.ExNoActivate | Native.ExToolWindow }) { RootVisual = menu };
            await Layout();
            menu.Items.OfType<MenuItem>().Single(m => Equals(m.Tag, "trail-dashed")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Layout(); Record("toolbar trail style", button);
            check(controller.Settings.HeroTrailsEnabled && controller.Settings.HeroTrailStyle == HeroTrailStyle.Dashed && button.Content.ToString()!.Contains("虚线"), "trail style and toolbar caption update together");
            var duration = ComfortTests.Descendants<Slider>(menu).Single();
            foreach (int seconds in new[] { 1, 10, 3 })
            {
                duration.Value = seconds;
                await Layout(); Record("toolbar trail duration " + seconds, button);
                check(controller.Settings.HeroTrailSeconds == seconds && button.Content.ToString()!.Contains(seconds + "秒"), "trail duration and toolbar caption update together: " + seconds);
            }
            foreach (string style in new[] { "trail-tail", "trail-off", "trail-dashed" })
            {
                menu.Items.OfType<MenuItem>().Single(m => Equals(m.Tag, style)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await Layout(); Record(style, button);
                check(menu.Items.OfType<MenuItem>().Count(m => m.IsChecked) == 1 && duration.IsEnabled == controller.Settings.HeroTrailsEnabled, "trail choices remain mutually exclusive and duration enabled state matches " + style);
            }
            check(controller.Store.Serialize() == sceneBeforeTrails && !controller.Store.CanUndo, "trail settings never alter the scene or create undo entries");
            menuHost.RootVisual = null;
            var team = ComfortTests.Descendants<ComboBox>(content).Single(c => AutomationProperties.GetName(c) == "选中英雄阵营");
            team.SelectedIndex = 1;
            controller.Notify(); await Layout();
            Record("toolbar team selection", team);
            check(controller.Editor.Selected?.Team == "enemy", "team dropdown still changes the selected hero");
            controller.Undo(); await Layout();
            check(ComfortTests.Descendants<ComboBox>(content).Single(c => AutomationProperties.GetName(c) == "选中英雄阵营").SelectedIndex == 0, "external undo refreshes the team dropdown");
            var size = ComfortTests.Descendants<Slider>(content).Single(s => AutomationProperties.GetName(s) == "头像尺寸");
            size.Value = 96; controller.Notify(); await Layout(); Record("toolbar hero size", size);
            check(controller.Editor.Selected?.Size == 96, "hero size slider still updates immediately");
            controller.SetTool(EditorTool.Brush); await Layout();
            var retention = ComfortTests.Descendants<ComboBox>(content).Single(c => c.Name == "BrushDurationChoice");
            retention.SelectedIndex = 1;
            controller.Notify(); await Layout();
            Record("toolbar brush retention", retention);
            check(controller.Editor.TemporaryBrush, "brush retention dropdown still enables temporary ink");
            controller.SetTemporaryBrush(false); await Layout();
            check(ComfortTests.Descendants<ComboBox>(content).Single(c => c.Name == "BrushDurationChoice").SelectedIndex == 0, "external brush retention changes refresh the dropdown");
            check((Native.GetWindowLong(host.Handle, -16) & 0x10000000) == 0 && (Native.GetWindowLong(menuHost.Handle, -16) & 0x10000000) == 0, "interaction regression windows remain hidden throughout");
            host.RootVisual = null; toolbar.Content = content;

            async Task Layout() { content.Measure(new Size(584, 260)); content.Arrange(new Rect(0, 0, 584, 260)); content.UpdateLayout(); await content.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle); }
            void Record(string action, Visual target)
            {
                bool connected = PresentationSource.FromVisual(target) == host;
                attached &= connected;
                findings.Add(new { action, anchorConnected = connected });
            }
        }
        finally { controller.Close(); }
        File.WriteAllText(Path.Combine(output, "popup-anchor-probe.json"), JsonSerializer.Serialize(findings, new JsonSerializerOptions { WriteIndented = true }));
        check(attached, "toolbar parameter changes must not detach active dropdowns or the trail popup anchor");
    }
}
