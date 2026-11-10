using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DownTidy;

public sealed class RulesWindow : Window
{
    private readonly RuleService _rules;
    private readonly TextBox _editor = new();
    private readonly TextBlock _message = new();

    public RulesWindow(RuleService rules)
    {
        _rules = rules;
        Title = "高级规则 · DownTidy";
        Width = 780;
        Height = 660;
        MinWidth = 620;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UiTheme.Apply(this);
        BuildUi();
        _editor.Text = _rules.CurrentJson;
    }

    private void BuildUi()
    {
        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;

        // 头部：标题 + 说明
        var header = new StackPanel { Margin = new Thickness(2, 0, 2, 12) };
        header.Children.Add(UiTheme.Heading("高级规则"));
        header.Children.Add(new TextBlock
        {
            Text = "规则按顺序匹配，命中即停。targetFolder 只能填写下载目录内的相对子目录，校验失败不会覆盖当前规则。\n想加自己的归类规则？推荐让 AI 帮忙：点下方「用 AI 帮忙写规则」，复制提示词发给任意聊天 AI，把 AI 返回的 JSON 贴回编辑框，点「校验并应用」即可。全程不用配置任何 key，工具也不联网；AI 写得不合适没关系，校验通过并确认过才会生效。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = UiTheme.TextSecondary,
            Margin = new Thickness(0, 5, 0, 0)
        });
        root.Children.Add(header);

        // 编辑器卡片
        var editorCard = UiTheme.Card();
        editorCard.Margin = new Thickness(0, 0, 0, 4);
        editorCard.Padding = new Thickness(10);
        _editor.Style = UiTheme.TextBoxStyle("Consolas", 13);
        _editor.AcceptsReturn = true;
        _editor.AcceptsTab = true;
        _editor.TextWrapping = TextWrapping.NoWrap;
        _editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _editor.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _editor.Background = System.Windows.Media.Brushes.Transparent;
        editorCard.Child = _editor;
        Grid.SetRow(editorCard, 1);
        root.Children.Add(editorCard);

        // 校验消息
        _message.TextWrapping = TextWrapping.Wrap;
        _message.FontSize = 12.5;
        _message.Margin = new Thickness(2, 10, 2, 10);
        _message.Foreground = UiTheme.TextSecondary;
        Grid.SetRow(_message, 2);
        root.Children.Add(_message);

        // 按钮行：左侧主要操作，右侧关闭
        var footer = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var leftButtons = new StackPanel { Orientation = Orientation.Horizontal };
        leftButtons.Children.Add(UiTheme.MakeButton("校验并应用", Apply, primary: true));
        leftButtons.Children.Add(WithMargin(UiTheme.MakeButton("只校验", ValidateOnly), 10, 0, 0, 0));
        leftButtons.Children.Add(WithMargin(UiTheme.MakeButton("恢复默认规则", RestoreDefaults), 10, 0, 0, 0));
        leftButtons.Children.Add(WithMargin(UiTheme.MakeButton("用 AI 帮忙写规则", CopyPrompt), 10, 0, 0, 0));
        footer.Children.Add(leftButtons);
        var closeButton = UiTheme.MakeButton("关闭", (_, _) => Close());
        Grid.SetColumn(closeButton, 1);
        footer.Children.Add(closeButton);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
    }

    private static FrameworkElement WithMargin(FrameworkElement e, double l, double t, double r, double b)
    {
        e.Margin = new Thickness(l, t, r, b);
        return e;
    }

