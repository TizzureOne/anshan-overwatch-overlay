using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace AnshanOverlay;

internal static class InlineTextTests
{
    internal static Task RunAsync(Action<bool, string> check, string output)
    {
        var controller = new Controller(test: true);
        var overlay = controller.Overlay; var input = overlay.TextEditor; var store = controller.Store;
        try
        {
            store.Preview(new Scene { Width = 800, Height = 600 }); controller.SetTool(EditorTool.Text);
            check(overlay.HandleTextClick(new Point(100, 100), 1) && input.IsEditing && input.Input.Text == "", "text tool starts an empty editor at the canvas click");
            input.Input.Text = "先占高台 <B>\r\n再保护后排";
            check(store.Current.Items.Count == 0 && !store.CanUndo, "typing keeps new text outside the scene and undo history");
            string draftFile = Path.Combine(output, "text-draft-autosave.json"); controller.WriteScene(draftFile, finishText: false);
            check(SceneStore.Parse(File.ReadAllText(draftFile), controller.Heroes.Heroes).Items.Count == 0 && input.IsEditing, "background autosave neither commits nor saves the editor draft");
            foreach (var key in new[] { Key.B, Key.T, Key.Delete, Key.Z })
            {
                var e = new KeyEventArgs(Keyboard.PrimaryDevice, HwndSource.FromHwnd(new WindowInteropHelper(overlay).Handle), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                input.Input.RaiseEvent(e);
                check(!e.Handled && controller.Editor.Tool == EditorTool.Text && input.IsEditing, "text input does not invoke canvas shortcuts: " + key);
            }
            Render("inline-text-draft.png");
            CheckVisibleLines("multiline editor shows its final line without clipping");
            check(overlay.HandleTextClick(new Point(700, 500), 1) && !input.IsEditing, "click outside completes editing without opening another draft");
            var label = store.Current.Items.Single();
            check(label.Text == "先占高台 <B>\n再保护后排" && label.Label!.Lines!.Length == 2 && label.TextWidth > 0, "completed text normalizes newlines and carries desktop line metrics");
            check(controller.Editor.Tool == EditorTool.Move && controller.Editor.SelectedId == label.Id, "completed text is selected and immediately draggable");
            Render("inline-text-completed.png");
            controller.Undo(); check(store.Current.Items.Count == 0 && !store.CanUndo, "one undo removes the whole text creation");
            controller.Redo(); check(store.Current.Items.Single().Text == label.Text, "redo restores complete multiline text");
            controller.Editor.Down(label.Start + new Vector(10, 10)); controller.Editor.Move(label.Start + new Vector(50, 40), false); controller.Editor.Up();
            check(store.Current.Items.Single().X == label.X + 40 && store.Current.Items.Single().Y == label.Y + 30, "completed multiline text can be dragged directly");
            controller.Undo();
            string before = store.Serialize();
            check(overlay.HandleTextClick(label.Start + new Vector(10, 10), 2) && input.IsEditing && input.Input.Text.Replace("\r", "") == label.Text, "double clicking existing text resumes editing in place");
            input.Input.Text = "准备集火\n先打支援";
            check(store.Serialize() == before && overlay.Board.EditingTextId == label.Id, "existing text remains unchanged for OBS while its local editor is active");
            input.Cancel(); check(store.Serialize() == before && overlay.Board.EditingTextId is null, "cancel restores original content and clears local editing state");
            overlay.HandleTextClick(label.Start + new Vector(10, 10), 2); input.Input.Text = "准备集火\n先打支援";
            controller.Editor.Settings[EditorTool.Text].Color = "#39A9FF"; controller.Editor.Settings[EditorTool.Text].FontSize = 36; input.UpdateStyle();
            check(input.Input.FontSize == 36 && input.Input.Foreground.ToString() == "#FF39A9FF", "color and size changes update the active editor");
            input.Commit();
            check(store.Current.Items.Single().Color == "#39A9FF" && store.Current.Items.Single().FontSize == 36, "text and style commit together");
            controller.Undo(); check(store.Serialize() == before, "one undo restores content and style together");
            overlay.HandleTextClick(label.Start + new Vector(10, 10), 2); input.Commit();
            check(store.Serialize() == before && store.CanRedo, "unchanged editing creates no undo step and preserves redo");
            overlay.HandleTextClick(label.Start + new Vector(10, 10), 2); input.Input.Clear(); input.Commit();
            check(store.Current.Items.Count == 0, "completing an emptied existing text removes it");
            controller.Undo(); check(store.Serialize() == before, "empty-text deletion is undoable");
            controller.SetTool(EditorTool.Text); overlay.HandleTextClick(new Point(400, 300), 1); input.Input.Text = ""; input.Commit();
            check(store.Serialize() == before, "empty new editor leaves no annotation");
            controller.SetTool(EditorTool.Text); overlay.HandleTextClick(new Point(400, 300), 1); input.Input.Text = "切换工具完成";
            controller.SetTool(EditorTool.Arrow);
            check(!input.IsEditing && store.Current.Items.Count == 2 && controller.Editor.Tool == EditorTool.Arrow, "switching tools completes valid text before changing tool");
            controller.SetTool(EditorTool.Text); overlay.HandleTextClick(new Point(400, 400), 1); input.Input.Text = "返回桌面完成"; controller.SetPassThrough(true);
            check(!input.IsEditing && controller.PassThrough && store.Current.Items.Count == 3, "desktop mode completes text and removes the input layer");
            controller.SetTool(EditorTool.Text); overlay.HandleTextClick(new Point(200, 400), 1); input.Input.Text = "Esc 放弃";
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, HwndSource.FromHwnd(new WindowInteropHelper(overlay).Handle), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent }; input.Input.RaiseEvent(escape);
            check(escape.Handled && !input.IsEditing && store.Current.Items.Count == 3 && !controller.PassThrough, "Escape cancels only the draft without leaving tactical mode");
            controller.SetTool(EditorTool.Text); overlay.HandleTextClick(new Point(20, 400), 1); input.Input.Text = "主动保存完成";
            string saved = Path.Combine(output, "inline-text-scene.json"); controller.WriteScene(saved);
            check(!input.IsEditing && SceneStore.Parse(File.ReadAllText(saved), controller.Heroes.Heroes).Items.Count == 4, "explicit saving completes the current draft");
            controller.SetTool(EditorTool.Text); overlay.HandleTextClick(new Point(200, 500), 1); input.Input.Text = "不保存这个草稿";
            string invalid = Path.Combine(output, "inline-invalid.json"); File.WriteAllText(invalid, "{}");
            File.WriteAllText(invalid, "{\"version\":99}");
            try { controller.LoadScene(invalid); } catch (InvalidDataException) { }
            check(input.IsEditing && input.Input.Text == "不保存这个草稿", "failed loading preserves the draft");
            controller.LoadScene(saved); check(!input.IsEditing && store.Current.Items.Count == 4, "successful loading discards the previous draft");
            store.Preview(new Scene { Width = 320, Height = 200 }); controller.SetTool(EditorTool.Text);
            overlay.HandleTextClick(new Point(319, 199), 1); input.Input.Text = "右下角输入\n中文与 English"; Render("inline-text-edge.png");
            CheckVisibleLines("edge editor shows its final line above the completion controls");
            var frame = input.Children.OfType<Border>().Single();
            check(new Rect(0, 0, 320, 200).Contains(new Rect(Canvas.GetLeft(frame), Canvas.GetTop(frame), frame.ActualWidth, frame.ActualHeight)), "inline editor and actions remain inside a small canvas edge");
            check(input.Commit() && new Rect(0, 0, 320, 200).Contains(Annotation.Bounds(store.Current.Items.Single())), "completed edge text stays inside the canvas");
            var wrapped = Annotation.Fit(new SceneItem { Kind = "text", Text = "一段需要自动换行的中文说明，包含 emoji 👨‍👩‍👧‍👦 和 English words。", TextWidth = 140 }, new Scene { Width = 800, Height = 600 });
            check(wrapped.Label!.Lines!.Length > 2 && string.Concat(wrapped.Label.Lines) == wrapped.Text, "wrapping preserves text and grapheme sequences");
            check(Annotation.ValidText(new string('界', 100)) && !Annotation.ValidText(new string('界', 501)) && !Annotation.ValidText(string.Join("\n", Enumerable.Repeat("行", 13))), "multiline input has explicit length and line limits");
            string json = JsonSerializer.Serialize(new Scene { Items = new() { wrapped } }, Assets.Json);
            File.WriteAllText(Path.Combine(output, "inline-text-obs-fixture.json"), json);
            var parsed = SceneStore.Parse(json, controller.Heroes.Heroes);
            check(parsed.Version == 4 && parsed.Items.Single().TextWidth == 140 && parsed.Items.Single().Label!.Lines!.SequenceEqual(wrapped.Label.Lines), "wrapped text layout survives scene save and reload");
            check((Native.GetWindowLong(new WindowInteropHelper(overlay).Handle, -16) & 0x10000000) == 0, "text regression uses a hidden application window");
            return Task.CompletedTask;
        }
        finally { input.Cancel(); controller.Close(); }

        void CheckVisibleLines(string name)
        {
            var last = input.Input.GetRectFromCharacterIndex(input.Input.Text.Length - 1);
            check(!last.IsEmpty && last.Bottom <= input.Input.ActualHeight - input.Input.Padding.Bottom && last.Top >= 0, name);
        }
        void Render(string name)
        {
            var content = (FrameworkElement)overlay.Content; overlay.Content = null;
            var preview = new Border { Background = BoardView.Brush("#34445A"), Child = content };
            ComfortTests.Render(preview, (int)store.Current.Width, (int)store.Current.Height, Path.Combine(output, name));
            preview.Child = null; overlay.Content = content;
        }
    }
}
