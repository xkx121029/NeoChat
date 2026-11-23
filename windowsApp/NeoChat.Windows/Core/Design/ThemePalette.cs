namespace NeoChat.Windows.Core.Design;

/// <summary>Which half of the palette is painted. Follows the system theme; see <see cref="SystemThemeFollower"/>.</summary>
public enum AppTheme
{
    Light,
    Dark,
}

/// <summary>
/// One design token that exists in both themes. <see cref="Key"/> is the name the view layer uses:
/// the app resource keys are <c>Brush</c> + Key and <c>Color</c> + Key.
/// </summary>
/// <param name="Key">Resource name without prefix, e.g. <c>Ink</c>.</param>
/// <param name="DesignId">ID in <c>docs/ui/DESIGN-SYSTEM.md</c>, light and dark halves together.</param>
/// <param name="Light">0xAARRGGBB for the light palette.</param>
/// <param name="Dark">0xAARRGGBB for the dark palette.</param>
public sealed record ThemeTone(string Key, string DesignId, uint Light, uint Dark);

/// <summary>
/// The light/dark pairs the UI paints from, and the one place a theme is turned into concrete colours.
/// Framework-free so the pairing can be asserted in tests: every key has both halves, no key is
/// missing a dark value, and the values are the ones DESIGN-SYSTEM lists for that ID.
/// </summary>
public static class ThemePalette
{
    /// <summary>
    /// Mean RGB below which the system background counts as dark. 推断值：DESIGN-SYSTEM 没有阈值条目，
    /// 取 8 位灰阶中点 128（纯白 255 / 纯黑 0 的中分）。
    /// </summary>
    public const double DarkLuminanceSplit = 128;

