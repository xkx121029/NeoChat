using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NeoChat.Windows.Core.Design;
using Microsoft.UI.Text;

namespace NeoChat.Windows.Design;

/// <summary>
/// Turns <see cref="Tokens"/> into the resource dictionary XAML binds to. This file holds no design
/// value of its own: every entry is a projection of a token, so the token file stays the only place a
/// colour, size or duration is written down.
/// </summary>
internal static class TokenResources
{
    internal static ResourceDictionary Build()
    {
        var dictionary = new ResourceDictionary();

        // §1.1–1.5 颜色：亮/暗两套由 ThemePalette 成对给出，这里只负责写入。ThemePainter 保存它创建的
        // 每个画刷实例，运行中切换主题时原地改 Color（静态资源引用不会换对象，换对象等于不刷新）。
        dictionary["ColorTransparent"] = ThemePainter.Argb(Tokens.Palette.Transparent);
        dictionary["BrushTransparent"] = new SolidColorBrush(ThemePainter.Argb(Tokens.Palette.Transparent));
        ThemePainter.Paint(dictionary, AppTheme.Light);

        // §1.6 字族与字重（只用 300 / 400 / 600 三档，§5 差异 9）
        dictionary["FontSans"] = new FontFamily(Tokens.Type.SansFamily);
        dictionary["FontIcon"] = new FontFamily(Tokens.Type.IconFamily);
        dictionary["WeightRegular"] = FontWeights.Normal;
        dictionary["WeightSemiBold"] = FontWeights.SemiBold;
        dictionary["WeightLight"] = FontWeights.Light;

        AddNumber(dictionary, "FontSizeDisplaySmall", Tokens.Type.DisplaySmallSize);
        AddNumber(dictionary, "LineHeightDisplaySmall", Tokens.Type.DisplaySmallLineHeight);
        AddNumber(dictionary, "FontSizeHeadlineMedium", Tokens.Type.HeadlineMediumSize);
        AddNumber(dictionary, "LineHeightHeadlineMedium", Tokens.Type.HeadlineMediumLineHeight);
        AddNumber(dictionary, "FontSizeTitleLarge", Tokens.Type.TitleLargeSize);
        AddNumber(dictionary, "LineHeightTitleLarge", Tokens.Type.TitleLargeLineHeight);
        AddNumber(dictionary, "FontSizeTitleSmall", Tokens.Type.TitleSmallSize);
        AddNumber(dictionary, "LineHeightTitleSmall", Tokens.Type.TitleSmallLineHeight);
        AddNumber(dictionary, "FontSizeBodyLarge", Tokens.Type.BodyLargeSize);
        AddNumber(dictionary, "LineHeightBodyLarge", Tokens.Type.BodyLargeLineHeight);
        AddNumber(dictionary, "FontSizeBodyMedium", Tokens.Type.BodyMediumSize);
        AddNumber(dictionary, "LineHeightBodyMedium", Tokens.Type.BodyMediumLineHeight);
        AddNumber(dictionary, "FontSizeBodySmall", Tokens.Type.BodySmallSize);
        AddNumber(dictionary, "LineHeightBodySmall", Tokens.Type.BodySmallLineHeight);
        AddNumber(dictionary, "FontSizeLabelLarge", Tokens.Type.LabelLargeSize);
        AddNumber(dictionary, "LineHeightLabelLarge", Tokens.Type.LabelLargeLineHeight);
        AddNumber(dictionary, "FontSizeLabelMedium", Tokens.Type.LabelMediumSize);
        AddNumber(dictionary, "LineHeightLabelMedium", Tokens.Type.LabelMediumLineHeight);
        AddNumber(dictionary, "FontSizeLabelSmall", Tokens.Type.LabelSmallSize);
        AddNumber(dictionary, "LineHeightLabelSmall", Tokens.Type.LabelSmallLineHeight);
        AddNumber(dictionary, "LineHeightHeaderStatus", Tokens.Type.HeaderStatusLineHeight);
        AddNumber(dictionary, "FontSizeModelChip", Tokens.Type.ModelChipSize);
        AddNumber(dictionary, "LineHeightModelChip", Tokens.Type.ModelChipLineHeight);
        AddNumber(dictionary, "FontSizeComposer", Tokens.Type.ComposerSize);
        AddNumber(dictionary, "LineHeightComposer", Tokens.Type.ComposerLineHeight);
        AddNumber(dictionary, "LineHeightStreamRowLabel", Tokens.Type.StreamRowLabelLineHeight);
        AddNumber(dictionary, "FontSizeStreamRowDetail", Tokens.Type.StreamRowDetailSize);
        AddNumber(dictionary, "LineHeightStreamRowDetail", Tokens.Type.StreamRowDetailLineHeight);

        // §1.7 圆角
        dictionary["RadiusXs"] = new CornerRadius(Tokens.Radius.Xs);
        dictionary["RadiusSm"] = new CornerRadius(Tokens.Radius.Sm);
        dictionary["RadiusMd"] = new CornerRadius(Tokens.Radius.Md);
        dictionary["RadiusLg"] = new CornerRadius(Tokens.Radius.Lg);
        dictionary["RadiusXl"] = new CornerRadius(Tokens.Radius.Xl);
        dictionary["RadiusCapsule"] = new CornerRadius(Tokens.Radius.Capsule);
        dictionary["RadiusCursorBar"] = new CornerRadius(Tokens.Radius.CursorBar);

        // §1.8 间距：同一批数值以 Thickness 提供给 Margin/Padding
        AddNumber(dictionary, "SpaceXxs", Tokens.Space.Xxs);
        AddNumber(dictionary, "SpaceXs", Tokens.Space.Xs);
        AddNumber(dictionary, "SpaceSm", Tokens.Space.Sm);
        AddNumber(dictionary, "SpaceMd", Tokens.Space.Md);
        AddNumber(dictionary, "SpaceLg", Tokens.Space.Lg);
        AddNumber(dictionary, "SpaceXl", Tokens.Space.Xl);
        AddNumber(dictionary, "SpaceXxl", Tokens.Space.Xxl);
        AddNumber(dictionary, "SpaceXxxl", Tokens.Space.Xxxl);
        AddNumber(dictionary, "SpaceGutter", Tokens.Space.Gutter);
        AddNumber(dictionary, "SpaceInTurn", Tokens.Space.InTurn);
        AddNumber(dictionary, "SpaceBetweenTurns", Tokens.Space.BetweenTurns);
        AddNumber(dictionary, "SpaceFloatingWidth", Tokens.Space.FloatingWidth);
        AddUniform(dictionary, "MarginXxs", Tokens.Space.Xxs);
        dictionary["ThicknessZero"] = new Thickness(0); // 非设计数值：零内边距（圆钮/图标键清空默认 padding）
        AddUniform(dictionary, "MarginXs", Tokens.Space.Xs);
        AddUniform(dictionary, "MarginSm", Tokens.Space.Sm);
        AddUniform(dictionary, "MarginMd", Tokens.Space.Md);
        AddUniform(dictionary, "MarginLg", Tokens.Space.Lg);
        AddUniform(dictionary, "MarginXxl", Tokens.Space.Xxl);
        AddUniform(dictionary, "MarginBetweenTurns", Tokens.Space.BetweenTurns);
        dictionary["ThicknessHeader"] = new Thickness(Tokens.Layout.HeaderPadding, 0, Tokens.Layout.HeaderPadding, 0);
        dictionary["ThicknessBubble"] = new Thickness(
            Tokens.Layout.BubblePaddingHorizontal,
            Tokens.Layout.BubblePaddingVertical,
            Tokens.Layout.BubblePaddingHorizontal,
            Tokens.Layout.BubblePaddingVertical);
        dictionary["ThicknessComposer"] = new Thickness(
            Tokens.Layout.ComposerPaddingStart,
            Tokens.Layout.ComposerPadding,
            Tokens.Layout.ComposerPadding,
            Tokens.Layout.ComposerPadding);
        dictionary["ThicknessErrorBanner"] = new Thickness(
            Tokens.Space.Md,
            Tokens.Space.Xs,
            Tokens.Space.Xs,
            Tokens.Space.Xs);
        dictionary["ThicknessComposerFooter"] = new Thickness(
            Tokens.Layout.ComposerPaddingStart,
            0,
            Tokens.Layout.ComposerPadding,
            Tokens.Layout.ComposerPadding);
        dictionary["ThicknessStreamRow"] = new Thickness(0, Tokens.Space.Xs, 0, Tokens.Space.Xs);
        dictionary["ThicknessModelCapsule"] = new Thickness(
            Tokens.Layout.ModelCapsulePaddingStart,
            0,
            Tokens.Layout.ModelCapsulePaddingEnd,
            0);
        dictionary["ThicknessEmptyCapsule"] = new Thickness(
            Tokens.Layout.EmptyCapsulePadStart,
            Tokens.Layout.EmptyCapsulePadVertical,
            Tokens.Layout.EmptyCapsulePadEnd,
            Tokens.Layout.EmptyCapsulePadVertical);
        AddUniform(dictionary, "ThicknessEmptyChip", Tokens.Layout.EmptyChipPadding);
        dictionary["ThicknessHeaderTitle"] = new Thickness(Tokens.Space.Sm, 0, Tokens.Space.Sm, 0);
        dictionary["ThicknessEmptyColumn"] = new Thickness(
            Tokens.Layout.EmptyMargin, 0, Tokens.Layout.EmptyMargin, 0);
        dictionary["ThicknessErrorBannerOuter"] = new Thickness(0, 0, 0, Tokens.Space.Md);
        dictionary["ThicknessStackGap"] = new Thickness(0, 0, 0, Tokens.Space.Md);
        dictionary["ThicknessListInset"] = new Thickness(
            0, Tokens.Layout.ListTopInset, 0, Tokens.Layout.ListBottomInset);
        dictionary["ThicknessStreamTextIndent"] = new Thickness(Tokens.Layout.StreamGap, 0, 0, 0);
        AddUniform(dictionary, "ThicknessErrorText", Tokens.Space.Sm);
        dictionary["MarginSubtitleTop"] = new Thickness(0, Tokens.Layout.EmptySubtitleGap, 0, 0);
        dictionary["MarginCapsuleTop"] = new Thickness(0, Tokens.Layout.EmptyCapsuleGap, 0, 0);
        dictionary["MarginSuggestionsTop"] = new Thickness(0, Tokens.Layout.EmptySuggestionsGap, 0, 0);
        dictionary["MarginHintTop"] = new Thickness(0, Tokens.Layout.EmptyHintGap, 0, 0);
        dictionary["MarginJumpToBottom"] = new Thickness(0, 0, Tokens.Layout.JumpToBottomInset, Tokens.Layout.JumpToBottomInset);
        dictionary["MarginFloatingInsets"] = new Thickness(
            Tokens.Space.FloatInset, 0, Tokens.Space.FloatInset, Tokens.Space.FloatInset);
        dictionary["MarginFloatingFooter"] = new Thickness(0, Tokens.Space.Xs, 0, 0);
        dictionary["MarginXs"] = new Thickness(Tokens.Space.Xs);
        dictionary["ThicknessComposerText"] = new Thickness(0, Tokens.Layout.ComposerTextVerticalPadding, 0, Tokens.Layout.ComposerTextVerticalPadding);

        // §2 布局常量
        AddNumber(dictionary, "LayoutHeaderHeight", Tokens.Layout.HeaderHeight);
        AddNumber(dictionary, "LayoutHeaderCapsuleMinHeight", Tokens.Layout.HeaderCapsuleMinHeight);
        AddNumber(dictionary, "LayoutHeaderKeySize", Tokens.Layout.HeaderKeySize);
        AddNumber(dictionary, "LayoutHeaderIconSize", Tokens.Layout.HeaderIconSize);
        AddNumber(dictionary, "LayoutStatusDotSize", Tokens.Layout.StatusDotSize);
        AddNumber(dictionary, "LayoutStatusDotGap", Tokens.Layout.StatusDotGap);
        AddNumber(dictionary, "LayoutModelCapsuleMinHeight", Tokens.Layout.ModelCapsuleMinHeight);
        AddNumber(dictionary, "LayoutModelChevronSize", Tokens.Layout.ModelChevronSize);
        AddNumber(dictionary, "LayoutListTopInset", Tokens.Layout.ListTopInset);
        AddNumber(dictionary, "LayoutListBottomInset", Tokens.Layout.ListBottomInset);
        AddNumber(dictionary, "LayoutJumpToBottomInset", Tokens.Layout.JumpToBottomInset);
        AddNumber(dictionary, "LayoutStreamLead", Tokens.Layout.StreamLead);
        AddNumber(dictionary, "LayoutStreamGap", Tokens.Layout.StreamGap);
        AddNumber(dictionary, "LayoutStreamIndent", Tokens.Layout.StreamIndent);
        AddNumber(dictionary, "LayoutStreamRowMinHeight", Tokens.Layout.StreamRowMinHeight);
        AddNumber(dictionary, "LayoutThinkingWindowLines", Tokens.Layout.ThinkingWindowLines);
        AddNumber(dictionary, "LayoutCursorWidth", Tokens.Layout.CursorWidth);
        AddNumber(dictionary, "LayoutCursorHeight", Tokens.Layout.CursorHeight);
        AddNumber(dictionary, "LayoutInterruptedIconSize", Tokens.Layout.InterruptedIconSize);
        AddNumber(dictionary, "LayoutComposerKeySize", Tokens.Layout.ComposerKeySize);
        AddNumber(dictionary, "LayoutComposerKeyGap", Tokens.Layout.ComposerKeyGap);
        AddNumber(dictionary, "LayoutComposerHeight", Tokens.Layout.ComposerHeight);
        AddNumber(dictionary, "LayoutComposerMaxHeight", Tokens.Layout.ComposerMaxHeight);
        AddNumber(dictionary, "LayoutComposerIconSize", Tokens.Layout.ComposerIconSize);
        AddNumber(dictionary, "LayoutComposerStopIconSize", Tokens.Layout.ComposerStopIconSize);
        AddNumber(dictionary, "LayoutComposerFooterGap", Tokens.Layout.ComposerFooterGap);
        AddNumber(dictionary, "LayoutErrorIconSize", Tokens.Layout.ErrorIconSize);
        AddNumber(dictionary, "LayoutErrorIconGap", Tokens.Layout.ErrorIconGap);
        AddNumber(dictionary, "LayoutErrorMaxTextLines", Tokens.Layout.ErrorMaxTextLines);
        AddNumber(dictionary, "LayoutErrorActionMinSize", Tokens.Layout.ErrorActionMinSize);
        AddNumber(dictionary, "LayoutErrorCloseSize", Tokens.Layout.ErrorCloseSize);
        AddNumber(dictionary, "LayoutErrorCloseIconSize", Tokens.Layout.ErrorCloseIconSize);
        AddNumber(dictionary, "LayoutEmptyMaxWidth", Tokens.Layout.EmptyMaxWidth);
        AddNumber(dictionary, "LayoutEmptyMargin", Tokens.Layout.EmptyMargin);
        AddNumber(dictionary, "LayoutEmptySubtitleGap", Tokens.Layout.EmptySubtitleGap);
        AddNumber(dictionary, "LayoutEmptyCapsuleGap", Tokens.Layout.EmptyCapsuleGap);
        AddNumber(dictionary, "LayoutEmptyOptionsGap", Tokens.Layout.EmptyOptionsGap);
        AddNumber(dictionary, "LayoutEmptySuggestionsGap", Tokens.Layout.EmptySuggestionsGap);
        AddNumber(dictionary, "LayoutEmptyHintGap", Tokens.Layout.EmptyHintGap);
        AddNumber(dictionary, "LayoutEmptyCapsuleHeight", Tokens.Layout.EmptyCapsuleHeight);
        AddNumber(dictionary, "LayoutEmptyChipHeight", Tokens.Layout.EmptyChipHeight);
        AddNumber(dictionary, "LayoutEmptyChipGap", Tokens.Layout.EmptyChipGap);
        AddNumber(dictionary, "LayoutGlowDiameter", Tokens.Layout.GlowDiameter);
        AddNumber(dictionary, "LayoutIconGlyphSize", Tokens.Layout.IconGlyphSize);
        AddNumber(dictionary, "SpaceReadingWidth", Tokens.Space.ReadingWidth);
        AddNumber(dictionary, "LayoutRailWidth", Tokens.Layout.RailWidth);
        AddNumber(dictionary, "LayoutRailRowMinHeight", Tokens.Layout.RailRowMinHeight);
        AddNumber(dictionary, "LayoutMessageActionSize", Tokens.Layout.MessageActionSize);
        AddNumber(dictionary, "LayoutMessageActionIconSize", Tokens.Layout.MessageActionIconSize);
        AddNumber(dictionary, "LayoutMessageActionGap", Tokens.Layout.MessageActionGap);
        AddNumber(dictionary, "LayoutCodeCopyKeySize", Tokens.Layout.CodeCopyKeySize);
        dictionary["MarginMessageActions"] = new Thickness(0, Tokens.Layout.MessageActionTopGap, 0, 0);
        AddNumber(dictionary, "LayoutOptimisticOpacity", Tokens.Layout.OptimisticBubbleOpacity);
        dictionary["LayoutErrorMaxTextLinesInt"] = (int)Tokens.Layout.ErrorMaxTextLines;
        dictionary["LayoutThinkingWindowLinesInt"] = (int)Tokens.Layout.ThinkingWindowLines;
        AddNumber(dictionary, "LayoutComposerTextVerticalPadding", Tokens.Layout.ComposerTextVerticalPadding);
        // BorderThickness 需要 Thickness，不能是 double（StaticResource 不会做数值转换）。
        // 会话列表：内边距与行内边距全部取自既有间距档位，只有右侧分隔线需要单独的 Thickness。
        dictionary["ThicknessRail"] = new Thickness(Tokens.Space.Sm);
        dictionary["ThicknessRailItem"] = new Thickness(
            Tokens.Space.Md, Tokens.Space.Sm, Tokens.Space.Md, Tokens.Space.Sm);
        dictionary["MarginRailHeader"] = new Thickness(0, 0, 0, Tokens.Space.Sm);
        dictionary["BorderThicknessRailDivider"] = new Thickness(0, 0, Tokens.Border.Hairline, 0);
        AddUniform(dictionary, "BorderHairline", Tokens.Border.Hairline);
        AddUniform(dictionary, "BorderCard", Tokens.Border.Card);
        AddUniform(dictionary, "BorderErrorBanner", Tokens.Border.ErrorBanner);

        return dictionary;
    }

    private static void AddNumber(ResourceDictionary dictionary, string name, double value) => dictionary[name] = value;

    private static void AddUniform(ResourceDictionary dictionary, string name, double value) =>
        dictionary[name] = new Thickness(value);
}
