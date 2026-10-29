using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace AnshanOverlay;

public enum HeroTrailStyle { Dashed, Tail }

// Display-only paths. Coordinates come from moved heroes, never from the pointer.
internal sealed class HeroTrails : IDisposable
{
    private sealed record Sample(Point Position, long Time, double Distance);
    private sealed class Trail
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public required string HeroId { get; init; }
        public required string Color { get; init; }
        public double Opacity { get; init; }
        public List<Sample> Points { get; } = new();
    }
    private readonly List<Trail> trails = new();
    private readonly Dictionary<string, Trail> active = new();
    private readonly Func<long> now;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private bool disposed;
    public bool Enabled { get; private set; }
    public HeroTrailStyle Style { get; private set; }
    public int Seconds { get; private set; } = 3;
    internal bool TimerRunning => timer.IsEnabled;
    internal double RefreshMilliseconds => timer.Interval.TotalMilliseconds;
    internal void SetHighFrameRate(bool enabled) => timer.Interval = TimeSpan.FromMilliseconds(enabled ? 1000d / 60 : 1000d / 30);
    public event Action? Changed;

    internal HeroTrails(Func<long>? clock = null)
    {
        now = clock ?? (() => Environment.TickCount64);
        timer.Tick += Tick;
    }
    public void Configure(bool enabled, HeroTrailStyle style, int seconds)
    {
        if (Enabled == enabled && Style == style && Seconds == seconds) return;
        Enabled = enabled; Style = style; Seconds = Math.Clamp(seconds, 1, 10); Clear();
    }
    public void Move(SceneItem previous, SceneItem next)
    {
        if (disposed || !Enabled || next.Kind != "hero" || (next.Start - previous.Start).Length < .01) return;
        long time = now();
        Prune(time);
        if (!active.TryGetValue(next.Id, out var trail))
        {
            if (trails.Count >= SceneStore.MaxItems) return;
            trail = new Trail { HeroId = next.Id, Color = next.HeroBorder, Opacity = next.Opacity };
            trail.Points.Add(new(previous.Start, time, 0));
            active[next.Id] = trail; trails.Add(trail);
        }
        var last = trail.Points[^1];
        int steps = Math.Clamp((int)Math.Ceiling((next.Start - last.Position).Length / 12), 1, 128);
        for (int n = 1; n <= steps; n++)
            trail.Points.Add(new(last.Position + (next.Start - last.Position) * (n / (double)steps),
                last.Time + (long)((time - last.Time) * (n / (double)steps)), last.Distance + (next.Start - last.Position).Length * n / steps));
        // Thin dense paths while retaining their time span and both endpoints.
        if (trail.Points.Count > 512)
        {
            for (int n = trail.Points.Count - 2; n > 0; n -= 2) trail.Points.RemoveAt(n);
            for (int n = 1; n < trail.Points.Count; n++)
                trail.Points[n] = trail.Points[n] with { Distance = trail.Points[n - 1].Distance + (trail.Points[n].Position - trail.Points[n - 1].Position).Length };
        }
        if (!timer.IsEnabled) timer.Start();
    }
    public void Complete() => active.Clear();
    public void Cancel()
    {
        if (active.Count == 0) return;
        var cancelled = active.Values.ToHashSet(); active.Clear();
        trails.RemoveAll(cancelled.Contains); UpdateTimer(); Changed?.Invoke();
    }
    public void RetainHeroes(IReadOnlySet<string> ids)
    {
        int removed = trails.RemoveAll(t => !ids.Contains(t.HeroId));
        foreach (var id in active.Keys.Where(id => !ids.Contains(id)).ToArray()) active.Remove(id);
        if (removed > 0) { UpdateTimer(); Changed?.Invoke(); }
    }
    public SceneItem[] Items
    {
        get
        {
            // Only the timer/movement removes samples, so expiry always refreshes both outputs.
            long time = now();
            var items = new List<SceneItem>();
            foreach (var trail in trails)
            {
                var points = trail.Points;
                if (Style == HeroTrailStyle.Dashed)
                {
                    items.Add(new SceneItem { Id = trail.Id, Kind = "path", IsHeroTrail = true, Color = trail.Color, Width = 4, Dashed = true,
                        Opacity = trail.Opacity * .85, TrailDashOffset = points[0].Distance, Points = points.Select(p => new InkPoint(p.Position.X, p.Position.Y)).ToList() });
                }
                else
                {
                    // One display item per hero, with at most sixteen continuous opacity bands.
                    var bands = new List<HeroTrailBand>();
                    var bandPoints = new List<InkPoint>(); int previousLevel = -1; double strength = 0;
                    for (int n = 1; n < points.Count; n++)
                    {
                        double age = Math.Clamp(1 - (time - points[n].Time) / (Seconds * 1000d), 0, 1);
                        double taper = .15 + .85 * n / (points.Count - 1d);
                        double weight = taper * age; int level = (int)Math.Ceiling(weight * 16);
                        var a = points[n - 1].Position; var b = points[n].Position;
                        if (level != previousLevel)
                        {
                            if (bandPoints.Count > 1) bands.Add(new(Math.Max(1, 9 * strength), .75 * strength, bandPoints));
                            bandPoints = new() { new(a.X, a.Y) }; previousLevel = level;
                        }
                        bandPoints.Add(new(b.X, b.Y)); strength = weight;
                    }
                    if (bandPoints.Count > 1) bands.Add(new(Math.Max(1, 9 * strength), .75 * strength, bandPoints));
                    items.Add(new SceneItem { Id = trail.Id, Kind = "path", IsHeroTrail = true, Color = trail.Color, Width = 9, Opacity = trail.Opacity, TrailBands = bands });
                }
            }
            return items.ToArray();
        }
    }
    private void Tick(object? sender, EventArgs e) => Expire();
    internal void Expire() { Prune(now()); Changed?.Invoke(); }
    private void Prune(long time)
    {
        long cutoff = time - Seconds * 1000;
        foreach (var trail in trails)
        {
            int count = 0;
            while (count + 1 < trail.Points.Count && trail.Points[count + 1].Time <= cutoff) count++;
            if (count > 0) trail.Points.RemoveRange(0, count);
            if (Style == HeroTrailStyle.Dashed && trail.Points.Count > 1 && trail.Points[0].Time < cutoff && trail.Points[1].Time > cutoff)
            {
                var a = trail.Points[0]; var b = trail.Points[1];
                double progress = (cutoff - a.Time) / (double)(b.Time - a.Time);
                trail.Points[0] = new(a.Position + (b.Position - a.Position) * progress, cutoff, a.Distance + (b.Distance - a.Distance) * progress);
            }
        }
        trails.RemoveAll(t => t.Points.Count < 2 || t.Points[^1].Time <= cutoff);
        foreach (var id in active.Keys.Where(id => active[id].Points.Count < 2 || active[id].Points[^1].Time <= cutoff).ToArray()) active.Remove(id);
        UpdateTimer();
    }
    private void UpdateTimer() { if (trails.Count == 0 || disposed) timer.Stop(); }
    public void Clear()
    {
        bool changed = trails.Count > 0;
        trails.Clear(); active.Clear(); timer.Stop();
        if (changed) Changed?.Invoke();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Clear(); timer.Tick -= Tick; Changed = null;
    }
}
