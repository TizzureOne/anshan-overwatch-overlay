using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AnshanOverlay;

internal static class EdgeCaseTests
{
    public static async Task RunAsync(Action<bool, string> check, string output)
    {
        var results = new List<(string Name, bool Passed, string Detail)>();
        void Probe(string name, Func<bool> test)
        {
            try { results.Add((name, test(), "")); }
            catch (Exception ex) { results.Add((name, false, ex.GetType().Name + ": " + ex.Message)); }
        }
        var controller = new Controller(test: true);
        controller.Store.Preview(new Scene { Items = new() { new SceneItem { HeroId = "ana", X = 120, Y = 120 } } });
        string beforeLoad = controller.Store.Serialize();
        string invalidScene = Path.Combine(output, "unsupported-scene.json");
        File.WriteAllText(invalidScene, "{\"version\":99,\"width\":1920,\"height\":1080,\"items\":[]}");
        Probe("loading an unsupported scene reports failure without crashing or changing the current scene", () =>
        {
            controller.Panel.LoadSceneFile(invalidScene);
            return controller.Status.StartsWith("加载失败：") && controller.Store.Serialize() == beforeLoad;
        });
        File.WriteAllText(invalidScene, "{broken json");
        Probe("loading malformed JSON preserves the current scene and reports failure", () =>
        {
            controller.Panel.LoadSceneFile(invalidScene);
            return controller.Status.StartsWith("加载失败：") && controller.Store.Serialize() == beforeLoad;
        });
        string settingsPath = Path.Combine(output, "invalid-shortcuts.json");
        var preferences = new Preferences(); preferences.Shortcuts["line"] = preferences.Shortcuts["brush"];
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(preferences, Assets.Json));
        Probe("invalid shortcut settings recover to valid defaults and a warning at startup", () =>
        {
            var loaded = Preferences.Load(out var warning, settingsPath);
            return warning is not null && Preferences.Validate(loaded.Shortcuts) is null;
        });
        Probe("missing settings use first-run close and shortcut defaults", () =>
        {
            var loaded = Preferences.Load(out var warning, Path.Combine(output, "missing-settings.json"));
            return warning is null && loaded.CloseBehavior == PanelCloseBehavior.Ask && Preferences.Validate(loaded.Shortcuts) is null;
        });
        foreach (var corner in new[] { new Point(40, 40), new Point(1880, 40), new Point(40, 1040), new Point(1880, 1040) })
        {
            var store = new SceneStore(); var editor = new Editor(store);
            var hero = new SceneItem { HeroId = "ana", X = corner.X, Y = corner.Y };
            store.Preview(new Scene { Items = new() { hero } }); editor.SelectedId = hero.Id;
            Probe("repeated duplication stays inside the canvas from " + corner, () =>
            {
                for (int n = 0; n < 5; n++)
                {
                    editor.Duplicate();
                    if (editor.Selected is not { } copy || !new Rect(0, 0, store.Current.Width, store.Current.Height).Contains(Editor.SelectionBounds(copy))) return false;
                }
                return store.Current.Items.Count == 6;
            });
        }
        var groupStore = new SceneStore(); var groupEditor = new Editor(groupStore);
        var group = new Scene { Items = new() {
            new SceneItem { HeroId = "ana", X = 1840, Y = 1000 },
            new SceneItem { HeroId = "ashe", X = 1880, Y = 1040 },
            new SceneItem { Kind = "path", X = 1800, Y = 980, X2 = 1840, Y2 = 1000, Points = new() { new(1800, 980), new(1840, 1000) }, Erasures = new() { new(1810, 985, 1820, 990, 5) } }
        } };
        groupStore.Preview(group); groupEditor.Select(group.Items.Select(i => i.Id));
        Probe("edge duplication preserves a mixed group and keeps every copy visible", () =>
        {
            groupEditor.Duplicate(); var copies = groupEditor.SelectedItems;
            var delta = copies[0].Start - group.Items[0].Start;
            return copies.Length == 3 && copies.All(i => new Rect(0, 0, 1920, 1080).Contains(Editor.SelectionBounds(i)))
                && copies.Zip(group.Items).All(pair => pair.First.Start - pair.Second.Start == delta)
                && copies[2].Points[0].X - group.Items[2].Points[0].X == delta.X
                && copies[2].Erasures[0].Y - group.Items[2].Erasures[0].Y == delta.Y;
        });
        Probe("one undo removes only the duplicated group and redo restores it", () =>
        {
            string duplicated = groupStore.Serialize(); groupStore.Undo();
            if (!groupStore.Current.Items.SequenceEqual(group.Items)) return false;
            groupStore.Redo(); return groupStore.Serialize() == duplicated;
        });
        Scene FullScene(int count) => new() { Items = Enumerable.Range(0, count).Select(_ => new SceneItem { HeroId = "ana", X = 100, Y = 100 }).ToList() };
        bool Reloadable(SceneStore store) => SceneStore.Parse(store.Serialize(), controller.Heroes.Heroes).Items.Count == store.Current.Items.Count;
        Probe("dropping a hero at the supported item limit cannot create an unreadable autosave", () =>
        {
            controller.Store.Preview(FullScene(2000));
            controller.PlaceHero(controller.Heroes.Heroes[0], new Point(500, 500));
            return Reloadable(controller.Store) && controller.Store.Current.Items.Count == 2000;
        });
        Probe("batch addition is atomic when only one item slot remains", () =>
        {
            controller.Store.Preview(FullScene(1999));
            controller.AddHeroes(controller.Heroes.Heroes.Take(2).ToArray());
            return Reloadable(controller.Store) && controller.Store.Current.Items.Count == 1999;
        });
        Probe("duplication at the supported item limit preserves the scene and selected original", () =>
        {
            var store = new SceneStore(); var editor = new Editor(store); store.Preview(FullScene(2000));
            string original = store.Current.Items[0].Id; editor.SelectedId = original; editor.Duplicate();
            return Reloadable(store) && store.Current.Items.Count == 2000 && editor.SelectedId == original && !store.CanUndo;
        });
        Probe("drawing at capacity does not create an unreadable autosave and works again after deleting an item", () =>
        {
            var store = new SceneStore(); var editor = new Editor(store); store.Preview(FullScene(2000)); editor.Tool = EditorTool.Line;
            editor.Down(new Point(600, 600)); editor.Move(new Point(700, 650), false); editor.Up();
            if (!Reloadable(store) || store.Current.Items.Count != 2000 || store.CanUndo) return false;
            editor.SelectedId = store.Current.Items[0].Id; editor.Delete();
            editor.Down(new Point(600, 600)); editor.Move(new Point(700, 650), false); editor.Up();
            return Reloadable(store) && store.Current.Items.Count == 2000 && store.Current.Items[^1].Kind == "line";
        });
        Probe("all drawing tools stop cleanly at capacity without leaving an active gesture", () =>
        {
            foreach (var tool in new[] { EditorTool.Brush, EditorTool.Line, EditorTool.Arrow, EditorTool.Circle, EditorTool.Rectangle })
            {
                var store = new SceneStore(); var editor = new Editor(store) { Tool = tool }; store.Preview(FullScene(2000));
                editor.Down(new Point(600, 600));
                if (editor.IsDragging) return false;
                editor.Move(new Point(700, 650), false); editor.Up();
                if (!Reloadable(store) || store.Current.Items.Count != 2000 || store.CanUndo) return false;
            }
            return true;
        });
        Probe("moving and deleting existing objects and undo remain usable at capacity", () =>
        {
            var store = new SceneStore(); var editor = new Editor(store); store.Preview(FullScene(2000));
            editor.Down(new Point(100, 100)); editor.Move(new Point(200, 200), false); editor.Up();
            if (editor.Selected?.X != 200 || !Reloadable(store)) return false;
            editor.Delete(); if (store.Current.Items.Count != 1999) return false;
            store.Undo(); if (store.Current.Items.Count != 2000) return false;
            store.Undo(); return store.Current.Items.All(i => i.X == 100) && Reloadable(store);
        });
        Probe("a rejected batch keeps the pending hero choices so it can be retried after freeing space", () =>
        {
            controller.Store.Preview(FullScene(1999)); controller.Panel.BatchMode.IsChecked = true;
            foreach (var hero in controller.Heroes.Heroes.Take(2)) controller.Panel.ToggleBatchHero(hero);
            controller.Panel.AddHeroesButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if (!controller.Panel.AddHeroesButton.IsEnabled || !controller.Panel.BatchHint.Text.StartsWith("已选 2 位") || controller.Store.Current.Items.Count != 1999) return false;
            controller.Editor.SelectedId = controller.Store.Current.Items[0].Id; controller.Delete();
            controller.Panel.AddHeroesButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            return !controller.Panel.AddHeroesButton.IsEnabled && controller.Store.Current.Items.Count == 2000 && Reloadable(controller.Store);
        });
        Probe("the last supported slot can still receive a hero and gives a useful limit message on the next attempt", () =>
        {
            controller.Store.Preview(FullScene(1999));
            if (!controller.PlaceHero(controller.Heroes.Heroes[0], new Point(500, 500))) return false;
            if (controller.PlaceHero(controller.Heroes.Heroes[1], new Point(600, 500))) return false;
            return controller.Store.Current.Items.Count == 2000 && Reloadable(controller.Store) && controller.Status.Contains("2000");
        });
        controller.Store.Preview(SceneStore.Parse(beforeLoad, controller.Heroes.Heroes));
        var panel = new WindowInteropHelper(controller.Panel).EnsureHandle();
        controller.Settings.CloseBehavior = PanelCloseBehavior.Exit;
        controller.Panel.Close();
        await Dispatcher.Yield(DispatcherPriority.Background);
        Probe("hiding the panel retains its native hotkey window and scene", () => !controller.IsClosing && Native.IsWindow(panel) && controller.Store.Serialize() == beforeLoad);
        controller.Close();
        var ownerClose = new Controller(test: true);
        var ownedPanel = new WindowInteropHelper(ownerClose.Panel).EnsureHandle();
        var ownedToolbar = new WindowInteropHelper(ownerClose.Toolbar).EnsureHandle();
        ownerClose.Overlay.Close();
        await Dispatcher.Yield(DispatcherPriority.Background);
        Probe("closing the canvas owner finishes all controls without a late close prompt", () => ownerClose.IsClosing && !Native.IsWindow(ownedPanel) && !Native.IsWindow(ownedToolbar));
        File.WriteAllText(Path.Combine(output, "edge-case-result.json"), JsonSerializer.Serialize(results.Select(r => new { name = r.Name, passed = r.Passed, detail = r.Detail }), new JsonSerializerOptions { WriteIndented = true }));
        foreach (var result in results) check(result.Passed, result.Name + (result.Detail.Length == 0 ? "" : " — " + result.Detail));
    }
}

