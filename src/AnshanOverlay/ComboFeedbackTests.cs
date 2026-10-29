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

namespace AnshanOverlay;

internal static class ComboFeedbackTests
{
    internal static async Task RunAsync(Action<bool, string> check, string output)
    {
        await ProbeAsync(check, output);
        var controller = new Controller(test: true);
        try
        {
            var hero = new SceneItem { HeroId = "ana", X = 400, Y = 300 };
            controller.Store.Preview(new Scene { Width = 1000, Height = 640, Items = new() { hero } });
            controller.SetTool(EditorTool.Move); controller.Editor.SelectedId = hero.Id; controller.Notify();
            var content = (FrameworkElement)controller.Toolbar.Content; controller.Toolbar.Content = null;
            ComfortTests.Render(content, 584, 240, Path.Combine(output, "team-before.png"));
            var combo = ComfortTests.Descendants<ComboBox>(content).Single(c => AutomationProperties.GetName(c) == "选中英雄阵营");
            check(combo.FocusVisualStyle is null && WindowGestures.IsControl(combo), "compact selector uses the focus border and is never blank-window dragging");
            combo.SelectedIndex = 1;
            check(controller.Editor.Selected?.Team == "enemy", "team selection still updates the selected hero");
            ComfortTests.Render(content, 584, 240, Path.Combine(output, "team-red.png"));
            controller.Toolbar.Content = content;
            string before = controller.Store.Serialize();
            var celebration = new BrandCelebration(controller.LectureBar);
            var handle = new WindowInteropHelper(celebration).EnsureHandle();
            int flags = Native.GetWindowLong(handle, -20);
            check((flags & (Native.ExTransparent | Native.ExNoActivate)) == (Native.ExTransparent | Native.ExNoActivate), "celebration cannot intercept clicks or activate itself");
            var visual = celebration.Visual; celebration.Content = null;
            Motion.Enabled = true;
            var frames = new List<byte[]>();
            foreach (double progress in new[] { .12, .45, .85, 1d })
            {
                visual.SetValue(CelebrationView.ProgressProperty, progress);
                frames.Add(ComfortTests.Render(visual, 520, 360, Path.Combine(output, $"celebration-{progress}.png")));
            }
            check(!Motion.CanAnimate || !frames[0].SequenceEqual(frames[1]) && !frames[1].SequenceEqual(frames[2]), "celebration has distinct burst and fading stages");
            check(frames[3].All(v => v == 0), "finished celebration leaves no pixels");
            Motion.Enabled = false; visual.SetValue(CelebrationView.ProgressProperty, .3);
            check(ComfortTests.Render(visual, 520, 360, Path.Combine(output, "celebration-disabled.png")).All(v => v == 0), "animation opt-out suppresses the celebration");
            celebration.Content = visual; celebration.Close();
            check(!Native.IsWindow(handle) && controller.Store.Serialize() == before, "celebration closes cleanly without modifying the scene or OBS state");
        }
        finally { Motion.Enabled = false; controller.Close(); }
    }
    internal static Task ProbeAsync(Action<bool, string> check, string output)
    {
        var results = new List<object>(); bool fits = true;
        var controller = new Controller(test: true);
        try
        {
            var hero = new SceneItem { HeroId = "ana", X = 400, Y = 300, Size = 80 };
            controller.Store.Preview(new Scene { Width = 1000, Height = 640, Items = new() { hero } });
            controller.SetTool(EditorTool.Move); controller.Editor.SelectedId = hero.Id; controller.Notify();
            var toolbar = controller.Toolbar; var content = (FrameworkElement)toolbar.Content; toolbar.Content = null;
            var frame = new ContentControl { Content = content, FontFamily = toolbar.FontFamily, FontSize = toolbar.FontSize, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
            foreach (double scale in new[] { 1d, 1.25, 1.5, 2 })
            {
                ComfortTests.Render(frame, 584, 240, Path.Combine(output, $"team-combo-{scale}.png"), scale);
                var combo = ComfortTests.Descendants<ComboBox>(frame).Single(c => AutomationProperties.GetName(c) == "选中英雄阵营");
                var caption = ComfortTests.Descendants<ContentPresenter>(combo).Single(p => Equals(p.Content, "蓝方"));
                var text = new TextBlock { Text = "蓝方", FontFamily = combo.FontFamily, FontSize = combo.FontSize };
                text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                bool complete = caption.ActualHeight + .1 >= text.DesiredSize.Height;
                fits &= complete;
                results.Add(new { scale, controlHeight = combo.ActualHeight, captionHeight = caption.ActualHeight, requiredTextHeight = text.DesiredSize.Height, complete });
            }
            frame.Content = null; toolbar.Content = content;
        }
        finally { controller.Close(); }
        File.WriteAllText(Path.Combine(output, "combo-probe.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        check(fits, "compact team dropdown leaves enough height for complete Chinese glyphs at all rendered scales");
        return Task.CompletedTask;
    }
}
