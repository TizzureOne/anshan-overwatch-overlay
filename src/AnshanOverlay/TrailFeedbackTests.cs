using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnshanOverlay;

internal static class TrailFeedbackTests
{
    internal static Task ProbeAsync(Action<bool, string> check, string output)
    {
        var measurements = new List<object>();
        long largestAllocation = 0; double slowestFrame = 0, largestRasterOverhead = 0;
        var baselineRaster = new Dictionary<int, double>();
        foreach (int heroes in new[] { 1, 6 })
        foreach (string style in new[] { "off", "dashed" })
        {
            bool enabled = style != "off";
            var controller = new Controller(test: true);
            try
            {
                var items = Enumerable.Range(0, heroes).Select(n => new SceneItem { HeroId = "ana", X = 400 + n * 90, Y = 400 }).ToList();
                controller.Store.Preview(new Scene { Width = 1920, Height = 1080, Items = items });
                var panel = (FrameworkElement)controller.Panel.Content;
                panel.Measure(new Size(474, 900)); panel.Arrange(new Rect(0, 0, 474, 900)); panel.UpdateLayout();
                controller.SetTool(EditorTool.Move); controller.SetHeroTrails(enabled, style == "dashed" ? HeroTrailStyle.Dashed : HeroTrailStyle.Tail, 10);
                controller.Editor.Select(items.Select(i => i.Id)); controller.Editor.Down(new Point(400, 400));
                Point Position(int n) => new(400 + 180 * Math.Sin(n * .035), 400 + 130 * Math.Cos(n * .035));
                for (int n = 0; n < 180; n++) controller.Editor.Move(Position(n), false);
                var drawing = new DrawingVisual(); var render = typeof(BoardView).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var board = controller.Overlay.Board;
                board.Measure(new Size(1920, 1080)); board.Arrange(new Rect(0, 0, 1920, 1080));
                var bitmap = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
                int panelRefreshes = 0; controller.Changed += () => panelRefreshes++;
                long before = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
                double rasterMilliseconds = 0;
                const int frames = 20;
                int displayItems = 0, bytes = 0;
                for (int n = 180; n < 180 + frames; n++)
                {
                    controller.Editor.Move(Position(n), false);
                    using (var context = drawing.RenderOpen()) render.Invoke(board, new object[] { context });
                    var scene = controller.DisplayScene; displayItems = scene.Items.Count;
                    string json = JsonSerializer.Serialize(scene, Assets.Json); bytes = System.Text.Encoding.UTF8.GetByteCount(json);
                    controller.Server.Publish(json);
                    var raster = Stopwatch.StartNew(); bitmap.Clear(); bitmap.Render(drawing); raster.Stop(); rasterMilliseconds += raster.Elapsed.TotalMilliseconds;
                }
                watch.Stop(); long allocated = (GC.GetAllocatedBytesForCurrentThread() - before) / frames;
                double milliseconds = watch.Elapsed.TotalMilliseconds / frames;
                double processingMilliseconds = (watch.Elapsed.TotalMilliseconds - rasterMilliseconds) / frames;
                if (!enabled) baselineRaster[heroes] = rasterMilliseconds / frames;
                else largestRasterOverhead = Math.Max(largestRasterOverhead, rasterMilliseconds / frames - baselineRaster[heroes]);
                measurements.Add(new { heroes, style, millisecondsPerFrame = milliseconds, processingMilliseconds, rasterMilliseconds = rasterMilliseconds / frames, allocatedBytesPerFrame = allocated, displayItems, snapshotBytes = bytes, panelRefreshes });
                if (enabled) { largestAllocation = Math.Max(largestAllocation, allocated); slowestFrame = Math.Max(slowestFrame, processingMilliseconds); }
            }
            finally { controller.Close(); }
        }
        var item = new ComboBoxItem { Content = "拖尾" };
        bool selectionSafe = WindowGestures.IsControl(item);
        File.WriteAllText(Path.Combine(output, "trail-performance.json"), JsonSerializer.Serialize(new { selectionSafe, measurements }, Assets.Json));
        check(largestRasterOverhead < 25, "dashed trails add less than 25 ms of actual raster work: " + largestRasterOverhead);
        check(selectionSafe, "choosing a dropdown item must not trigger blank-window dragging");
        check(largestAllocation < 1_000_000, "six-hero dashed frames allocate less than 1 MB: " + largestAllocation);
        check(slowestFrame < 25, "six-hero update/OBS work fits within 25 ms: " + slowestFrame);
        return Task.CompletedTask;
    }
}
