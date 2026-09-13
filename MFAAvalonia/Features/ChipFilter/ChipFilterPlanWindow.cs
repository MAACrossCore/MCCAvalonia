using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MFAAvalonia.Helper;
using SukiUI.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MFAAvalonia.Features.ChipFilter;

public sealed class ChipFilterPlanWindow : SukiWindow
{
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#4F72E8"));
    private static readonly IBrush MutedBorder = new SolidColorBrush(Color.Parse("#667080"));
    private static readonly IBrush Configured = new SolidColorBrush(Color.Parse("#2D8A62"));
    private readonly Dictionary<int, Dictionary<string, ToggleButton>> _modeButtons = [];
    private readonly Dictionary<int, Button> _configureButtons = [];
    private readonly Dictionary<string, Button> _skillButtons = [];
    private readonly TextBox _nameBox = new() { Watermark = "方案名称", MaxLength = 40 };
    private readonly TextBox _importBox = new() { Watermark = "在这里粘贴方案码" };
    private readonly TextBlock _conditionTitle = new() { FontSize = 18, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _progress = new();
    private readonly WrapPanel _skillPanel = new();
    private readonly Button _copyButton = new();
    private readonly Border _conditionArea;
    private ChipFilterPlan _plan;
    private int _activeLevel = 2;
    private string _currentMainSkill = ChipFilterCatalog.MainSkills[0];

    public ChipFilterPlanWindow()
    {
        Title = "芯片筛选方案";
        Width = Math.Max(980, Instances.RootView?.Bounds.Width ?? 1180);
        Height = Math.Max(680, Instances.RootView?.Bounds.Height ?? 760);
        MinWidth = 900;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = IconHelper.WindowIcon;
        _plan = ChipFilterPlanStore.Load();

        _conditionArea = BuildConditionArea();
        Content = BuildLayout();
        LoadPlanIntoControls();
    }

    public static async Task ShowForCurrentProjectAsync(Window? owner = null)
    {
        var window = new ChipFilterPlanWindow();
        await window.ShowDialog(owner ?? Instances.RootView);
    }

    private Control BuildLayout()
    {
        var root = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        root.Children.Add(BuildTopBar());

        var levelCards = new UniformGrid { Columns = 3, Rows = 1, Margin = new Thickness(0, 14, 0, 14) };
        Grid.SetRow(levelCards, 1);
        foreach (var level in new[] { 1, 2, 3 })
            levelCards.Children.Add(BuildLevelCard(level));
        root.Children.Add(levelCards);

        Grid.SetRow(_conditionArea, 2);
        root.Children.Add(_conditionArea);

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 14, 0, 0) };
        var path = new TextBlock
        {
            Text = $"保存位置：{ChipFilterPlanStore.PlanPath}",
            Opacity = 0.65,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        footer.Children.Add(path);
        var save = new Button { Content = "保存方案", Classes = { "Primary" }, MinWidth = 120 };
        save.Click += async (_, _) => await SaveAsync();
        Grid.SetColumn(save, 1);
        footer.Children.Add(save);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
        return root;
    }