    private void Apply(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = _editor.Text;
            var json = RuleService.ExtractJsonBlock(text);
            bool extracted = json != text.Trim();
            _rules.Save(json);
            _editor.Text = _rules.CurrentJson;
            ShowMessage(extracted
                ? "已自动从粘贴内容中提取 JSON 并应用，新规则立即生效。"
                : "规则已校验并应用，新规则立即生效。", UiTheme.SuccessFg);
        }
        catch (Exception ex) { ShowMessage(ex.Message, UiTheme.ErrorFg); }
    }

    private void ValidateOnly(object sender, RoutedEventArgs e)
    {
        var json = RuleService.ExtractJsonBlock(_editor.Text);
        _rules.Validate(json, out var error);
        ShowMessage(string.IsNullOrEmpty(error)
                ? "格式和路径校验通过，尚未应用。"
                : error,
            string.IsNullOrEmpty(error) ? UiTheme.SuccessFg : UiTheme.ErrorFg);
    }

    private void RestoreDefaults(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "确定放弃当前修改并恢复默认规则吗？", "恢复默认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _editor.Text = RuleService.Serialize(RuleService.CreateDefaults());
        Apply(sender, e);
    }

    private void CopyPrompt(object sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "用 AI 帮忙写规则 · DownTidy",
            Width = 720,
            Height = 700,
            MinWidth = 560,
            MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };
        UiTheme.Apply(dialog);

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        dialog.Content = root;

        // 用法说明：三步，把边界讲清楚
        var header = new StackPanel { Margin = new Thickness(2, 0, 2, 12) };
        header.Children.Add(UiTheme.Heading("用 AI 帮忙写规则"));
        header.Children.Add(new TextBlock
        {
            Text = "按下面三步走，不需要给工具配置任何 key，工具全程不联网：\n① 在下面的框里用大白话写一句你想怎么整理（不知道怎么写就点一个示例，改几个字就行），然后点「复制全部」，发给任意聊天 AI（比如 WorkBuddy、DeepSeek 网页版）；\n② AI 会返回一段完整 JSON，复制它；\n③ 回到「高级规则」编辑框，把 AI 的回复整段粘贴进来（客套话不用删，程序会自动挑出 JSON），点「校验并应用」。校验通过才会生效，不放心可以先点「只校验」看看。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = UiTheme.TextSecondary,
            Margin = new Thickness(0, 5, 0, 0)
        });
        root.Children.Add(header);

        // 需求区：大白话写需求 + 可点选的示例
        var requirementCard = UiTheme.Card();
        requirementCard.Margin = new Thickness(0, 0, 0, 10);
        requirementCard.Padding = new Thickness(12, 10, 12, 10);
        var requirementStack = new StackPanel();

        requirementStack.Children.Add(new TextBlock
        {
            Text = "你想让 DownTidy 怎么整理？（大白话写一句就行，不会写就点一个示例）",
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiTheme.TextPrimary
        });

        var requirementBox = new TextBox
        {
            Margin = new Thickness(0, 8, 0, 0),
            AcceptsReturn = false
        };
        requirementBox.Style = UiTheme.TextBoxStyle(null, 13);

        var samples = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        samples.Children.Add(new TextBlock
        {
            Text = "示例：",
            FontSize = 11.5,
            Foreground = UiTheme.TextHint,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 4)
        });
        string[] sampleTexts =
        {
            "把截图和图片单独放一个文件夹",
            "所有视频放到一个文件夹里",
            "发票、合同这类文件放在一起",
            "压缩包和安装包单独放，别混在一起"
        };
        foreach (var text in sampleTexts)
        {
            var chip = UiTheme.MakeButton(text, (_, _) => { });
            chip.FontSize = 11.5;
            chip.Margin = new Thickness(0, 0, 8, 4);
            chip.Click += (_, _) =>
            {
                requirementBox.Text = text;
                requirementBox.CaretIndex = text.Length;
            };
            samples.Children.Add(chip);
        }

        requirementStack.Children.Add(requirementBox);
        requirementStack.Children.Add(samples);
        requirementCard.Child = requirementStack;
        Grid.SetRow(requirementCard, 1);
        root.Children.Add(requirementCard);

        // 提示词全文预览（随需求实时刷新）
        var card = UiTheme.Card();
        card.Padding = new Thickness(10);
        var promptBox = new TextBox
        {
            IsReadOnly = true,
            Background = System.Windows.Media.Brushes.Transparent
        };
        promptBox.Style = UiTheme.TextBoxStyle("Consolas", 12.5);
        promptBox.AcceptsReturn = true;
        promptBox.TextWrapping = TextWrapping.Wrap;
        promptBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

        void RefreshPrompt()
        {
            var requirement = requirementBox.Text.Trim();
            promptBox.Text = string.IsNullOrEmpty(requirement)
                ? _rules.BuildAiPrompt() + "\n\n我的需求：（请把这句换成你想怎么整理，例如：把截图和图片单独放一个文件夹）"
                : _rules.BuildAiPrompt() + "\n\n我的需求：" + requirement;
        }
        requirementBox.TextChanged += (_, _) => RefreshPrompt();
        RefreshPrompt();

        card.Child = promptBox;
        Grid.SetRow(card, 2);
        root.Children.Add(card);

        // 按钮：复制 + 关闭
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var copyButton = UiTheme.MakeButton("复制全部", (_, _) => { }, primary: true);
        copyButton.Click += (_, _) =>
        {
            Clipboard.SetText(promptBox.Text);
            copyButton.Content = "已复制 ✓";
        };
        footer.Children.Add(copyButton);
        footer.Children.Add(WithMargin(UiTheme.MakeButton("关闭", (_, _) => dialog.Close()), 10, 0, 0, 0));
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        dialog.ShowDialog();
    }

    private void ShowMessage(string text, Brush color)
    {
        _message.Text = text;
        _message.Foreground = color;
    }
}
