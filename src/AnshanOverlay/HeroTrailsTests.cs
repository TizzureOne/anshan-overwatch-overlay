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

internal static class HeroTrailsTests
{
    public static async Task RunAsync(Action<bool, string> verify, string output)
    {
        var checks = new List<string>();
        void Check(bool condition, string name) { verify(condition, name); checks.Add(name); }
        string settingsFile = Path.Combine(output, "trail-settings.json");
        File.WriteAllText(settingsFile, "{\"panelWidth\":555}");
        var settings = Preferences.Load(out var warning, settingsFile);
        Check(!settings.HeroTrailsEnabled && settings.HeroTrailStyle == HeroTrailStyle.Dashed && settings.HeroTrailSeconds == 3 && settings.PanelWidth == 555 && warning is null, "legacy settings keep other preferences and default to disabled three-second dashed trails");
        File.WriteAllText(settingsFile, "{\"heroTrailStyle\":99,\"heroTrailSeconds\":999}");
        settings = Preferences.Load(out _, settingsFile);
        Check(settings.HeroTrailStyle == HeroTrailStyle.Dashed && settings.HeroTrailSeconds == 10, "invalid persisted style and excessive duration recover safely");
        File.WriteAllText(settingsFile, "{\"heroTrailSeconds\":0}");
        Check(Preferences.Load(out _, settingsFile).HeroTrailSeconds == 1, "persisted duration has a one-second lower bound");

        long time = 1000;
        var hero = new SceneItem { HeroId = "ana", X = 100, Y = 100, Opacity = .8 };
        using (var trails = new HeroTrails(() => time))
        {
            trails.Move(hero, hero with { X = 300 });
            Check(trails.Items.Length == 0 && !trails.TimerRunning, "disabled trails allocate no visible items or timer");
            trails.Configure(true, HeroTrailStyle.Dashed, 3);
            trails.Move(hero, hero);
            Check(trails.Items.Length == 0, "clicking or remaining stationary leaves no trail");
            trails.Move(hero, hero with { X = 300 });
            Check(trails.Items.Length == 1 && trails.Items[0].Dashed && trails.Items[0].Color == hero.HeroBorder && trails.Items[0].Points[0] == new InkPoint(100, 100), "dashed trail starts at the actual hero center and uses its team color");
            time += 500; trails.Move(hero with { X = 300 }, hero with { X = 300, Y = 200 }); trails.Complete();
            Check(trails.Items[0].Points.Any(p => p.X == 300 && p.Y == 100) && trails.Items[0].Points[^1] == new InkPoint(300, 200), "trail preserves bends instead of replacing movement with a straight shortcut");
            time = 4000; trails.Expire();
            Check(trails.Items.Length == 1 && trails.Items[0].Points.All(p => p.X == 300), "older sections expire while newer sections remain");
            time = 4500; trails.Expire();
            Check(trails.Items.Length == 0 && !trails.TimerRunning, "all dashed sections expire at their own deadline and stop the timer");

            trails.Configure(true, HeroTrailStyle.Tail, 10);
            trails.Move(hero, hero with { X = 400 }); trails.Complete();
            var tail = trails.Items.Single().TrailBands!;
            Check(tail.Count > 2 && tail.Count <= 16 && tail[0].Width < tail[^1].Width && tail[0].Opacity < tail[^1].Opacity, "tail tapers in both width and opacity with bounded continuous bands");
            time += 5000; var faded = trails.Items.Single().TrailBands!;
            Check(faded[^1].Width < tail[^1].Width && faded[^1].Opacity < tail[^1].Opacity, "tail continues fading and narrowing after release");
            time += 5000; trails.Expire();
            Check(trails.Items.Length == 0 && !trails.TimerRunning, "ten-second tail expires exactly at the configured limit");

            trails.Configure(true, HeroTrailStyle.Dashed, 1);
            trails.Move(hero, hero with { X = 200 }); trails.Complete();
            string completed = trails.Items[0].Id;
            trails.Move(hero with { X = 200 }, hero with { X = 300 }); trails.Cancel();
            Check(trails.Items.Length == 1 && trails.Items[0].Id == completed, "cancelling a new drag removes only that drag's trail");
            time += 1000; trails.Expire();
            trails.Move(hero with { X = 300 }, hero with { X = 400 });
            Check(trails.Items[0].Points[0] == new InkPoint(300, 100), "movement after a long pause resumes at the current center without reconnecting expired history");
            trails.Configure(false, HeroTrailStyle.Dashed, 1);
            Check(trails.Items.Length == 0 && !trails.TimerRunning, "turning the feature off clears existing paths and timer immediately");
            trails.Configure(true, HeroTrailStyle.Tail, 3);
            for (int n = 0; n < 1000; n++) trails.Move(hero with { X = n * 4 }, hero with { X = n * 4 + 4 });
            var dense = trails.Items.Single().TrailBands!;
            Check(dense.Count <= 16 && dense.Sum(b => b.Points.Count) <= 530 && dense[0].Points[0] == new InkPoint(0, 100) && dense[^1].Points[^1] == new InkPoint(4000, 100), "long rapid drags bound sample density while retaining the full unexpired path");
            trails.Dispose(); trails.Move(hero, hero with { X = 200 });
            Check(trails.Items.Length == 0 && !trails.TimerRunning, "disposed trails cannot restart");
        }

        var controller = new Controller(test: true);
        try
        {
            var store = controller.Store; var editor = controller.Editor; var panel = controller.Panel;
            var scene = new Scene { Width = 800, Height = 600, Items = new() {
                hero with { Opacity = 1 },
                new SceneItem { HeroId = "genji", Team = "enemy", X = 100, Y = 240 },
                new SceneItem { Kind = "text", Text = "教学标注", X = 340, Y = 80 }
            } };
            store.Preview(scene); controller.SetTool(EditorTool.Move);
            var menu = controller.Overlay.CreateHeroMenu(scene.Items[0]);
            MenuItem Choice(string tag) => menu.Items.OfType<MenuItem>().Single(i => Equals(i.Tag, tag));
            var durationRow = (Grid)Choice("trail-duration").Header;
            var duration = durationRow.Children.OfType<Slider>().Single();
            Check(Choice("trail-off").IsChecked && !duration.IsEnabled, "hero context menu defaults to trails off");
            Choice("trail-tail").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); duration.Value = 7;
            Check(editor.Trails.Enabled && editor.Trails.Style == HeroTrailStyle.Tail && editor.Trails.Seconds == 7 && Choice("trail-tail").IsChecked, "context menu directly enables tail and edits its duration");
            Choice("trail-dashed").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(editor.Trails.Style == HeroTrailStyle.Dashed && Choice("trail-dashed").IsChecked && !Choice("trail-tail").IsChecked, "context menu switches to dashed without reopening");
            Choice("trail-off").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(!editor.Trails.Enabled && Choice("trail-off").IsChecked && !duration.IsEnabled, "context menu turns trails off and disables the duration control");
            Choice("trail-tail").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            File.WriteAllText(settingsFile, JsonSerializer.Serialize(controller.Settings, Assets.Json));
            settings = Preferences.Load(out warning, settingsFile);
            Check(settings.HeroTrailsEnabled && settings.HeroTrailStyle == HeroTrailStyle.Tail && settings.HeroTrailSeconds == 7 && warning is null, "all three trail preferences survive serialization and reload");
            Check(controller.SetHeroTrails(true, (HeroTrailStyle)99, 3) is not null && controller.SetHeroTrails(true, HeroTrailStyle.Tail, 0) is not null && editor.Trails.Seconds == 7, "invalid settings leave the current trail configuration unchanged");
            controller.SetHeroTrails(true, HeroTrailStyle.Dashed, 10);
            editor.Select(scene.Items.Select(i => i.Id)); editor.Down(hero.Start); editor.Move(new Point(320, 140), false); editor.Up();
            var groupPaths = editor.Trails.Items;
            Check(groupPaths.Length == 2 && groupPaths.Select(i => i.Color).ToHashSet().SetEquals(new[] { "#39A9FF", "#F05265" }), "mixed group movement produces one team-colored trail for each hero and none for labels");
            Check(groupPaths[0].Points[^1] == new InkPoint(320, 140) && groupPaths[1].Points[^1] == new InkPoint(320, 280), "group trails follow each hero's center independently");
            Check(controller.DisplayScene.Items.Take(2).All(i => i.Kind == "path") && store.Current.Items.Count == 3, "trails render behind heroes without becoming scene items");
            string saved = Path.Combine(output, "trail-scene.json"); controller.WriteScene(saved);
            Check(SceneStore.Parse(File.ReadAllText(saved), controller.Heroes.Heroes).Items.Count == 3 && !File.ReadAllText(saved).Contains(groupPaths[0].Id), "saving excludes all trail geometry");
            bool transientRejected = false;
            try { SceneStore.Parse(JsonSerializer.Serialize(controller.DisplayScene, Assets.Json), controller.Heroes.Heroes); }
            catch (InvalidDataException) { transientRejected = true; }
            Check(transientRejected, "display-only trail payloads cannot become permanent saved scene objects");
            controller.Undo();
            Check(store.Current.Items[0].X == 100 && store.Current.Items[1].X == 100 && editor.Trails.Items.Length == 0 && !store.CanUndo, "one undo reverses the full hero move without a trail undo step");
            controller.Redo();
            Check(store.Current.Items[0].X == 320 && editor.Trails.Items.Length == 0, "redo restores hero positions without resurrecting transient trails");

            foreach (var tool in Enum.GetValues<EditorTool>())
            {
                editor.Cancel(); editor.Trails.Clear(); store.Preview(scene); editor.Tool = tool; editor.SelectedId = null;
                editor.Down(hero.Start); editor.Move(new Point(260, 120), false); editor.Up();
                Check(store.Current.Items.Count == 3 && store.Current.Items[0].X == 260 && editor.Trails.Items.Length == 1, "hero drag retains priority and creates a trail with tool " + tool);
            }
            controller.SetTool(EditorTool.Move); editor.Trails.Clear(); store.Preview(scene); editor.SelectedId = null;
            editor.Down(new Point(110, 105)); editor.Move(new Point(-100, -100), false);
            Check(editor.Trails.Items.Single().Points[^1] == new InkPoint(40, 40), "offset grabbing and canvas clamping record the hero center rather than the pointer");
            editor.Cancel();
            Check(store.Current.Items[0].Start == hero.Start && editor.Trails.Items.Length == 0, "cancel restores the hero and discards its unfinished trail");
            editor.Down(new Point(600, 400)); editor.Move(new Point(750, 550), false); editor.Up();
            Check(editor.Trails.Items.Length == 0, "selection box movement does not create trails");

            void Drag()
            {
                editor.Cancel(); editor.Trails.Clear(); store.Preview(scene); editor.Tool = EditorTool.Move; editor.SelectedId = null;
                editor.Down(hero.Start); editor.Move(new Point(220, 100), false); editor.Move(new Point(300, 180), false); editor.Move(new Point(460, 180), false); editor.Up();
            }
            Drag(); editor.Down(new Point(190, 100));
            Check(editor.SelectedIds.Count == 0, "visible trail geometry is not selectable"); editor.Cancel();
            string broken = Path.Combine(output, "trail-invalid-scene.json"); File.WriteAllText(broken, "{broken");
            controller.Panel.LoadSceneFile(broken);
            Check(editor.Trails.Items.Length == 1, "failed scene loading preserves existing trails");
            controller.LoadScene(saved);
            Check(editor.Trails.Items.Length == 0 && !editor.Trails.TimerRunning, "successful loading clears paths and timer");
            Drag(); controller.ChangeScreen(controller.Screen);
            Check(editor.Trails.Items.Length == 0 && !editor.Trails.TimerRunning, "changing screens clears stale trail coordinates");
            Drag(); controller.Clear(true);
            Check(editor.Trails.Items.Length == 0 && store.Current.Items.All(i => i.Kind == "hero"), "clearing annotations removes trails while preserving heroes");
            Drag(); controller.Delete();
            Check(editor.Trails.Items.Length == 0, "deleting a hero removes its remaining trail");

            Render(menu, 264, 500, Path.Combine(output, "trail-context-menu.png"));
            var menuBounds = new Rect(0, 0, 264, 500);
            Check(menuBounds.Contains(new Rect(Choice("trail-tail").TranslatePoint(new Point(), menu), Choice("trail-tail").RenderSize)), "tail choice is fully visible in the first-level hero menu");
            Check(menuBounds.Contains(new Rect(durationRow.TranslatePoint(new Point(), menu), durationRow.RenderSize)) && duration.ActualWidth >= 140, "duration slider is visible and wide enough in the hero menu");
            Check(WindowGestures.IsControl(Choice("trail-tail")) && WindowGestures.IsControl(duration), "right-click menu controls cannot start blank-window dragging");

            controller.SetHeroTrails(true, HeroTrailStyle.Dashed, 3); Drag();
            var dashedScene = controller.DisplayScene;
            RenderScene(dashedScene, "dashed");
            controller.SetHeroTrails(true, HeroTrailStyle.Tail, 3); Drag();
            var tailScene = controller.DisplayScene;
            RenderScene(tailScene, "tail");
            Check(dashedScene.Items.First().Dashed && tailScene.Items.First().TrailBands is { Count: > 1 }, "desktop snapshots contain distinct dashed and tapered styles");
            void RenderScene(Scene current, string label) => Render(new Border { Background = BoardView.Brush("#253448"), Child = new BoardView { Scene = current, DrawEditorChrome = false } }, 800, 600, Path.Combine(output, "trail-" + label + ".png"));
            File.WriteAllText(Path.Combine(output, "trail-render-fixtures.json"), JsonSerializer.Serialize(new { dashed = dashedScene, tail = tailScene, expired = store.Current }, Assets.Json));

            // Exercise the actual dispatcher clock and OBS network pipeline at one second.
            controller.SetHeroTrails(true, HeroTrailStyle.Dashed, 1);
            await controller.Server.StartAsync(0);
            Drag(); string trailId = editor.Trails.Items.Single().Id;
            controller.Server.Publish(JsonSerializer.Serialize(controller.DisplayScene, Assets.Json));
            async Task<Scene> Receive(ClientWebSocket client, Func<Scene, bool> match)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (true)
                {
                    using var data = new MemoryStream(); var buffer = new byte[65536]; WebSocketReceiveResult result;
                    do { result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token); data.Write(buffer, 0, result.Count); } while (!result.EndOfMessage);
                    var current = JsonSerializer.Deserialize<Scene>(Encoding.UTF8.GetString(data.ToArray()), Assets.Json)!;
                    if (match(current)) return current;
                }
            }
            var uri = new Uri($"ws://127.0.0.1:{controller.Server.Port}/ws");
            using var socket = new ClientWebSocket(); await socket.ConnectAsync(uri, CancellationToken.None);
            Check((await Receive(socket, s => s.Items.Any(i => i.Id == trailId))).Items[0].Dashed, "OBS initial connection receives the active dashed trail");
            using (var reconnect = new ClientWebSocket())
            {
                await reconnect.ConnectAsync(uri, CancellationToken.None);
                Check((await Receive(reconnect, s => s.Items.Any(i => i.Id == trailId))).Items.Count == 4, "OBS reconnect receives a still-active trail");
                await reconnect.CloseAsync(WebSocketCloseStatus.NormalClosure, "test", CancellationToken.None);
            }
            var expired = await Receive(socket, s => s.Items.All(i => i.Id != trailId));
            Check(expired.Items.Count == 3 && controller.Overlay.Board.Scene.Items.Count == 3 && !editor.Trails.TimerRunning, $"real timer clears the desktop and OBS trail after one second (OBS={expired.Items.Count}, desktop={controller.Overlay.Board.Scene.Items.Count}, timer={editor.Trails.TimerRunning})");
            Check(store.Current.Items[0].X == 460, "trail expiry preserves the hero's final position");
            using (var reconnect = new ClientWebSocket())
            {
                await reconnect.ConnectAsync(uri, CancellationToken.None);
                Check((await Receive(reconnect, _ => true)).Items.Count == 3, "reconnect after expiry cannot restore a stale path");
                await reconnect.CloseAsync(WebSocketCloseStatus.NormalClosure, "test", CancellationToken.None);
            }
            controller.SetHeroTrails(true, HeroTrailStyle.Tail, 2); Drag();
            var liveTail = await Receive(socket, s => s.Items.Count > 3);
            double firstOpacity = liveTail.Items.First().TrailBands!.Max(b => b.Opacity);
            var fadedTail = await Receive(socket, s => s.Items.FirstOrDefault()?.TrailBands is { Count: > 0 } bands && bands.Max(b => b.Opacity) < firstOpacity * .7);
            Check(fadedTail.Items.First().TrailBands![^1].Opacity < firstOpacity && fadedTail.Items.First().TrailBands![^1].Width < liveTail.Items.First().TrailBands![^1].Width, "OBS receives progressive opacity and width updates for a released tail");
            Check((await Receive(socket, s => s.Items.Count == 3)).Items.All(i => i.Kind != "path") && editor.Trails.Items.Length == 0, "real two-second tail disappears from both outputs");
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test", CancellationToken.None);
            Drag(); int segmentCount = editor.Trails.Items.Length; controller.SetPassThrough(true);
            Check(segmentCount > 0 && editor.Trails.Items.Length == segmentCount && editor.Trails.TimerRunning && controller.Overlay.Board.PassThroughOpacity == .5, "completed trails keep their countdown and desktop opacity when returning to the game");
            controller.Close();
            Check(!editor.Trails.TimerRunning && editor.Trails.Items.Length == 0, "closing releases trail state and timer");
        }
        finally { if (!controller.IsClosing) controller.Close(); }
        File.WriteAllText(Path.Combine(output, "hero-trails-checks.json"), JsonSerializer.Serialize(checks, Assets.Json));
    }
    private static void Render(FrameworkElement element, int width, int height, string path)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
}