    private Control BuildTopBar()
    {
        var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var titleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 0, 0, 10) };
        titleRow.Children.Add(new TextBlock
        {
            Text = "芯片筛选方案",
            FontSize = 24,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        _nameBox.Margin = new Thickness(18, 0, 0, 0);
        _nameBox.MaxWidth = 360;
        _nameBox.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(_nameBox, 1);
        titleRow.Children.Add(_nameBox);
        Grid.SetColumnSpan(titleRow, 2);
        panel.Children.Add(titleRow);

        _importBox.MinWidth = 520;
        Grid.SetRow(_importBox, 1);
        panel.Children.Add(_importBox);
        var import = new Button { Content = "导入方案码", Margin = new Thickness(10, 0, 0, 0), MinWidth = 112 };
        import.Click += async (_, _) => await ImportAsync();
        Grid.SetRow(import, 1);
        Grid.SetColumn(import, 1);
        panel.Children.Add(import);
        return panel;
    }

    private Control BuildLevelCard(int level)
    {
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = $"芯片主词条 {level} 级",
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        var segment = new UniformGrid { Columns = 3, Rows = 1 };
        _modeButtons[level] = [];
        foreach (var (mode, label) in new[]
                 {
                     (ChipLockModes.Lock, "锁定"),
                     (ChipLockModes.Conditional, "条件锁定"),
                     (ChipLockModes.Unlock, "不锁定")
                 })
        {
            var button = new ToggleButton { Content = label, MinHeight = 34, Margin = new Thickness(2) };
            button.Click += (_, _) => SetMode(level, mode);
            _modeButtons[level][mode] = button;
            segment.Children.Add(button);
        }
        content.Children.Add(segment);

        var configure = new Button
        {
            Content = "配置副词条条件",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        configure.Click += (_, _) => ActivateConditionalLevel(level);
        _configureButtons[level] = configure;
        content.Children.Add(configure);

        return new Border
        {
            Child = content,
            BorderBrush = MutedBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14),
            Margin = new Thickness(5, 0)
        };
    }

    private Border BuildConditionArea()
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(_conditionTitle);
        _progress.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_progress, 1);
        header.Children.Add(_progress);
        _copyButton.Margin = new Thickness(12, 0, 0, 0);
        _copyButton.Click += async (_, _) => await CopyToUnconfiguredAsync();
        Grid.SetColumn(_copyButton, 2);
        header.Children.Add(_copyButton);
        var clear = new Button
        {
            Content = "清空当前配置",
            Background = new SolidColorBrush(Color.Parse("#C74747")),
            Foreground = Brushes.White,
            Margin = new Thickness(10, 0, 0, 0)
        };
        clear.Click += async (_, _) => await ClearCurrentLevelAsync();
        Grid.SetColumn(clear, 3);
        header.Children.Add(clear);

        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        content.Children.Add(header);
        var scroll = new ScrollViewer
        {
            Content = _skillPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 1);
        content.Children.Add(scroll);
        return new Border
        {
            Child = content,
            BorderBrush = MutedBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14)
        };
    }

    private void LoadPlanIntoControls()
    {
        _nameBox.Text = _plan.Name;
        foreach (var level in new[] { 1, 2, 3 })
            UpdateModeButtons(level);
        _activeLevel = _plan.Levels.Where(pair => pair.Value.Mode == ChipLockModes.Conditional)
            .Select(pair => pair.Key).DefaultIfEmpty(2).First();
        RefreshConditionArea();
    }

    private void SetMode(int level, string mode)
    {
        _plan.Levels[level].Mode = mode;
        UpdateModeButtons(level);
        if (mode == ChipLockModes.Conditional)
            ActivateConditionalLevel(level);
        else if (_activeLevel == level)
        {
            _activeLevel = _plan.Levels.Where(pair => pair.Value.Mode == ChipLockModes.Conditional)
                .Select(pair => pair.Key).DefaultIfEmpty(level).First();
            RefreshConditionArea();
        }
    }

    private void UpdateModeButtons(int level)
    {
        var current = _plan.Levels[level].Mode;
        foreach (var pair in _modeButtons[level])
        {
            var selected = pair.Key == current;
            pair.Value.IsChecked = selected;
            if (selected)
            {
                pair.Value.Background = Accent;
                pair.Value.Foreground = Brushes.White;
            }
            else
            {
                pair.Value.ClearValue(TemplatedControl.BackgroundProperty);
                pair.Value.ClearValue(TemplatedControl.ForegroundProperty);
            }
        }
        _configureButtons[level].IsVisible = current == ChipLockModes.Conditional;
    }

    private void ActivateConditionalLevel(int level)
    {
        _activeLevel = level;
        RefreshConditionArea();
    }

    private void RefreshConditionArea()
    {
        var rule = _plan.Levels[_activeLevel];
        _conditionArea.IsVisible = rule.Mode == ChipLockModes.Conditional;
        if (!_conditionArea.IsVisible)
            return;

        _conditionTitle.Text = $"主词条 {_activeLevel} 级：按类别配置副词条";
        _progress.Text = $"已配置 {rule.Conditions.Count}/{ChipFilterCatalog.MainSkills.Length}";
        _copyButton.Content = $"复制“{_currentMainSkill}”配置到...";
        _skillPanel.Children.Clear();
        _skillButtons.Clear();
        foreach (var main in ChipFilterCatalog.MainSkills)
        {
            var done = rule.Conditions.ContainsKey(main);
            var current = main == _currentMainSkill;
            var button = new Button
            {
                Content = current ? $"{main}  当前词条" : done ? $"{main}  已配置" : $"{main}  待配置",
                MinWidth = 128,
                Margin = new Thickness(2),
                BorderBrush = current || done ? Configured : MutedBorder,
                BorderThickness = new Thickness(current ? 3 : done ? 2 : 1)
            };
            if (current)
            {
                button.Background = Configured;
                button.Foreground = Brushes.White;
            }
            button.Click += (_, _) =>
            {
                _currentMainSkill = main;
                UpdateCurrentSkillSelection();
            };
            button.DoubleTapped += async (_, _) => await ConfigureMainSkillAsync(main);
            _skillButtons[main] = button;
            _skillPanel.Children.Add(button);
        }
    }

    private void UpdateCurrentSkillSelection()
    {
        var rule = _plan.Levels[_activeLevel];
        _copyButton.Content = $"复制“{_currentMainSkill}”配置到...";
        foreach (var (main, button) in _skillButtons)
        {
            var current = main == _currentMainSkill;
            var done = rule.Conditions.ContainsKey(main);
            button.Content = current ? $"{main}  当前词条" : done ? $"{main}  已配置" : $"{main}  待配置";
            button.BorderBrush = current || done ? Configured : MutedBorder;
            button.BorderThickness = new Thickness(current ? 3 : done ? 2 : 1);
            if (current)
            {
                button.Background = Configured;
                button.Foreground = Brushes.White;
            }
            else
            {
                button.ClearValue(TemplatedControl.BackgroundProperty);
                button.ClearValue(TemplatedControl.ForegroundProperty);
            }
        }
    }

    private async Task ConfigureMainSkillAsync(string main)
    {
        var levelRule = _plan.Levels[_activeLevel];
        levelRule.Conditions.TryGetValue(main, out var existing);
        var wizard = new ChipSubSkillWizard(_activeLevel, main, existing);
        if (await wizard.ShowDialog<bool>(this))
        {
            levelRule.Conditions[main] = wizard.Result;
            _currentMainSkill = main;
            RefreshConditionArea();
        }
    }

    private async Task CopyToUnconfiguredAsync()
    {
        var rule = _plan.Levels[_activeLevel];
        if (!rule.Conditions.TryGetValue(_currentMainSkill, out var source))
        {
            await NoticeWindow.ShowAsync(this, $"当前词条“{_currentMainSkill}”尚未配置，请双击完成设置后再复制。");
            return;
        }
        var unconfigured = ChipFilterCatalog.MainSkills.Where(main => !rule.Conditions.ContainsKey(main)).ToArray();
        if (unconfigured.Length == 0)
        {
            await NoticeWindow.ShowAsync(this, "当前等级没有未配置的主词条。");
            return;
        }
        var picker = new CopyTargetWindow(_currentMainSkill, unconfigured);
        if (!await picker.ShowDialog<bool>(this))
            return;
        foreach (var main in picker.SelectedSkills)
            rule.Conditions[main] = source.Clone();
        RefreshConditionArea();
        await NoticeWindow.ShowAsync(this, $"已将“{_currentMainSkill}”的设置复制到 {picker.SelectedSkills.Count} 个未配置主词条。");
    }

    private async Task ClearCurrentLevelAsync()
    {
        var rule = _plan.Levels[_activeLevel];
        if (rule.Conditions.Count == 0)
        {
            await NoticeWindow.ShowAsync(this, $"主词条 {_activeLevel} 级目前没有条件锁定配置。");
            return;
        }
        var confirmed = await ChoiceWindow.AskAsync(this,
            $"确认清空主词条 {_activeLevel} 级的全部条件锁定配置？\n主词条 1、2、3 级中的其他等级不会受影响。", "确认清空", "取消");
        if (!confirmed)
            return;
        rule.Conditions.Clear();
        RefreshConditionArea();
        await NoticeWindow.ShowAsync(this, $"已清空主词条 {_activeLevel} 级的条件锁定配置。");
    }

    private async Task ImportAsync()
    {
        try
        {
            _plan = ChipFilterPlanCodec.Decode(_importBox.Text ?? string.Empty);
            _importBox.Text = string.Empty;
            _currentMainSkill = ChipFilterCatalog.MainSkills[0];
            LoadPlanIntoControls();
            await NoticeWindow.ShowAsync(this, "方案码导入成功，检查后点击“保存方案”即可使用。");
        }
        catch (Exception ex)
        {
            await NoticeWindow.ShowAsync(this, ex.Message);
        }
    }

    private async Task SaveAsync()
    {
        _plan.Name = (_nameBox.Text ?? string.Empty).Trim();
        foreach (var level in new[] { 1, 2, 3 })
        {
            var rule = _plan.Levels[level];
            if (rule.Mode == ChipLockModes.Conditional && rule.Conditions.Count != ChipFilterCatalog.MainSkills.Length)
            {
                ActivateConditionalLevel(level);
                await NoticeWindow.ShowAsync(this,
                    $"主词条 {level} 级还有 {ChipFilterCatalog.MainSkills.Length - rule.Conditions.Count} 个类别未配置。");
                return;
            }
        }

        try
        {
            ChipFilterPlanStore.Save(_plan);
            var export = await ChoiceWindow.AskAsync(this, "方案已保存！是否导出方案码？", "是", "否");
            if (export)
                await new PlanCodeWindow(ChipFilterPlanCodec.Encode(_plan)).ShowDialog(this);
        }
        catch (Exception ex)
        {
            await NoticeWindow.ShowAsync(this, ex.Message);
        }
    }
}

