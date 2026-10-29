using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnshanOverlay;

internal static class TemporaryInkTests
{
    public static async Task RunAsync(Action<bool, string> check, string output)
    {
        long time = 1000;
        using (var ink = new TemporaryInk(() => time))
        {
            void DrawInk() { ink.Begin(new Point(20, 20), "#FFD166", 5, true); ink.Move(new Point(50, 50)); ink.Complete(); }
            check(!ink.TimerRunning && !ink.CanUndo, "idle temporary ink has no running timer");
            ink.Begin(new Point(20, 20), "#FFD166", 5, false);
            time += 6000; ink.Expire();
            check(ink.IsDrawing && !ink.TimerRunning, "held stroke waits until release before timing out");
            ink.Complete(); check(ink.Items.Length == 0 && !ink.TimerRunning, "click without a stroke leaves no item or timer");
            DrawInk(); string id = ink.Items.Single().Id;
            check(ink.TimerRunning && ink.Items.Single().Dashed, "completed stroke starts one timer and keeps its brush style");
            time += 4000; ink.Undo();
            check(ink.Items.Length == 0 && ink.CanRedo && ink.TimerRunning, "undo hides temporary ink while retaining its original deadline");
            ink.Redo(); check(ink.Items.Single().Id == id, "redo restores the same unexpired stroke");
            time += 1000; ink.Expire();
            check(ink.Items.Length == 0 && !ink.CanUndo && !ink.CanRedo && !ink.TimerRunning, "expiry removes stroke and history without extending the deadline");
            DrawInk(); ink.Undo(); time += 5000; ink.Redo();
            check(ink.Items.Length == 0 && !ink.CanRedo && !ink.TimerRunning, "redo cannot resurrect expired ink");
            DrawInk(); time += 2000; DrawInk(); time += 3000; ink.Expire();
            check(ink.Items.Length == 1 && ink.TimerRunning, "strokes expire independently");
            ink.Begin(new Point(90, 90), "#39A9FF", 4, false); ink.Move(new Point(120, 90)); ink.Cancel();
            check(ink.Items.Length == 1 && !ink.IsDrawing, "cancelling the active stroke preserves completed ink");
            ink.Clear(); check(ink.Items.Length == 0 && !ink.TimerRunning, "clear releases the timer immediately");
            DrawInk(); ink.Undo(); ink.ClearRedo();
            check(!ink.CanRedo && !ink.TimerRunning, "invalidating the last redo releases its timer");
            DrawInk(); ink.Dispose();
            ink.Begin(new Point(0, 0), "#FFFFFF", 4, false);
            check(ink.Items.Length == 0 && !ink.IsDrawing && !ink.TimerRunning, "disposed ink stays empty and cannot restart its timer");
        }

        var controller = new Controller(test: true); var store = controller.Store; var editor = controller.Editor;
        var permanent = new Scene { Width = 800, Height = 600, Items = new() {
            new SceneItem { HeroId = "ana", X = 100, Y = 100 },
            new SceneItem { Kind = "line", X = 300, Y = 160, X2 = 500, Y2 = 160, Color = "#39A9FF", Width = 6 },
            new SceneItem { Kind = "text", Text = "永久标注", X = 300, Y = 80 }
        } };
        store.Preview(permanent); string baseline = store.Serialize();
        controller.SetTool(EditorTool.Brush);
        check(!editor.TemporaryBrush, "temporary brush is off by default");
        controller.SetTemporaryBrush(true); editor.Width = 8;
        void Draw() { editor.Down(new Point(300, 300)); editor.Move(new Point(400, 350), false); editor.Move(new Point(500, 300), false); editor.Up(); }
        editor.Down(new Point(300, 300)); editor.Move(new Point(400, 350), false);
        check(editor.IsDragging && controller.DisplayScene.Items.Count == 4 && store.Serialize() == baseline, "live preview is visible without touching the scene store");
        string saved = Path.Combine(output, "temporary-permanent-scene.json"); controller.WriteScene(saved);
        check(File.ReadAllText(saved) == baseline && !store.CanUndo, "saving during a temporary stroke excludes its preview and undo history");
        editor.Cancel(); check(controller.DisplayScene.Items.Count == 3 && !editor.Temporary.TimerRunning, "cancel removes only the temporary preview");
        Draw(); controller.WriteScene(saved);
        check(File.ReadAllText(saved) == baseline && store.Current.Items.Count == 3, "saving a completed temporary stroke writes only permanent items");
        editor.Down(new Point(100, 100)); editor.Move(new Point(140, 100), false); editor.Up();
        check(store.Current.Items[0].X == 140 && editor.Temporary.Items.Length == 1, "hero dragging keeps priority with temporary brush enabled");
        store.Undo(); check(store.Serialize() == baseline, "hero movement remains in the permanent undo history");
        controller.Undo(); check(controller.DisplayScene.Items.Count == 3 && store.Serialize() == baseline, "controller undo removes temporary ink without changing permanent items");
        controller.Redo(); check(controller.DisplayScene.Items.Count == 4 && store.Serialize() == baseline, "controller redo restores only temporary ink");
        controller.SetTool(EditorTool.Arrow); editor.Down(new Point(300, 400)); editor.Move(new Point(500, 450), false); editor.Up();
        check(store.Current.Items.Count == 4 && store.Current.Items[^1].Arrow, "temporary setting does not affect the arrow tool");
        store.Undo(); controller.SetTool(EditorTool.Brush); controller.SetTemporaryBrush(false);
        Draw(); check(store.Current.Items.Count == 4 && store.Current.Items[^1].Kind == "path", "normal brush still adds a permanent stroke");
        store.Undo(); editor.Temporary.Clear();

        var toolbar = controller.Toolbar; var content = (FrameworkElement)toolbar.Content; toolbar.Content = null;
        var preview = new Border { Background = Brushes.White, Child = content };
        foreach (var size in new[] { new Size(680, 180), new Size(340, 300), new Size(300, 300) })
        {
            Render(preview, (int)size.Width, (int)size.Height, Path.Combine(output, $"temporary-toolbar-{size.Width:0}.png"));
            var toggle = Descendants(preview).OfType<ComboBox>().Single(c => c.Name == "BrushDurationChoice");
            check(new Rect(new Point(), size).Contains(new Rect(toggle.TranslatePoint(new Point(), preview), toggle.RenderSize)), "temporary toggle is fully visible at " + size.Width);
            check(Descendants(preview).OfType<ScrollViewer>().All(s => s.ScrollableHeight < 1 && s.ScrollableWidth < 1), "brush controls need no scrolling at " + size.Width);
        }
        var control = Descendants(preview).OfType<ComboBox>().Single(c => c.Name == "BrushDurationChoice");
        control.SelectedIndex = 1;
        check(editor.TemporaryBrush && store.Serialize() == baseline, "duration choice changes only the session tool setting");
        Render(preview, 300, 300, Path.Combine(output, "temporary-toolbar-active.png"));
        preview.Child = null; toolbar.Content = content;

        // Use the real controller timer and WebSocket pipeline for the five-second lifetime.
        Draw(); string transientId = editor.Temporary.Items.Single().Id;
        string beforeImage = Path.Combine(output, "temporary-before.png"), afterImage = Path.Combine(output, "temporary-after.png");
        Render(new BoardView { Scene = controller.DisplayScene, DrawEditorChrome = false }, 800, 600, beforeImage);
        Render(new Border { Background = BoardView.Brush("#253448"), Child = new BoardView { Scene = controller.DisplayScene, DrawEditorChrome = false } }, 800, 600, Path.Combine(output, "temporary-before-preview.png"));
        await controller.Server.StartAsync(0);
        controller.Server.Publish(JsonSerializer.Serialize(controller.DisplayScene, Assets.Json));
        async Task<Scene> Receive(ClientWebSocket client, Func<Scene, bool> match)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (true)
            {
                using var data = new MemoryStream(); var buffer = new byte[65536]; WebSocketReceiveResult result;
                do { result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token); data.Write(buffer, 0, result.Count); } while (!result.EndOfMessage);
                var snapshot = SceneStore.Parse(Encoding.UTF8.GetString(data.ToArray()), controller.Heroes.Heroes);
                if (match(snapshot)) return snapshot;
            }
        }
        var uri = new Uri($"ws://127.0.0.1:{controller.Server.Port}/ws");
        using var socket = new ClientWebSocket(); await socket.ConnectAsync(uri, CancellationToken.None);
        check((await Receive(socket, s => s.Items.Any(i => i.Id == transientId))).Items.Count == 4, "OBS initial connection receives active temporary ink and permanent content");
        controller.Undo();
        check((await Receive(socket, s => s.Items.All(i => i.Id != transientId))).Items.Count == 3, "OBS synchronizes temporary undo");
        controller.Redo();
        check((await Receive(socket, s => s.Items.Any(i => i.Id == transientId))).Items.Count == 4, "OBS synchronizes unexpired temporary redo");
        using (var reconnect = new ClientWebSocket())
        {
            await reconnect.ConnectAsync(uri, CancellationToken.None);
            check((await Receive(reconnect, s => s.Items.Any(i => i.Id == transientId))).Items.Count == 4, "OBS reconnection restores a still-active temporary stroke");
            await reconnect.CloseAsync(WebSocketCloseStatus.NormalClosure, "test", CancellationToken.None);
        }
        var expired = await Receive(socket, s => s.Items.All(i => i.Id != transientId));
        check(expired.Items.Count == 3 && controller.Overlay.Board.Scene.Items.Count == 3 && !editor.Temporary.TimerRunning, "real expiry clears desktop and OBS and stops the timer");
        check(store.Serialize() == baseline, "real expiry leaves the permanent scene byte-for-byte unchanged");
        Render(new BoardView { Scene = controller.DisplayScene, DrawEditorChrome = false }, 800, 600, afterImage);
        Render(new Border { Background = BoardView.Brush("#253448"), Child = new BoardView { Scene = controller.DisplayScene, DrawEditorChrome = false } }, 800, 600, Path.Combine(output, "temporary-after-preview.png"));
        check(Alpha(beforeImage, 400, 350) > 200 && Alpha(afterImage, 400, 350) == 0 && Alpha(afterImage, 400, 160) > 200, "rendered temporary ink disappears while permanent ink stays visible");
        using (var reconnect = new ClientWebSocket())
        {
            await reconnect.ConnectAsync(uri, CancellationToken.None);
            check((await Receive(reconnect, _ => true)).Items.Count == 3, "OBS reconnect after expiry cannot restore stale ink");
            await reconnect.CloseAsync(WebSocketCloseStatus.NormalClosure, "test", CancellationToken.None);
        }
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test", CancellationToken.None);

        Draw(); controller.Undo(); store.Edit(s => s with { ShowHeroNames = true });
        check(!editor.Temporary.CanRedo && !editor.Temporary.TimerRunning, "a new permanent edit invalidates temporary redo without retaining a timer");
        store.Undo(); Draw();
        string invalid = Path.Combine(output, "temporary-invalid-scene.json"); File.WriteAllText(invalid, "{broken");
        controller.Panel.LoadSceneFile(invalid);
        check(editor.Temporary.Items.Length == 1 && store.Serialize() == baseline, "failed scene loading preserves current temporary and permanent content");
        controller.LoadScene(saved);
        check(editor.Temporary.Items.Length == 0 && !editor.Temporary.TimerRunning && store.Current.Items.Count == 3, "successful scene loading clears temporary ink and its timer");
        controller.Undo();
        check(editor.Temporary.Items.Length == 0 && controller.DisplayScene.Items.All(i => permanent.Items.Any(p => p.Id == i.Id)), "scene undo cannot bring temporary ink back");
        Draw(); controller.ChangeScreen(controller.Screen);
        check(editor.Temporary.Items.Length == 0 && !editor.Temporary.TimerRunning, "screen changes clear temporary coordinates and timer");
        Draw(); controller.Clear(true);
        check(editor.Temporary.Items.Length == 0 && !editor.Temporary.TimerRunning && store.Current.Items.All(i => i.Kind == "hero"), "clear marks removes both layers while preserving heroes");
        controller.Undo(); check(store.Current.Items.Count == 3 && editor.Temporary.Items.Length == 0, "clear undo restores only permanent marks");
        Draw(); controller.SetTool(EditorTool.Move);
        check(editor.Temporary.Items.Length == 1, "switching tools leaves completed ink on its original timer");
        controller.Close();
        check(controller.IsClosing && editor.Temporary.Items.Length == 0 && !editor.Temporary.TimerRunning, "closing clears temporary state and releases the timer");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int n = 0; n < VisualTreeHelper.GetChildrenCount(root); n++)
        {
            var child = VisualTreeHelper.GetChild(root, n); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static void Render(FrameworkElement element, int width, int height, string path)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
    private static byte Alpha(string path, int x, int y)
    {
        using var file = File.OpenRead(path); var frame = BitmapFrame.Create(file, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var bitmap = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0); var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0); return pixel[3];
    }
}
