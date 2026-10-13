using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;

namespace AnshanOverlay;

public sealed record Shortcut(Key Key, ModifierKeys Modifiers = ModifierKeys.None)
{
    public override string ToString() => Key == Key.None ? "未设置" :
        (Modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "") + (Modifiers.HasFlag(ModifierKeys.Alt) ? "Alt+" : "") +
        (Modifiers.HasFlag(ModifierKeys.Shift) ? "Shift+" : "") + (Modifiers.HasFlag(ModifierKeys.Windows) ? "Win+" : "") + Key;
}
public sealed record ShortcutAction(string Id, string Label, bool Global, Shortcut Default);
public sealed class Preferences
{
    public static readonly ShortcutAction[] Actions = {
        new("edit", "编辑 / 鼠标穿透", true, new(Key.E, ModifierKeys.Control | ModifierKeys.Alt)),
        new("topmost", "画布置顶", true, new(Key.T, ModifierKeys.Control | ModifierKeys.Alt)),
        new("visible", "显示 / 隐藏标注", true, new(Key.H, ModifierKeys.Control | ModifierKeys.Alt)),
        new("panel", "唤出面板", true, new(Key.P, ModifierKeys.Control | ModifierKeys.Alt)),
        new("move", "移动", false, new(Key.V)), new("hero", "英雄", false, new(Key.H)),
        new("brush", "画笔", false, new(Key.B)), new("line", "直线", false, new(Key.L)),
        new("arrow", "箭头", false, new(Key.A)), new("circle", "椭圆", false, new(Key.C)),
        new("rectangle", "矩形", false, new(Key.R)), new("eraser", "橡皮擦", false, new(Key.E)),
        new("dash", "全局虚线", false, new(Key.D)), new("names", "英雄名称", false, new(Key.N)),
        new("undo", "撤销", false, new(Key.Z, ModifierKeys.Control)), new("redo", "重做", false, new(Key.Y, ModifierKeys.Control)),
        new("duplicate", "复制选中", false, new(Key.D, ModifierKeys.Control)), new("delete", "删除选中", false, new(Key.Delete)),
        new("clear", "清空画布", false, new(Key.Delete, ModifierKeys.Control | ModifierKeys.Shift)),
        new("cancel", "取消操作 / 取消选择 / 穿透", false, new(Key.Escape)) };
    public Dictionary<string, Shortcut> Shortcuts { get; set; } = Actions.ToDictionary(a => a.Id, a => a.Default);
    public double PanelWidth { get; set; } = 474;
    public double PanelHeight { get; set; } = 900;
    public double ToolbarWidth { get; set; } = 680;
    public double ToolbarHeight { get; set; } = 220;
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnshanOverlay", "settings.json");
    public static string? Validate(Dictionary<string, Shortcut> shortcuts)
    {
        var used = new Dictionary<Shortcut, string>();
        foreach (var action in Actions)
        {
            if (!shortcuts.TryGetValue(action.Id, out var binding) || binding is null || !Enum.IsDefined(binding.Key) || ((int)binding.Modifiers & ~15) != 0)
                return "快捷键格式无效：" + action.Label;
            if (binding.Key == Key.None) continue;
            if (binding.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin || KeyInterop.VirtualKeyFromKey(binding.Key) == 0)
                return "请同时指定一个非修饰键：" + action.Label;
            if (used.TryGetValue(binding, out var other)) return $"快捷键重复：{action.Label} 与 {other}（{binding}）";
            used[binding] = action.Label;
        }
        return null;
    }
    public static Preferences Load()
    {
        if (!File.Exists(FilePath)) return new();
        var value = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath), Assets.Json) ?? new();
        value.Shortcuts ??= new();
        foreach (var action in Actions) value.Shortcuts.TryAdd(action.Id, action.Default);
        if (Validate(value.Shortcuts) is { } error) throw new InvalidDataException(error);
        value.PanelWidth = Size(value.PanelWidth, 340, 2400, 474); value.PanelHeight = Size(value.PanelHeight, 420, 2000, 900);
        value.ToolbarWidth = Size(value.ToolbarWidth, 300, 2400, 680); value.ToolbarHeight = Size(value.ToolbarHeight, 150, 1400, 180);
        return value;
    }
    private static double Size(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this, Assets.Json)); File.Move(FilePath + ".tmp", FilePath, true);
    }
}