    /// <summary>
    /// Colour tokens in the order they are written to the resource dictionary. Keys match the names
    /// the XAML and the code-behind already use (<c>BrushInk</c>, <c>BrushSeed</c>, …).
    /// </summary>
    public static IReadOnlyList<ThemeTone> Tones { get; } =
    [
        // §1.1 ↔ §1.2 页面与文字
        new("MistTop", "L01 / D01", Tokens.Palette.L01MistTop, Tokens.Palette.D01NightTop),
        new("MistBottom", "L02 / D02", Tokens.Palette.L02MistBottom, Tokens.Palette.D02NightBottom),
        new("Mist", "L03 / D03", Tokens.Palette.L03Mist, Tokens.Palette.D03Night),
        new("MistLowest", "L04 / D04", Tokens.Palette.L04MistLowest, Tokens.Palette.D04NightLowest),
        new("MistLow", "L05 / D05", Tokens.Palette.L05MistLow, Tokens.Palette.D05NightLow),
        new("MistContainer", "L06 / D06", Tokens.Palette.L06MistContainer, Tokens.Palette.D06NightContainer),
        new("MistHigh", "L07 / D07", Tokens.Palette.L07MistHigh, Tokens.Palette.D07NightHigh),
        new("MistHighest", "L08 / D08", Tokens.Palette.L08MistHighest, Tokens.Palette.D08NightHighest),
        new("Ink", "L09 / D09", Tokens.Palette.L09Ink, Tokens.Palette.D09Paper),
        new("InkMuted", "L10 / D10", Tokens.Palette.L10InkMuted, Tokens.Palette.D10PaperMuted),
        new("InkFaint", "L11 / D11", Tokens.Palette.L11InkFaint, Tokens.Palette.D11PaperFaint),
        new("InkGhost", "L12 / D12", Tokens.Palette.L12InkGhost, Tokens.Palette.D12PaperGhost),
        new("OutlineLight", "L13 / D13", Tokens.Palette.L13OutlineLight, Tokens.Palette.D13OutlineDark),
        new("RuleLight", "L14 / D14", Tokens.Palette.L14RuleLight, Tokens.Palette.D14RuleDark),
        new("ShadowInk", "L15 / D15", Tokens.Palette.L15ShadowInk, Tokens.Palette.D15ShadowNight),

        // §1.3 语义色
        new("TextTertiary", "M01", Tokens.Palette.M01TextTertiary, Tokens.Palette.M01TextTertiaryDark),
        new("TextDisabled", "M02", Tokens.Palette.M02TextDisabled, Tokens.Palette.M02TextDisabledDark),
        new("Success", "M03", Tokens.Palette.M03Success, Tokens.Palette.M03SuccessDark),
        new("Warning", "M05", Tokens.Palette.M05Warning, Tokens.Palette.M05WarningDark),
        new("Info", "M09", Tokens.Palette.M09Info, Tokens.Palette.M09InfoDark),
        new("Slate", "S01", Tokens.Palette.S01Slate, Tokens.Palette.S01SlateDark),
        new("SlateContainer", "S02", Tokens.Palette.S02SlateContainer, Tokens.Palette.S02SlateContainerDark),
        new("Danger", "S07 / M07", Tokens.Palette.S07Danger, Tokens.Palette.S07DangerDark),
        new("OnDanger", "S08", Tokens.Palette.S08OnDanger, Tokens.Palette.S08OnDangerDark),
        new("DangerContainer", "S09 / M08", Tokens.Palette.S09DangerContainer, Tokens.Palette.S09DangerContainerDark),
        new("OnDangerContainer", "S10", Tokens.Palette.S10OnDangerContainer, Tokens.Palette.S10OnDangerContainerDark),
        new("CodeWellBg", "M13", Tokens.Palette.M13CodeWellBg, Tokens.Palette.M13CodeWellBgDark),
        new("CodeWellHeader", "M14", Tokens.Palette.M14CodeWellHeader, Tokens.Palette.M14CodeWellHeaderDark),
        new("CodeWellFg", "M15", Tokens.Palette.M15CodeWellFg, Tokens.Palette.M15CodeWellFgDark),
        new("CodeWellBorder", "M16", Tokens.Palette.M16CodeWellBorder, Tokens.Palette.M16CodeWellBorderDark),
        new("LineNumberFg", "M17", Tokens.Palette.M17LineNumberFg, Tokens.Palette.M17LineNumberFgDark),

        // §1.4 强调色（mist 默认套）
        new("Seed", "A01", Tokens.Palette.A01Seed, Tokens.Palette.A01SeedDark),
        new("SeedPressed", "A02", Tokens.Palette.A02Pressed, Tokens.Palette.A02PressedDark),
        new("SeedContainer", "A03", Tokens.Palette.A03Container, Tokens.Palette.A03ContainerDark),
        new("OnSeedContainer", "A04", Tokens.Palette.A04OnContainer, Tokens.Palette.A04OnContainerDark),
        new("OnSeed", "A05", Tokens.Palette.A05OnSeed, Tokens.Palette.A05OnSeedDark),
        new("Bubble", "A06 / G17", Tokens.Palette.A06Bubble, Tokens.Palette.A06BubbleDark),
        new("GlowPrimary", "A07", Tokens.Palette.A07Glow, Tokens.Palette.A07GlowDark),
        new("GlowSecondary", "A08", Tokens.Palette.A08Haze, Tokens.Palette.A08HazeDark),

        // §1.5 玻璃（G18：blurEnabled=false，全部走 fill / fallbackFill）
        new("ChromeFill", "G01", Tokens.Palette.G01ChromeFill, Tokens.Palette.G01ChromeFillDark),
        new("ChromeFallback", "G02", Tokens.Palette.G02ChromeFallbackFill, Tokens.Palette.G02ChromeFallbackFillDark),
        new("ChromeHairline", "G03", Tokens.Palette.G03ChromeHairline, Tokens.Palette.G03ChromeHairlineDark),
        new("ChromeRim", "G04", Tokens.Palette.G04ChromeRim, Tokens.Palette.G04ChromeRimDark),
        new("CardFill", "G11", Tokens.Palette.G11CardFill, Tokens.Palette.G11CardFillDark),
        new("CardHairline", "G12", Tokens.Palette.G12CardHairline, Tokens.Palette.G12CardHairlineDark),
        new("CardRim", "G12 rim", Tokens.Palette.G12CardRim, Tokens.Palette.G12CardRimDark),
        new("PillFill", "G13", Tokens.Palette.G13PillFill, Tokens.Palette.G13PillFillDark),
        new("PillHairline", "G14", Tokens.Palette.G14PillHairline, Tokens.Palette.G14PillHairlineDark),
        new("Scrim", "G16", Tokens.Palette.G16Scrim, Tokens.Palette.G16ScrimDark),
    ];

