using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AnshanOverlay;

public sealed class Controller
{
    public HeroManifest Heroes { get; } = Assets.LoadHeroes();
    public SceneStore Store { get; } = new();
    public Editor Editor { get; }
    public Preferences Settings { get; private set; } = new();
    private readonly bool testMode;
    private bool hotkeyHookAdded;
    private bool shortcutsDialogOpen;
    private Scene? propertyBefore;
    private System.Drawing.Icon? trayIcon;
    public ObsServer Server { get; }
    public OverlayWindow Overlay { get; }
    public MainWindow Panel { get; }
    public ToolbarWindow Toolbar { get; }
    public bool PassThrough { get; private set; } = true;
    public bool AlwaysOnTop { get; private set; } = true;
    public bool IsClosing { get; private set; }
    public string Status { get; private set; } = "选择英雄，在画面上点击放置。";
    public string HotkeyWarning { get; private set; } = "";
    public string ModeState => PassThrough ? "当前：鼠标穿透" : "当前：编辑中 · 画布接收鼠标";
    public string ModeShortcut => Settings.Shortcuts["edit"].Key == Key.None ? "点击模式按钮切换" : BindingText("edit") + " 切换";
    public List<DisplayInfo> Screens { get; } = Native.Displays();
    public DisplayInfo Screen { get; private set; }
    public event Action? Changed;
    private readonly DispatcherTimer publishTimer;
    private readonly DispatcherTimer saveTimer;
    private readonly List<int> hotkeys = new();
    private readonly string autosave;
    private IntPtr previousWindow;
    private System.Windows.Forms.NotifyIcon? tray;