internal sealed class ChipSubSkillWizard : SukiWindow
{
    private static readonly int[] TotalLevelThresholds = [2, 3, 4, 5, 6];
    private readonly List<CheckBox> _skillBoxes = [];
    private readonly WrapPanel _choices = new();
    private readonly TextBlock _heading = new() { FontSize = 19, FontWeight = FontWeight.SemiBold };
    private readonly ComboBox _totalLevel = new()
    {
        ItemsSource = new[] { "大于等于 2", "大于等于 3", "大于等于 4", "大于等于 5", "大于等于 6" },
        SelectedIndex = 1,
        MinWidth = 150
    };

    public ChipSubSkillRule Result { get; private set; } = new();

    public ChipSubSkillWizard(int mainLevel, string mainSkill, ChipSubSkillRule? existing)
    {
        Title = $"{mainSkill}副词条条件";
        Width = 720;
        Height = 470;
        MinWidth = 620;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = IconHelper.WindowIcon;
        var selected = new HashSet<string>(existing?.EffectiveSubSkills ?? []);
        foreach (var skill in ChipFilterCatalog.SubSkills)
        {
            var box = new CheckBox
            {
                Content = skill,
                IsChecked = selected.Contains(skill),
                Margin = new Thickness(6),
                MinWidth = 130
            };
            _skillBoxes.Add(box);
            _choices.Children.Add(box);
        }
        if (existing != null)
            _totalLevel.SelectedIndex = Math.Max(0, Array.IndexOf(TotalLevelThresholds, existing.MinimumTotalLevel));

        _heading.Text = $"主词条 {mainLevel} 级 · {mainSkill}";
        Content = BuildLayout();
    }