    /// <summary>The colour for one key in one theme; throws rather than returning a wrong default.</summary>
    public static uint Value(string key, AppTheme theme)
    {
        foreach (var tone in Tones)
        {
            if (string.Equals(tone.Key, key, StringComparison.Ordinal))
            {
                return theme == AppTheme.Dark ? tone.Dark : tone.Light;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(key), key, "no theme tone with that key");
    }

    /// <summary>
    /// Every colour the app paints, including the ones derived by mixing a token with an alpha from
    /// §1.5 / §1.9 / §2.2. Derived keys never carry their own hex: they are computed from the tone, so a
    /// theme switch cannot leave half of a surface behind.
    /// </summary>
    public static IReadOnlyDictionary<string, uint> Resolve(AppTheme theme)
    {
        var dark = theme == AppTheme.Dark;
        var colors = new Dictionary<string, uint>(Tones.Count + 8, StringComparer.Ordinal);
        foreach (var tone in Tones)
        {
            colors[tone.Key] = dark ? tone.Dark : tone.Light;
        }

        // G15 页面光晕：glowPrimary α0.42 / glowSecondary α0.38（暗色 0.28 / 0.24）。
        var glowPrimaryAlpha = dark ? Tokens.Theme.GlowPrimaryAlphaDark : Tokens.Layout.GlowPrimaryAlpha;
        var glowSecondaryAlpha = dark ? Tokens.Theme.GlowSecondaryAlphaDark : Tokens.Layout.GlowSecondaryAlpha;
        colors["GlowPrimaryInner"] = WithAlpha(colors["GlowPrimary"], glowPrimaryAlpha);
        colors["GlowPrimaryOuter"] = WithAlpha(colors["GlowPrimary"], 0);
        colors["GlowSecondaryInner"] = WithAlpha(colors["GlowSecondary"], glowSecondaryAlpha);
        colors["GlowSecondaryOuter"] = WithAlpha(colors["GlowSecondary"], 0);

        // ST27 错误横幅：dangerContainer α0.92 铺底，accent α0.3 描边（B03）。
        colors["ErrorBannerFill"] = WithAlpha(colors["DangerContainer"], Tokens.Border.ErrorBannerFillAlpha);
        colors["ErrorBannerBorder"] = WithAlpha(colors["Seed"], Tokens.Border.ErrorBannerAlpha);

        // Y43/Y44 发送键禁用态：onSurface 12% 底 / 38% 前景；暗色的 onSurface 就是 paper。
        colors["SendDisabled"] = WithAlpha(colors["Ink"], Tokens.Layout.SendDisabledAlpha);
        colors["SendForegroundDisabled"] = WithAlpha(colors["Ink"], Tokens.Layout.SendForegroundDisabledAlpha);

        return colors;
    }

    /// <summary>Scales the alpha channel of <paramref name="argb"/> by <paramref name="alpha"/>.</summary>
    public static uint WithAlpha(uint argb, double alpha)
    {
        var source = (argb >> 24) & 0xFF;
        var scaled = (uint)Math.Round(source * alpha, MidpointRounding.AwayFromZero) & 0xFF;
        return (scaled << 24) | (argb & 0x00FFFFFF);
    }
}
