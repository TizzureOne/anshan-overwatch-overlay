using System;
using System.Text.RegularExpressions;

namespace AnshanOverlay;

public enum HeroSelectionShape { Corners, Ring, Crosshair, None }

public sealed record SelectionAppearance
{
    public HeroSelectionShape Shape { get; init; } = HeroSelectionShape.Corners;
    public string Color { get; init; } = "#7DE2D1";
    public double Thickness { get; init; } = 2;
    public double Gap { get; init; } = 6;
    public double Length { get; init; } = 10;
    public double Opacity { get; init; } = .9;
    public bool Outline { get; init; } = true;

    public static bool ValidColor(string? value) => value is not null && Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$");
    public SelectionAppearance Normalize() => this with
    {
        Shape = Enum.IsDefined(Shape) ? Shape : HeroSelectionShape.Corners,
        Color = ValidColor(Color) ? Color.ToUpperInvariant() : "#7DE2D1",
        Thickness = Clamp(Thickness, 1, 6, 2), Gap = Clamp(Gap, 0, 24, 6),
        Length = Clamp(Length, 4, 24, 10), Opacity = Clamp(Opacity, 0, 1, .9)
    };
    private static double Clamp(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
