using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace AnshanOverlay;

internal static class Annotation
{
    public const int MaxLength = 500;
    public const int MaxLines = 12;
    private static readonly ConditionalWeakTable<SceneItem, LabelMetrics> layouts = new();
    public static bool IsLabel(SceneItem item) => item.Kind is "text" or "number";
    public static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ");
    public static bool ValidText(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= MaxLength &&
        Normalize(text).Count(c => c == '\n') < MaxLines && !text.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t');
    private static FormattedText Measure(string text, double size) => new(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
        new Typeface("Microsoft YaHei UI"), size, Brushes.White, 1);
    public static FormattedText Format(SceneItem item)
    {
        var layout = Metrics(item);
        var text = Measure(layout.Lines is null ? item.Text : string.Join("\n", layout.Lines), item.FontSize);
        if (layout.LineHeight > 0) text.LineHeight = layout.LineHeight;
        return text;
    }
    public static Rect Bounds(SceneItem item)
    {
        var layout = Metrics(item);
        return new Rect(item.X, item.Y, layout.Width, layout.Height);
    }
    public static LabelMetrics Metrics(SceneItem item) => layouts.GetValue(item, Layout);
    private static LabelMetrics Layout(SceneItem item)
    {
        var lines = new List<string>();
        foreach (string paragraph in Normalize(item.Text).Split('\n'))
        {
            string remaining = paragraph;
            while (item.TextWidth > 0 && Measure(remaining, item.FontSize).WidthIncludingTrailingWhitespace > item.TextWidth)
            {
                int[] starts = StringInfo.ParseCombiningCharacters(remaining);
                int low = 1, high = starts.Length;
                while (low < high)
                {
                    int mid = (low + high + 1) / 2, end = mid == starts.Length ? remaining.Length : starts[mid];
                    if (Measure(remaining[..end], item.FontSize).WidthIncludingTrailingWhitespace <= item.TextWidth) low = mid; else high = mid - 1;
                }
                int split = low == starts.Length ? remaining.Length : starts[low];
                int space = remaining.LastIndexOf(' ', Math.Max(0, split - 1), split);
                if (space > 0 && split < remaining.Length) split = space + 1;
                lines.Add(remaining[..split]); remaining = remaining[split..];
            }
            lines.Add(remaining);
        }
        double lineHeight = item.Kind == "text" ? item.FontSize * 1.45 : 0;
        var text = Measure(string.Join("\n", lines), item.FontSize);
        if (lineHeight > 0) text.LineHeight = lineHeight;
        double width = text.WidthIncludingTrailingWhitespace + 20, height = text.Height + 12;
        if (item.Kind == "number") width = height = Math.Max(width, height);
        return new LabelMetrics(width, height, (height - text.Height) / 2 + text.Baseline, item.Kind == "text" ? lines.ToArray() : null, lineHeight);
    }
    public static SceneItem Fit(SceneItem item, Scene scene)
    {
        if (item.TextWidth > 0) item = item with { TextWidth = Math.Min(item.TextWidth, scene.Width - 20) };
        var bounds = Bounds(item);
        for (int n = 0; n < 6 && (bounds.Width > scene.Width || bounds.Height > scene.Height); n++)
        {
            double scale = Math.Min(1, Math.Min((scene.Width - 20) / (bounds.Width - 20), (scene.Height - 12) / (bounds.Height - 12)));
            item = item with { FontSize = Math.Clamp(item.FontSize * scale, 8, 96) };
            bounds = Bounds(item);
        }
        return item with { X = Math.Clamp(item.X, 0, Math.Max(0, scene.Width - bounds.Width)), Y = Math.Clamp(item.Y, 0, Math.Max(0, scene.Height - bounds.Height)) };
    }
}
