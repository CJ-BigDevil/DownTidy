using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DownTidy;

/// <summary>全局设计令牌与控件模板，所有窗口共用，保证风格统一。</summary>
public static class UiTheme
{
    // ---- 色板 ----
    public static readonly Brush PageBg = Frozen(Color.FromRgb(244, 245, 247));
    public static readonly Brush CardBg = Brushes.White;
    public static readonly Brush CardBorder = Frozen(Color.FromRgb(230, 232, 236));
    public static readonly Brush Divider = Frozen(Color.FromRgb(241, 242, 244));
    public static readonly Brush TextPrimary = Frozen(Color.FromRgb(31, 41, 55));
    public static readonly Brush TextSecondary = Frozen(Color.FromRgb(107, 114, 128));
    public static readonly Brush TextHint = Frozen(Color.FromRgb(156, 163, 175));
    public static readonly Brush InputBorder = Frozen(Color.FromRgb(216, 220, 227));
    public static readonly Brush InputBorderHover = Frozen(Color.FromRgb(185, 192, 203));
    public static readonly Brush RowHover = Frozen(Color.FromRgb(247, 248, 250));
    public static readonly Brush RowSelected = Frozen(Color.FromRgb(231, 243, 236));

    // 主色取自应用图标（陶土风收纳盒）的绿色系：图标深绿 #3D8F69、主体 #83BBA0、高光 #B8E4CF，
    // 主按钮色在其深绿基础上加深到可承载白色文字的对比度，色相保持一致（约 150°）。
    public static readonly Color AccentColor = Color.FromRgb(46, 140, 99);      // #2E8C63
    public static readonly Brush Accent = Frozen(AccentColor);
    public static readonly Brush AccentDark = Frozen(Color.FromRgb(39, 115, 80));    // #277350
    public static readonly Brush AccentPressed = Frozen(Color.FromRgb(31, 94, 66));  // #1F5E42
    public static readonly Brush AccentLight = Frozen(Color.FromRgb(227, 242, 234)); // #E3F2EA
    public static readonly Brush SuccessFg = Frozen(Color.FromRgb(30, 122, 80));     // #1E7A50
    public static readonly Brush ErrorFg = Frozen(Color.FromRgb(185, 28, 28));
    public static readonly Brush NeutralFg = Frozen(Color.FromRgb(55, 65, 81));

    public static readonly Brush PillRunFg = Frozen(Color.FromRgb(26, 112, 74));
    public static readonly Brush RunDot = Frozen(Color.FromRgb(61, 186, 135));       // #3DBA87，图标主体绿
    public static readonly Brush PillPauseBg = Frozen(Color.FromRgb(254, 243, 199));
    public static readonly Brush PillPauseFg = Frozen(Color.FromRgb(146, 64, 14));
    public static readonly Brush PauseDot = Frozen(Color.FromRgb(245, 158, 11));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>给窗口套用全局基调：浅灰底 + 雅黑字体。</summary>
    public static void Apply(Window w)
    {
        w.Background = PageBg;
        w.FontFamily = new FontFamily("Microsoft YaHei UI");
        w.FontSize = 13;
    }

    // ---- 按钮 ----
    private static ControlTemplate ButtonTemplate(Brush normalBg, Brush normalFg, Brush normalBorder,
        Brush hoverBg, Brush hoverBorder, Brush pressedBg)
    {
        var tpl = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "bd");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, normalBg);
        border.SetValue(Border.BorderBrushProperty, normalBorder);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(TextBlock.ForegroundProperty, normalFg);
        border.AppendChild(content);
        tpl.VisualTree = border;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, hoverBg, "bd"));
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, hoverBorder, "bd"));
        tpl.Triggers.Add(hover);

        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, pressedBg, "bd"));
        tpl.Triggers.Add(pressed);

        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
        tpl.Triggers.Add(disabled);
        return tpl;
    }

    public static Style PrimaryButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(18, 8, 18, 8)));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(Control.TemplateProperty, ButtonTemplate(
            Accent, Brushes.White, Accent, AccentDark, AccentDark, AccentPressed)));
        return style;
    }

    public static Style SecondaryButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16, 8, 16, 8)));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        style.Setters.Add(new Setter(Control.ForegroundProperty, NeutralFg));
        style.Setters.Add(new Setter(Control.TemplateProperty, ButtonTemplate(
            CardBg, NeutralFg, InputBorder, RowHover, Accent, Divider)));
        return style;
    }

    // ---- 输入框：圆角 + 聚焦描边 ----
    private static FrameworkElementFactory InputTemplateCore(out ControlTemplate tpl)
    {
        tpl = new ControlTemplate();
        var border = new FrameworkElementFactory(typeof(Border), "bd");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, CardBg);
        border.SetValue(Border.BorderBrushProperty, InputBorder);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        scroll.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        scroll.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(scroll);
        tpl.VisualTree = border;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, InputBorderHover, "bd"));
        tpl.Triggers.Add(hover);
        var focused = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focused.Setters.Add(new Setter(Border.BorderBrushProperty, Accent, "bd"));
        tpl.Triggers.Add(focused);
        return border;
    }

    public static Style TextBoxStyle(string? fontFamily = null, double fontSize = 13)
    {
        var style = new Style(typeof(TextBox));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 7, 10, 7)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, fontSize));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
        style.Setters.Add(new Setter(TextBox.SelectionBrushProperty, Frozen(AccentColor)));
        style.Setters.Add(new Setter(TextBox.CaretBrushProperty, TextPrimary));
        InputTemplateCore(out var tpl);
        tpl.TargetType = typeof(TextBox);
        style.Setters.Add(new Setter(Control.TemplateProperty, tpl));
        if (fontFamily != null)
            style.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily(fontFamily)));
        return style;
    }

    public static Style PasswordBoxStyle()
    {
        var style = new Style(typeof(PasswordBox));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 7, 10, 7)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
        style.Setters.Add(new Setter(PasswordBox.SelectionBrushProperty, Frozen(AccentColor)));
        InputTemplateCore(out var tpl);
        tpl.TargetType = typeof(PasswordBox);
        style.Setters.Add(new Setter(Control.TemplateProperty, tpl));
        return style;
    }

    // ---- 常用元素 ----
    public static Border Card() => new()
    {
        Background = CardBg,
        BorderBrush = CardBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12)
    };

    public static TextBlock Heading(string text, double size = 16) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = FontWeights.SemiBold,
        Foreground = TextPrimary
    };

    public static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = TextSecondary,
        TextWrapping = TextWrapping.Wrap
    };

    public static TextBlock FieldLabel(string text) => new()
    {
        Text = text,
        FontSize = 12.5,
        Foreground = TextSecondary,
        VerticalAlignment = VerticalAlignment.Center
    };

    public static Button MakeButton(string text, RoutedEventHandler handler, bool primary = false)
    {
        var button = new Button { Content = text };
        button.Style = primary ? PrimaryButtonStyle() : SecondaryButtonStyle();
        button.Click += handler;
        return button;
    }
}