    private Control BuildLayout()
    {
        var root = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto") };
        root.Children.Add(_heading);
        var help = new TextBlock
        {
            Text = "勾选对当前主词条有效的副词条；芯片上这些词条的实际等级相加后参与判定。",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Thickness(0, 8, 0, 12)
        };
        Grid.SetRow(help, 1);
        root.Children.Add(help);
        var scroll = new ScrollViewer { Content = _choices, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);
        var totalLevelRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                new TextBlock
                {
                    Text = "所选有效副词条实际总等级",
                    VerticalAlignment = VerticalAlignment.Center
                },
                _totalLevel,
                new TextBlock
                {
                    Text = "时上锁",
                    Opacity = 0.7,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
        Grid.SetRow(totalLevelRow, 3);
        root.Children.Add(totalLevelRow);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "取消", MinWidth = 90 };
        var confirm = new Button { Content = "完成设置", Classes = { "Primary" }, MinWidth = 100 };
        cancel.Click += (_, _) => Close(false);
        confirm.Click += async (_, _) =>
        {
            var effective = _skillBoxes.Where(box => box.IsChecked == true)
                .Select(box => box.Content?.ToString() ?? string.Empty)
                .Where(value => value.Length > 0)
                .ToList();
            if (effective.Count == 0)
            {
                await NoticeWindow.ShowAsync(this, "请至少勾选一个有效副词条。");
                return;
            }
            Result = new ChipSubSkillRule
            {
                MinimumTotalLevel = TotalLevelThresholds[Math.Max(0, _totalLevel.SelectedIndex)],
                EffectiveSubSkills = effective
            };
            Close(true);
        };
        footer.Children.Add(cancel);
        footer.Children.Add(confirm);
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);
        return root;
    }
}

internal sealed class CopyTargetWindow : SukiWindow
{
    private readonly List<CheckBox> _boxes = [];

    public IReadOnlyList<string> SelectedSkills { get; private set; } = [];