    public Controller(bool test = false)
    {
        testMode = test;
        if (!test) try { Settings = Preferences.Load(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { Status = "设置无法读取，已使用默认快捷键和尺寸。"; }
        Motion.Enabled = !test && Settings.AnimateInterface;
        Editor = new Editor(Store); Server = new ObsServer(Heroes);
        Screen = Screens.FirstOrDefault(s => s.Primary) ?? Screens[0];
        autosave = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnshanOverlay", "autosave.json");
        if (!test && File.Exists(autosave))
        {
            try { Store.Preview(SceneStore.Parse(File.ReadAllText(autosave), Heroes.Heroes)); }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException)
            { Status = "上次场景无法读取，已新建空白画布。原文件尚未改动。"; }
        }
        Store.Preview(SceneStore.Scale(Store.Current, Screen.Width, Screen.Height));
        // Recover in visible, click-through mode so a saved hidden state cannot
        // leave users drawing on an invisible canvas after restart.
        Store.Preview(Store.Current with { Visible = true });
        Overlay = new OverlayWindow(this); Panel = new MainWindow(this); Toolbar = new ToolbarWindow(this);
        publishTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        publishTimer.Tick += (_, _) => { publishTimer.Stop(); Server.Publish(Store.Serialize()); };
        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); if (!test) SaveAuto(); };
        Store.Changed += () =>
        {
            Overlay.Refresh();
            if (!publishTimer.IsEnabled) publishTimer.Start();
            saveTimer.Stop(); saveTimer.Start(); Notify();
        };
        Panel.SourceInitialized += (_, _) => { if (!test) RegisterHotkeys(); };
    }
    public async Task StartAsync()
    {
        try { await Server.StartAsync(); Server.Publish(Store.Serialize()); }
        catch (Exception ex) { Status = "OBS 服务未能启动：" + ex.Message; }
        Overlay.Show(); Overlay.Place(Screen); Overlay.Refresh(); Panel.Show();
        Toolbar.Left = SystemParameters.WorkArea.Left + Math.Max(0, (SystemParameters.WorkArea.Width - Toolbar.Width) / 2);
        Toolbar.Top = SystemParameters.WorkArea.Bottom - Toolbar.Height - 16; Toolbar.Show();
        CreateTray(); Notify(); Panel.StartUpdateChecks();
    }
    public void Notify()
    {
        if (tray is not null) tray.Text = "安山守望 · " + (PassThrough ? "鼠标穿透" : "编辑中") + " · " + BindingText("edit");
        Changed?.Invoke();
    }
    public void SetStatus(string text) { Status = text; Notify(); }
    public void TogglePassThrough() => SetPassThrough(!PassThrough);
    public void SetPassThrough(bool enabled)
    {
        if (!enabled)
        {
            var foreground = Native.GetForegroundWindow();
            if (foreground != new WindowInteropHelper(Overlay).Handle && foreground != new WindowInteropHelper(Panel).Handle && foreground != new WindowInteropHelper(Toolbar).Handle)
                previousWindow = foreground;
            if (!Store.Current.Visible) Store.Edit(s => s with { Visible = true });
        }
        if (enabled) Panel.CancelHeroPreview();
        PassThrough = enabled; Overlay.SetPassThrough(enabled);
        if (enabled && previousWindow != IntPtr.Zero && Native.IsWindow(previousWindow)) Native.SetForegroundWindow(previousWindow);
        if (!enabled && !testMode) { Overlay.Activate(); RaisePanel(); }
        SetStatus(enabled ? $"鼠标穿透中 · {BindingText("edit")} 恢复编辑。" : $"编辑中 · 拖动头像或绘制；{BindingText("edit")} 返回游戏。");
    }
    public void ToggleTopmost()
    {
        AlwaysOnTop = !AlwaysOnTop; Overlay.Topmost = AlwaysOnTop;
        SetStatus(AlwaysOnTop ? "画布已置顶。" : "画布已取消置顶，可能被其他窗口遮住。");
    }
    public void ToggleVisible()
    {
        Editor.Cancel(); Store.Edit(s => s with { Visible = !s.Visible });
        if (!Store.Current.Visible) SetPassThrough(true);
        SetStatus(Store.Current.Visible ? "标注已显示（桌面与 OBS 同步）。" : "标注已隐藏（桌面与 OBS 同步），内容保留。");
    }
    public void SetTool(EditorTool tool)
    {
        Editor.Cancel(); Editor.Tool = tool;
        SetPassThrough(false); Overlay.Refresh(); Notify();
    }
    public void ChooseHero(Hero hero)
    {
        Editor.PendingHero = hero; SetTool(EditorTool.Hero);
        SetStatus($"已选择 {hero.Name} · 在画面上点击放置，放置后可直接拖动。");
    }
    public void SetHeroTeam(string team)
    {
        if (Editor.Team == team) return;
        Editor.Team = team; Notify();
    }
    public void AddHeroes(IReadOnlyList<Hero> heroes)
    {
        if (heroes.Count == 0) return;
        Editor.Cancel();
        double size = 0; int columns = 1;
        for (int n = heroes.Count; n >= 1; n--)
        {
            int rows = (heroes.Count + n - 1) / n;
            double candidate = Math.Min(Editor.Size, Math.Min((Store.Current.Width - 32) / (n * 1.15), (Store.Current.Height - 32) / (rows * 1.15)));
            if (candidate > size) { size = candidate; columns = n; }
            if (size == Editor.Size) break;
        }
        double step = size * 1.15;
        int rowCount = (heroes.Count + columns - 1) / columns;
        double top = Editor.Team == "ally" ? 16 : Store.Current.Height - 16 - rowCount * step;
        var added = heroes.Select((hero, index) => new SceneItem { HeroId = hero.Id, Team = Editor.Team, Size = size,
            X = 16 + index % columns * step + step / 2, Y = top + index / columns * step + step / 2 }).ToList();
        Editor.Select(added.Select(i => i.Id)); Editor.PendingHero = null;
        Store.Edit(s => s with { Visible = true, Items = s.Items.Concat(added).ToList() });
        SetTool(EditorTool.Move);
        SetStatus($"已添加 {added.Count} 位{(Editor.Team == "ally" ? "蓝方" : "红方")}英雄，可拖动任一头像整体移动。");
    }
    public void ChangeScreen(DisplayInfo screen)
    {
        Editor.Cancel(); Screen = screen; Store.Resize(screen.Width, screen.Height);
        if (Overlay.IsLoaded) Overlay.Place(screen);
        SetStatus($"画布 {screen.Width} × {screen.Height} · 请同步修改 OBS 浏览器源尺寸。");
    }
    public void Undo() { Editor.Cancel(); Store.Undo(); Overlay.Refresh(); }
    public void Redo() { Editor.Cancel(); Store.Redo(); Overlay.Refresh(); }
    public void Clear(bool linesOnly) { Editor.Cancel(); Store.Clear(linesOnly); }
    public void Delete() => Editor.Delete();
    public void CancelOrDeselect()
    {
        if (Editor.IsDragging) { Editor.Cancel(); Overlay.Board.ReleaseMouseCapture(); }
        else if (Editor.SelectedIds.Count > 0) Editor.SelectedId = null;
        else { SetPassThrough(true); return; }
        Overlay.Refresh(); Notify();
    }
    public void SetHeroNames(bool show)
    {
        Editor.Cancel(); Store.Edit(s => s with { ShowHeroNames = show });
    }
    public void SetDashed(bool dashed)
    {
        Editor.Cancel(); Editor.Dashed = dashed; Notify();
    }
    public void ShowPanel() { Panel.SetFolded(false); Panel.Show(); Panel.WindowState = WindowState.Normal; Panel.Activate(); if (!Toolbar.IsVisible) { Toolbar.SetFolded(false); Toolbar.Show(); } }
    public void UpdateHero(string id, Func<SceneItem, SceneItem> change)
    {
        Editor.Cancel(); Store.Edit(s => s with { Items = s.Items.Select(i => i.Id == id && i.Kind == "hero" ? change(i) : i).ToList() });
    }
    public string? ApplyUpdatePreferences(bool automatic, bool beta, bool animations)
    {
        var before = (Settings.AutomaticallyCheckUpdates, Settings.IncludeBetaUpdates, Settings.AnimateInterface);
        Settings.AutomaticallyCheckUpdates = automatic; Settings.IncludeBetaUpdates = beta; Settings.AnimateInterface = animations;
        try { if (!testMode) Settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            (Settings.AutomaticallyCheckUpdates, Settings.IncludeBetaUpdates, Settings.AnimateInterface) = before;
            return "设置未能保存，请检查文件权限后重试。";
        }
        Motion.Enabled = !testMode && animations; return null;
    }
    public void PrepareForUpdate()
    {
        Editor.Cancel();
        if (testMode) return;
        WriteScene(autosave); SaveWindowSettings();
    }
    private void SaveWindowSettings()
    {
        Settings.PanelWidth = double.IsFinite(Panel.RestoreBounds.Width) ? Panel.RestoreBounds.Width : Panel.Width; Settings.PanelHeight = Panel.ExpandedHeight;
        Settings.ToolbarWidth = Toolbar.Width; Settings.ToolbarHeight = Toolbar.ExpandedHeight; Settings.Save();
    }
    public void RaisePanel()
    {
        // Editing can activate the full-screen overlay. Keep its separate controls
        // above it without stealing keyboard focus from the drawing canvas.
        if (Panel.IsVisible && Panel.WindowState != WindowState.Minimized)
            Native.SetWindowPos(new WindowInteropHelper(Panel).Handle, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
        if (Toolbar is not null && Toolbar.IsVisible)
            Native.SetWindowPos(new WindowInteropHelper(Toolbar).Handle, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
    }
    public string BindingText(string id) => Settings.Shortcuts[id].ToString();
    public void HandleEditorKey(KeyEventArgs e)
    {
        if (e.IsRepeat) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var action = Preferences.Actions.FirstOrDefault(a => !a.Global && Settings.Shortcuts[a.Id] == new Shortcut(key, Keyboard.Modifiers));
        if (action is null) return;
        RunAction(action.Id); e.Handled = true;
    }
    private void RunAction(string id)
    {
        switch (id)
        {
            case "edit": TogglePassThrough(); break; case "topmost": ToggleTopmost(); break;
            case "visible": ToggleVisible(); break; case "panel": ShowPanel(); break;
            case "move": SetTool(EditorTool.Move); break;
            case "hero": SetTool(EditorTool.Hero); ShowPanel(); break;
            case "brush": SetTool(EditorTool.Brush); break; case "line": SetTool(EditorTool.Line); break;
            case "arrow": SetTool(EditorTool.Arrow); break; case "circle": SetTool(EditorTool.Circle); break;
            case "rectangle": SetTool(EditorTool.Rectangle); break; case "eraser": SetTool(EditorTool.Eraser); break;
            case "dash": SetDashed(!Editor.Dashed); break; case "names": SetHeroNames(!Store.Current.ShowHeroNames); break;
            case "undo": Undo(); break; case "redo": Redo(); break;
            case "duplicate": Editor.Cancel(); Editor.Duplicate(); Overlay.Refresh(); Notify(); break;
            case "delete": Delete(); break; case "clear": Clear(false); break;
            case "cancel": CancelOrDeselect(); break;
        }
    }
    private string RegisterHotkeys(Dictionary<string, Shortcut>? bindings = null)
    {
        var hwnd = new WindowInteropHelper(Panel).Handle;
        foreach (int old in hotkeys) Native.UnregisterHotKey(hwnd, old); hotkeys.Clear();
        var failed = new List<string>(); bindings ??= Settings.Shortcuts;
        for (int n = 0; n < 4; n++)
        {
            var action = Preferences.Actions[n]; var binding = bindings[action.Id];
            if (binding.Key == Key.None) continue;
            if (Native.RegisterHotKey(hwnd, n + 1, 0x4000 | (uint)binding.Modifiers, (uint)KeyInterop.VirtualKeyFromKey(binding.Key))) hotkeys.Add(n + 1);
            else failed.Add(action.Label + " " + binding);
        }
        if (!hotkeyHookAdded) { HwndSource.FromHwnd(hwnd)?.AddHook(HotkeyHook); hotkeyHookAdded = true; }
        HotkeyWarning = failed.Count == 0 ? "" : "快捷键被占用或不可用：" + string.Join("、", failed);
        return HotkeyWarning;
    }
    public string? ApplyShortcuts(Dictionary<string, Shortcut> bindings)
    {
        if (Preferences.Validate(bindings) is { } error) return error;
        if (!testMode && RegisterHotkeys(bindings) is { Length: > 0 } conflict) { RegisterHotkeys(); if (shortcutsDialogOpen) SuspendHotkeys(); return conflict; }
        var old = Settings.Shortcuts; Settings.Shortcuts = new(bindings);
        try { if (!testMode) Settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Settings.Shortcuts = old; if (!testMode) RegisterHotkeys(); if (shortcutsDialogOpen) SuspendHotkeys(); return "保存失败：" + ex.Message; }
        if (shortcutsDialogOpen) SuspendHotkeys();
        Overlay.Refresh(); Notify(); return null;
    }
    private void SuspendHotkeys()
    {
        var hwnd = new WindowInteropHelper(Panel).Handle;
        foreach (int id in hotkeys) Native.UnregisterHotKey(hwnd, id); hotkeys.Clear();
    }
    public void ShowShortcuts()
    {
        shortcutsDialogOpen = true; SuspendHotkeys();
        try { new ShortcutWindow(this).ShowDialog(); }
        finally { shortcutsDialogOpen = false; if (!testMode) RegisterHotkeys(); Notify(); }
    }
    public void BeginPropertyEdit() { Editor.Cancel(); propertyBefore ??= Store.Current; }
    public void UpdateSelected(Func<SceneItem, SceneItem> change)
    {
        if (Editor.Selected is not { } selected) return;
        var next = change(selected);
        if (propertyBefore is null) Store.Edit(s => s with { Items = s.Items.Select(i => i.Id == selected.Id ? next : i).ToList() });
        else Store.Replace(next);
    }
    public void EndPropertyEdit() { if (propertyBefore is not { } before) return; propertyBefore = null; Store.Commit(before); }
    public void PlaceHero(Hero hero, Point position)
    {
        Editor.Cancel();
        var item = new SceneItem { HeroId = hero.Id, X = Math.Clamp(position.X, 0, Store.Current.Width), Y = Math.Clamp(position.Y, 0, Store.Current.Height), Size = Editor.Size, Team = Editor.Team };
        Editor.SelectedId = item.Id;
        Store.Edit(s => s with { Visible = true, Items = s.Items.Append(item).ToList() });
        SetStatus($"已放置 {hero.Name}，可直接拖动。");
    }
    private IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        // WM_ACTIVATE supplies the previously active HWND when the panel is
        // activated. Remember it so a toolbar-driven edit can return focus too.
        if (msg == 0x0006 && (w.ToInt64() & 0xFFFF) != 0 && l != IntPtr.Zero &&
            l != new WindowInteropHelper(Overlay).Handle && l != new WindowInteropHelper(Toolbar).Handle && l != hwnd)
            previousWindow = l;
        if (msg != 0x0312) return IntPtr.Zero;
        int index = w.ToInt32() - 1; if (index >= 0 && index < 4) RunAction(Preferences.Actions[index].Id);
        handled = true; return IntPtr.Zero;
    }
    private void CreateTray()
    {
        using var iconStream = new MemoryStream(Assets.Read("Assets/app.ico"));
        trayIcon = new System.Drawing.Icon(iconStream);
        tray = new System.Windows.Forms.NotifyIcon { Text = "安山守望 · 教学标注", Icon = trayIcon, Visible = true };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("显示控制面板", null, (_, _) => ShowPanel());
        menu.Items.Add("编辑 / 鼠标穿透", null, (_, _) => TogglePassThrough());
        menu.Items.Add("恢复鼠标穿透", null, (_, _) => SetPassThrough(true));
        menu.Items.Add("显示 / 隐藏标注", null, (_, _) => ToggleVisible());
        menu.Items.Add("退出", null, (_, _) => Close()); tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowPanel();
    }
    private void SaveAuto()
    {
        try { WriteScene(autosave); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus("自动保存失败：" + ex.Message); }
    }
    public void WriteScene(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp"; File.WriteAllText(temp, Store.Serialize()); File.Move(temp, path, true);
    }
    public void LoadScene(string path)
    {
        var loaded = SceneStore.Parse(File.ReadAllText(path), Heroes.Heroes);
        Editor.Cancel(); Store.Edit(_ => SceneStore.Scale(loaded, Screen.Width, Screen.Height));
        if (!Store.Current.Visible) SetPassThrough(true);
        SetStatus("已加载场景，并按当前屏幕等比适配。");
    }
    public async void Close()
    {
        if (IsClosing) return; IsClosing = true;
        Panel.StopUpdateChecks(); Panel.CancelHeroPreview();
        Editor.Cancel(); publishTimer.Stop(); saveTimer.Stop(); if (!testMode) SaveAuto();
        var hwnd = new WindowInteropHelper(Panel).Handle;
        foreach (var id in hotkeys) Native.UnregisterHotKey(hwnd, id);
        if (!testMode) try { SaveWindowSettings(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine(ex); }
        tray?.Dispose(); trayIcon?.Dispose(); Toolbar.Close(); Overlay.Close(); Panel.Close();
        await Server.DisposeAsync(); Application.Current.Shutdown();
    }
}
