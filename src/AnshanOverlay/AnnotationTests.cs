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
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnshanOverlay;

internal static class AnnotationTests
{
    public static async Task RunAsync(Action<bool, string> check, string output)
    {
        var controller = new Controller(test: true);
        var store = controller.Store; var editor = controller.Editor;
        store.Preview(new Scene { Width = 800, Height = 600 });
        string draftText = "";
        void Click(double x, double y)
        {
            if (editor.Tool == EditorTool.Text) { controller.Overlay.TextEditor.Begin(new Point(x, y)); controller.Overlay.TextEditor.Input.Text = draftText; controller.Overlay.TextEditor.Commit(); }
            else { editor.Down(new Point(x, y)); editor.Up(); }
        }
        editor.Tool = EditorTool.Text; draftText = "   "; Click(80, 80);
        check(store.Current.Items.Count == 0 && !store.CanUndo, "empty text never creates an item or undo entry");
        editor.Tool = EditorTool.Text; draftText = "先占高台 <B>"; Click(80, 80);
        var label = store.Current.Items.Single();
        check(label.Kind == "text" && label.Text == draftText && Annotation.Bounds(label).Width > 100, "Chinese text is placed literally with measured bounds");
        store.Undo(); check(store.Current.Items.Count == 0, "text creation is a single undo");
        store.Redo(); check(store.Current.Items.Single().Text == label.Text, "redo restores text");
        store.Edit(s => s with { Items = s.Items.Concat(new[] { new SceneItem { Kind = "number", Text = "1", X = 60, Y = 190 }, new SceneItem { Kind = "number", Text = "2", X = 140, Y = 190 } }).ToList() });
        check(store.Current.Items.Where(i => i.Kind == "number").Select(i => i.Text).SequenceEqual(new[] { "1", "2" }), "legacy number fixtures remain displayable");
        store.Undo(); check(store.Current.Items.Count == 1, "undo remains valid with legacy number items");
        store.Redo(); check(store.Current.Items.Count == 3, "redo restores legacy scene content");
        editor.Down(new Point(220, 190)); editor.Cancel();
        check(store.Current.Items.Count == 3, "cancelling text placement preserves legacy scene content");
        string saved = Path.Combine(output, "annotation-scene.json");
        controller.WriteScene(saved); var parsed = SceneStore.Parse(File.ReadAllText(saved), controller.Heroes.Heroes);
        check(parsed.Version == 4 && parsed.Items.Select(i => (i.Kind, i.Text, i.FontSize, i.Color)).SequenceEqual(store.Current.Items.Select(i => (i.Kind, i.Text, i.FontSize, i.Color))), "saved labels preserve type content size and color");
        store.Preview(new Scene()); controller.LoadScene(saved);
        check(store.Current.Items.Count == 3 && store.Current.Items.Count(i => i.Kind == "number") == 2, "controller loading preserves old number items");
        foreach (int version in new[] { 1, 2, 3 })
        {
            var legacy = SceneStore.Parse($"{{\"version\":{version},\"width\":800,\"height\":600,\"items\":[{{\"id\":\"legacy\",\"heroId\":\"ana\"}}]}}", controller.Heroes.Heroes);
            check(legacy.Version == 4 && legacy.Items.Single().HeroId == "ana", "legacy scene remains readable: " + version);
        }
        foreach (string value in new[] { "\"\"", "null", "\"a\\u0001b\"", "\"" + new string('a', Annotation.MaxLength + 1) + "\"" })
        {
            bool rejected = false;
            try { SceneStore.Parse("{\"items\":[{\"kind\":\"text\",\"text\":" + value + "}]}", controller.Heroes.Heroes); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "invalid text scene is rejected: " + value);
        }
        store.Preview(parsed); editor.Tool = EditorTool.Move; editor.SelectedId = label.Id;
        var p = new Point(label.X + 10, label.Y + 10);
        editor.Down(p); editor.Move(p + new Vector(100, 50), false); editor.Up();
        check(editor.Selected?.X == label.X + 100 && editor.Selected?.Y == label.Y + 50, "text drag uses the whole label hit area");
        store.Undo(); check(store.Current.Items.First().X == label.X, "one undo restores a text move");
        editor.SelectedId = label.Id; editor.Duplicate();
        check(store.Current.Items.Count == 4 && editor.Selected?.Text == label.Text && editor.SelectedId != label.Id, "text duplication creates an independent item");
        editor.Delete(); check(store.Current.Items.Count == 3, "text deletion removes only the selected copy");
        store.Undo(); check(store.Current.Items.Count == 4, "deleted text can be restored");
        store.Preview(parsed); editor.Tool = EditorTool.Move;
        editor.Down(new Point(0, 0)); editor.Move(new Point(600, 300), false); editor.Up();
        check(editor.SelectedIds.Count == 3, "text and number join ordinary marquee selection");
        editor.Tool = EditorTool.Eraser; Click(90, 90);
        check(store.Current.Items.Count == 2 && store.Current.Items.All(i => i.Kind == "number"), "eraser removes a whole label without damaging nearby numbers");
        store.Undo(); check(store.Current.Items.Count == 3, "one undo restores an erased label");
        editor.Tool = EditorTool.Text; draftText = new string('界', 24); editor.Options.FontSize = 96;
        store.Preview(new Scene { Width = 320, Height = 200 }); Click(319, 199);
        check(new Rect(0, 0, 320, 200).Contains(Annotation.Bounds(store.Current.Items.Single())), "long label fits a small canvas at the bottom-right edge");
        store.Preview(parsed); editor.Tool = EditorTool.Move; editor.SelectedId = label.Id;
        controller.UpdateSelected(i => Annotation.Fit(i with { Text = "先保护后排", FontSize = 40 }, store.Current));
        check(editor.Selected?.Text == "先保护后排" && editor.Selected.FontSize == 40, "selected text and font size can be edited");
        store.Undo(); check(store.Current.Items.First().Text == label.Text && store.Current.Items.First().FontSize == label.FontSize, "editing a label is undoable");
        var settings = new Preferences(); settings.Shortcuts.Remove("text"); settings.Shortcuts["number"] = new Shortcut(Key.I); settings.Shortcuts["brush"] = new Shortcut(Key.T);
        string settingsFile = Path.Combine(output, "annotation-upgrade-settings.json"); File.WriteAllText(settingsFile, JsonSerializer.Serialize(settings, Assets.Json));
        var upgraded = Preferences.Load(out var warning, settingsFile);
        check(warning is null && upgraded.Shortcuts["brush"].Key == Key.T && upgraded.Shortcuts["text"].Key == Key.None, "new shortcuts preserve conflicting user bindings during upgrade");
        check(!upgraded.Shortcuts.ContainsKey("number") && Preferences.Actions.All(a => a.Id != "number") && !Enum.TryParse<EditorTool>("Number", out _), "number tool and old shortcut are removed without affecting other bindings");
        check(controller.Toolbar.CreateMoreMenu().Items.OfType<MenuItem>().All(i => !(i.Header as string ?? "").StartsWith("编号")), "toolbar menu has no number creation entry");

        var toolbar = controller.Toolbar; var content = (FrameworkElement)toolbar.Content; toolbar.Content = null;
        var preview = new Border { Background = Brushes.White, Child = content };
        draftText = "先占高台"; editor.Settings[EditorTool.Text].FontSize = 28;
        foreach (var tool in new[] { EditorTool.Text })
        foreach (var viewport in new[] { new Size(680, 220), new Size(340, 300), new Size(300, 300) })
        {
            controller.SetTool(tool);
            Render(preview, (int)viewport.Width, (int)viewport.Height, Path.Combine(output, $"annotation-{tool}-{viewport.Width:0}.png"));
            var toolButton = Descendants(preview).OfType<Button>().Single(b => b.Tag is EditorTool t && t == tool);
            check(toolButton.ActualWidth >= 40 && new Rect(new Point(), viewport).Contains(new Rect(toolButton.TranslatePoint(new Point(), preview), toolButton.RenderSize)), "label tool is reachable at " + viewport.Width + ": " + tool);
            check(Descendants(preview).OfType<ScrollViewer>().All(v => v.ScrollableHeight < 1 && v.ScrollableWidth < 1), "label parameters need no scrolling at " + viewport.Width + ": " + tool);
        }
        controller.SetTool(EditorTool.Text); Render(preview, 300, 300, Path.Combine(output, "annotation-input.png"));
        check(!Descendants(preview).OfType<TextBox>().Any(), "text tool has no separate toolbar content field");
        preview.Child = null; toolbar.Content = content;
        controller.Overlay.TextEditor.Begin(new Point(80, 80));
        var input = controller.Overlay.TextEditor.Input; input.Text = "输入中文 TEST";
        check(controller.Overlay.TextEditor.IsEditing && store.Current.Items.Count == 3, "inline text stays a draft until completion");
        var inputKey = new KeyEventArgs(Keyboard.PrimaryDevice, HwndSource.FromHwnd(new WindowInteropHelper(controller.Overlay).EnsureHandle()), 0, Key.B) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        input.RaiseEvent(inputKey);
        check(!inputKey.Handled && editor.Tool == EditorTool.Text, "typing B inside label input never selects the brush");
        controller.Overlay.TextEditor.Cancel();
        controller.SetTool(EditorTool.Move); editor.SelectedId = label.Id; controller.Notify();
        toolbar.Content = null; preview.Child = content; Render(preview, 300, 300, Path.Combine(output, "annotation-edit.png"));
        Descendants(preview).OfType<Button>().Single(b => Equals(b.ToolTip, "在文字原位置继续编辑")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        controller.Overlay.TextEditor.Input.Text = "第二步：集火"; controller.Overlay.TextEditor.Commit();
        check(store.Current.Items.First().Text == "第二步：集火", "toolbar edit entry edits the selected label in place");
        store.Undo(); check(store.Current.Items.First().Text == label.Text, "apply button produces one undo entry");
        preview.Child = null; toolbar.Content = content;
        Render(new Border { Background = BoardView.Brush("#253448"), Child = new BoardView { Scene = parsed, DrawEditorChrome = false } }, 800, 600, Path.Combine(output, "annotation-board.png"));

        await controller.Server.StartAsync(0); controller.Server.Publish(store.Serialize());
        using var socket = new ClientWebSocket(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{controller.Server.Port}/ws"), timeout.Token);
        async Task<Scene> Receive()
        {
            using var memory = new MemoryStream(); var buffer = new byte[65536]; WebSocketReceiveResult result;
            do { result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token); memory.Write(buffer, 0, result.Count); } while (!result.EndOfMessage);
            string json = Encoding.UTF8.GetString(memory.ToArray());
            check(json.Contains("\"label\":") && json.Contains("\"baseline\":"), "OBS snapshot carries desktop label metrics");
            return SceneStore.Parse(json, controller.Heroes.Heroes);
        }
        check((await Receive()).Items.Count(i => Annotation.IsLabel(i)) == 3, "OBS initial snapshot includes both label types");
        controller.UpdateSelected(i => i with { Text = "同步修改" });
        check((await Receive()).Items.First().Text == "同步修改", "controller automatically publishes label edits to OBS");
        controller.Undo(); check((await Receive()).Items.First().Text == label.Text, "controller automatically publishes label undo to OBS");
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test", timeout.Token);
        controller.Close();
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
}