    public CopyTargetWindow(string source, IEnumerable<string> targets)
    {
        Title = "选择复制目标";
        Width = 680;
        Height = 520;
        MinWidth = 600;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = IconHelper.WindowIcon;

        var choices = new WrapPanel();
        foreach (var target in targets)
        {
            var box = new CheckBox { Content = target, Margin = new Thickness(6), MinWidth = 128 };
            _boxes.Add(box);
            choices.Children.Add(box);
        }

        var selectAll = new Button { Content = "全选" };
        var clearAll = new Button { Content = "取消全选" };
        var cancel = new Button { Content = "取消", MinWidth = 90 };
        var confirm = new Button { Content = "确认复制", Classes = { "Primary" }, MinWidth = 100 };
        selectAll.Click += (_, _) => _boxes.ForEach(box => box.IsChecked = true);
        clearAll.Click += (_, _) => _boxes.ForEach(box => box.IsChecked = false);
        cancel.Click += (_, _) => Close(false);
        confirm.Click += async (_, _) =>
        {
            SelectedSkills = _boxes.Where(box => box.IsChecked == true)
                .Select(box => box.Content?.ToString() ?? string.Empty)
                .Where(value => value.Length > 0).ToArray();
            if (SelectedSkills.Count == 0)
            {
                await NoticeWindow.ShowAsync(this, "请至少选择一个需要复制配置的主词条。");
                return;
            }
            Close(true);
        };

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { selectAll, clearAll }
        };
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Children = { cancel, confirm }
        };
        Content = new Grid
        {
            Margin = new Thickness(20),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Children =
            {
                new TextBlock
                {
                    Text = $"将“{source}”的副词条条件复制到选中的未配置词条",
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                },
                new ContentControl { Content = toolbar, Margin = new Thickness(0, 12, 0, 8), [Grid.RowProperty] = 1 },
                new ScrollViewer
                {
                    Content = choices,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    [Grid.RowProperty] = 2
                },
                new ContentControl { Content = footer, Margin = new Thickness(0, 14, 0, 0), [Grid.RowProperty] = 3 }
            }
        };
    }
}

internal sealed class NoticeWindow : SukiWindow
{
    private NoticeWindow(string message)
    {
        Title = "提示";
        Width = 480;
        Height = 210;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = IconHelper.WindowIcon;
        var close = new Button { Content = "确定", Classes = { "Primary" }, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 90 };
        close.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 24,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 16 },
                close
            }
        };
    }

    public static Task ShowAsync(Window owner, string message) => new NoticeWindow(message).ShowDialog(owner);
}

internal sealed class ChoiceWindow : SukiWindow
{
    private ChoiceWindow(string message, string yes, string no)
    {
        Title = "确认";
        Width = 500;
        Height = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = IconHelper.WindowIcon;
        var yesButton = new Button { Content = yes, Classes = { "Primary" }, MinWidth = 90 };
        var noButton = new Button { Content = no, MinWidth = 90 };
        yesButton.Click += (_, _) => Close(true);
        noButton.Click += (_, _) => Close(false);
        Content = new Grid
        {
            Margin = new Thickness(22),
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 17 },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { noButton, yesButton },
                    [Grid.RowProperty] = 1
                }
            }
        };
    }

    public static Task<bool> AskAsync(Window owner, string message, string yes, string no) =>
        new ChoiceWindow(message, yes, no).ShowDialog<bool>(owner);
}

internal sealed class PlanCodeWindow : SukiWindow
{
    private readonly TextBlock _notice = new() { Text = "已复制到剪贴板，快去分享吧~", IsVisible = false };

    public PlanCodeWindow(string code)
    {
        Title = "方案码如下";
        Width = 780;
        Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = IconHelper.WindowIcon;
        var codeBox = new TextBox
        {
            Text = code,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 150
        };
        var copy = new Button { Content = "复制", Classes = { "Primary" }, MinWidth = 90 };
        var close = new Button { Content = "关闭", MinWidth = 90 };
        copy.Click += async (_, _) =>
        {
            await Clipboard.SetTextAsync(code);
            _notice.IsVisible = true;
            await Task.Delay(TimeSpan.FromSeconds(10));
            _notice.IsVisible = false;
        };
        close.Click += (_, _) => Close();
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 12, 0, 0) };
        _notice.VerticalAlignment = VerticalAlignment.Center;
        buttons.Children.Add(_notice);
        Grid.SetColumn(copy, 1);
        buttons.Children.Add(copy);
        close.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(close, 2);
        buttons.Children.Add(close);
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "方案码如下", FontSize = 20, FontWeight = FontWeight.SemiBold },
                codeBox,
                buttons
            }
        };
    }
}
