using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NeoChat.Windows.Core.Design;
using Windows.Foundation;
using Windows.UI;

namespace NeoChat.Windows.Design;

/// <summary>
/// Writes <see cref="ThemePalette"/> into the app's resource dictionary and keeps the instances it
/// created, so a theme switch can move every colour in place.
/// <para>
/// In place matters: XAML resolves <c>{StaticResource BrushInk}</c> once and keeps that brush object,
/// so replacing the dictionary entry would leave every loaded element light. Mutating the shared
/// <see cref="SolidColorBrush"/> instead repaints the whole window without rebuilding templates.
/// </para>
/// </summary>
internal static class ThemePainter
{
    private static readonly Dictionary<string, SolidColorBrush> Shades = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Color> Tones = new(StringComparer.Ordinal);
    private static LinearGradientBrush? _pageGradient;
    private static RadialGradientBrush? _glowPrimary;
    private static RadialGradientBrush? _glowSecondary;

    /// <summary>The palette currently painted.</summary>
    internal static AppTheme Theme { get; private set; } = AppTheme.Light;

    /// <summary>The shared brush behind a resource key without the <c>Brush</c> prefix (e.g. <c>Ink</c>).</summary>
    internal static SolidColorBrush Shade(string key) => Shades[key];

    /// <summary>The plain colour behind a resource key without the <c>Color</c> prefix.</summary>
    internal static Color Tone(string key) => Tones[key];

    internal static void Paint(ResourceDictionary dictionary, AppTheme theme)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        Theme = theme;

        var palette = ThemePalette.Resolve(theme);
        foreach (var (key, argb) in palette)
        {
            var color = Argb(argb);
            Tones[key] = color;

            if (Shades.TryGetValue(key, out var brush))
            {
                brush.Color = color;
            }
            else
            {
                Shades[key] = new SolidColorBrush(color);
            }

            dictionary["Color" + key] = color;
            dictionary["Brush" + key] = Shades[key];
        }

        PaintSurfaces(dictionary, palette);
    }

    /// <summary>G15 页面渐变与光晕：两个渐变刷的停靠点跟着令牌走，实例不换。</summary>
    private static void PaintSurfaces(ResourceDictionary dictionary, IReadOnlyDictionary<string, uint> palette)
    {
        if (_pageGradient is null)
        {
            _pageGradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop { Offset = 0 },
                    new GradientStop { Offset = 1 },
                },
            };
            dictionary["BrushPageGradient"] = _pageGradient;
        }

        _pageGradient.GradientStops[0].Color = Argb(palette["MistTop"]);
        _pageGradient.GradientStops[1].Color = Argb(palette["MistBottom"]);

        _glowPrimary ??= Register(dictionary, "BrushGlowPrimary");
        _glowSecondary ??= Register(dictionary, "BrushGlowSecondary");
        PaintGlow(_glowPrimary, palette["GlowPrimaryInner"], palette["GlowPrimaryOuter"]);
        PaintGlow(_glowSecondary, palette["GlowSecondaryInner"], palette["GlowSecondaryOuter"]);
    }

    private static RadialGradientBrush Register(ResourceDictionary dictionary, string key)
    {
        var brush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop { Offset = 0 },
                new GradientStop { Offset = 1 },
            },
        };
        dictionary[key] = brush;
        return brush;
    }

    private static void PaintGlow(RadialGradientBrush brush, uint inner, uint outer)
    {
        brush.GradientStops[0].Color = Argb(inner);
        brush.GradientStops[1].Color = Argb(outer);
    }

    internal static Color Argb(uint value) => Color.FromArgb(
        (byte)((value >> 24) & 0xFF),
        (byte)((value >> 16) & 0xFF),
        (byte)((value >> 8) & 0xFF),
        (byte)(value & 0xFF));
}
