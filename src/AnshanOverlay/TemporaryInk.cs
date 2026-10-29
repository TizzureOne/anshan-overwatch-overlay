using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace AnshanOverlay;

// Session-only ink: neither the scene store nor its undo history owns these items.
internal sealed class TemporaryInk : IDisposable
{
    internal const int LifetimeMilliseconds = 5000;
    private sealed record Stroke(SceneItem Item, long ExpiresAt);
    private readonly List<Stroke> strokes = new();
    private readonly List<Stroke> redo = new();
    private readonly Func<long> now;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private SceneItem? active;
    private bool disposed;
    public event Action? Changed;
    public bool IsDrawing => active is not null;
    public bool CanUndo => strokes.Count > 0;
    public bool CanRedo => redo.Count > 0;
    internal bool TimerRunning => timer.IsEnabled;
    public SceneItem[] Items => active is null ? strokes.Select(s => s.Item).ToArray() : strokes.Select(s => s.Item).Append(active).ToArray();

    internal TemporaryInk(Func<long>? clock = null)
    {
        now = clock ?? (() => Environment.TickCount64);
        timer.Tick += Tick;
    }
    public void Begin(Point point, string color, double width, bool dashed)
    {
        if (disposed) return;
        Expire();
        if (strokes.Count >= SceneStore.MaxItems) return;
        active = new SceneItem { Kind = "path", X = point.X, Y = point.Y, X2 = point.X, Y2 = point.Y,
            Color = color, Width = width, Dashed = dashed, Points = new() { new(point.X, point.Y) } };
        Changed?.Invoke();
    }
    public void Move(Point point)
    {
        if (active is null) return;
        var last = active.Points[^1];
        if (new Vector(point.X - last.X, point.Y - last.Y).Length < 2 || active.Points.Count >= 20000) return;
        active = active with { X2 = point.X, Y2 = point.Y, Points = active.Points.Append(new InkPoint(point.X, point.Y)).ToList() };
        Changed?.Invoke();
    }
    public void Complete()
    {
        if (active is null) return;
        if (active.Points.Count >= 2) { strokes.Add(new(active, now() + LifetimeMilliseconds)); redo.Clear(); }
        active = null; UpdateTimer(); Changed?.Invoke();
    }
    public void Cancel()
    {
        if (active is null) return;
        active = null; Changed?.Invoke();
    }
    public void Undo()
    {
        Expire(); if (!CanUndo) return;
        redo.Add(strokes[^1]); strokes.RemoveAt(strokes.Count - 1); Changed?.Invoke();
    }
    public void Redo()
    {
        Expire(); if (!CanRedo) return;
        strokes.Add(redo[^1]); redo.RemoveAt(redo.Count - 1); Changed?.Invoke();
    }
    public void ClearRedo() { redo.Clear(); UpdateTimer(); }
    private void Tick(object? sender, EventArgs e) => Expire();
    internal void Expire()
    {
        long time = now();
        int removed = strokes.RemoveAll(s => s.ExpiresAt <= time) + redo.RemoveAll(s => s.ExpiresAt <= time);
        UpdateTimer(); if (removed > 0) Changed?.Invoke();
    }
    private void UpdateTimer()
    {
        if (!disposed && strokes.Count + redo.Count > 0) { if (!timer.IsEnabled) timer.Start(); }
        else timer.Stop();
    }
    public void Clear()
    {
        bool changed = active is not null || strokes.Count + redo.Count > 0;
        active = null; strokes.Clear(); redo.Clear(); timer.Stop();
        if (changed) Changed?.Invoke();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Clear(); timer.Tick -= Tick; Changed = null;
    }
}
