using NeoChat.Windows.Core.Design;
using Xunit;

namespace NeoChat.Windows.Core.Tests;

/// <summary>
/// The dark theme is a second column of the same tokens, not a second palette invented at the view:
/// every key must carry both halves, the dark half must be the value DESIGN-SYSTEM lists for that ID,
/// and following the system must report one switch per flip.
/// </summary>
public sealed class ThemePaletteTests
{
    [Fact]
    public void EveryToneCarriesBothHalvesAndTheyDiffer()
    {
        foreach (var tone in ThemePalette.Tones)
        {
            Assert.NotEqual(tone.Light, tone.Dark);
            Assert.NotEqual(0u, tone.Light >> 24);
            Assert.NotEqual(0u, tone.Dark >> 24);
            Assert.False(string.IsNullOrWhiteSpace(tone.DesignId), tone.Key + " 没有 DESIGN-SYSTEM 条目 ID");
        }
    }

    [Fact]
    public void ResolvedDictionariesCarryTheSameKeysInBothThemes()
    {
        var light = ThemePalette.Resolve(AppTheme.Light);
        var dark = ThemePalette.Resolve(AppTheme.Dark);

        Assert.Equal(light.Keys.Order(), dark.Keys.Order());
        foreach (var (key, value) in dark)
        {
            if (ThemePalette.Tones.Any(tone => tone.Key == key))
            {
                Assert.NotEqual(light[key], value);
            }
        }
    }

    [Fact]
    public void DarkValuesAreTheDarkColumnOfTheDesignSystem()
    {
        var dark = ThemePalette.Resolve(AppTheme.Dark);

        Assert.Equal(0xFF1D222C, dark["MistTop"]); // D01 nightTop
        Assert.Equal(0xFF1B1C23, dark["MistBottom"]); // D02 nightBottom
        Assert.Equal(0xFF14171E, dark["MistLowest"]); // D04 nightLowest
        Assert.Equal(0xFFE6E9F0, dark["Ink"]); // D09 paper
        Assert.Equal(0xFFB6BBC5, dark["InkMuted"]); // D10 paperMuted
        Assert.Equal(0xFF9A9EA9, dark["InkFaint"]); // D11 paperFaint
        Assert.Equal(0xFF293144, dark["Bubble"]); // A06 bubble（暗色）
        Assert.Equal(0xFF97B1F0, dark["Seed"]); // A01 seed（暗色）
        Assert.Equal(0xFF14171E, dark["CodeWellBg"]); // M13 codeWellBg（暗色）
        Assert.Equal(0xFFE6E9F0, dark["CodeWellFg"]); // M15 codeWellFg（暗色）
    }

    [Fact]
    public void LightValuesAreUnchangedByTheThemeLayer()
    {
        var light = ThemePalette.Resolve(AppTheme.Light);

        Assert.Equal(0xFFEBF1FE, light["MistTop"]); // L01
        Assert.Equal(0xFFF6F7FC, light["MistBottom"]); // L02
        Assert.Equal(0xFF1E232E, light["Ink"]); // L09
        Assert.Equal(0xFF4F5562, light["InkMuted"]); // L10
        Assert.Equal(0xFFD9E4FD, light["Bubble"]); // A06
    }

    [Fact]
    public void DerivedSurfacesFollowTheThemeInsteadOfFallingBehind()
    {
        var light = ThemePalette.Resolve(AppTheme.Light);
        var dark = ThemePalette.Resolve(AppTheme.Dark);

        // ST27 / B03：横幅底与描边由 dangerContainer、accent 混出，两个主题都必须换。
        Assert.NotEqual(light["ErrorBannerFill"], dark["ErrorBannerFill"]);
        Assert.NotEqual(light["ErrorBannerBorder"], dark["ErrorBannerBorder"]);
        // Y43/Y44：发送键禁用态 = onSurface 12% / 38%，暗色的 onSurface 是 paper（D09）。
        Assert.Equal(
            ThemePalette.WithAlpha(0xFFE6E9F0, 0.12),
            dark["SendDisabled"]);
        Assert.Equal(
            ThemePalette.WithAlpha(0xFF1E232E, 0.12),
            light["SendDisabled"]);
        // G15：光晕透明度本来就分亮暗两列。
        Assert.Equal(ThemePalette.WithAlpha(0xFF51679D, 0.28), dark["GlowPrimaryInner"]);
        Assert.Equal(ThemePalette.WithAlpha(0xFFABC3FC, 0.42), light["GlowPrimaryInner"]);
    }

    [Fact]
    public void FollowingTheSystemThemeReportsEverySwitchExactlyOnce()
    {
        var follower = new SystemThemeFollower(systemIsDark: false);
        var seen = new List<AppTheme>();
        follower.Changed += (_, theme) => seen.Add(theme);

        Assert.False(follower.Update(false));
        Assert.True(follower.Update(true));
        Assert.False(follower.Update(true));
        Assert.Equal(AppTheme.Dark, follower.Theme);

        Assert.True(follower.Update(false));
        Assert.False(follower.Update(false));
        Assert.Equal(AppTheme.Light, follower.Theme);

        Assert.Equal([AppTheme.Dark, AppTheme.Light], seen);
    }
}
