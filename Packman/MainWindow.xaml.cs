using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Packman.Views;

namespace Packman;

public partial class MainWindow : Window
{
    private int _currentStep;
    private readonly UserControl[] _stepViews;
    private readonly (string title, string sub, bool optional, string primary)[] _stepMeta =
    {
        ("Generate", "PSADT structure", false, "Generate Package"),
        ("Edit Script", "Optional", true, "Continue"),
        ("Remote Test", "Optional", true, "Continue"),
        ("Upload", "Deploy to Intune", false, "Build & Upload"),
    };

    public MainWindow()
    {
        InitializeComponent();
        _stepViews = new UserControl[]
        {
            new StepGenerate(),
            new StepEdit(),
            new StepTest(),
            new StepUpload(),
        };
        SetStep(0);
    }

    private void Stepper_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string s && int.TryParse(s, out int idx))
            SetStep(idx);
    }

    private void BackBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 0) SetStep(_currentStep - 1);
    }

    private void SkipBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep < _stepViews.Length - 1) SetStep(_currentStep + 1);
    }

    private void PrimaryBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep < _stepViews.Length - 1)
        {
            SetStep(_currentStep + 1);
        }
        else
        {
            // Last step → would kick off the publish flow.
            // Wired later; for now just stay on the page.
        }
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var dark = !App.IsDarkTheme;
        App.ApplyTheme(dark);
        // Swap the theme button icon
        var iconKey = dark ? "IconSun" : "IconMoon";
        if (FindResource(iconKey) is Geometry geo)
            ThemeIconPath.Data = geo;
    }

    private void SetStep(int idx)
    {
        idx = Math.Clamp(idx, 0, _stepViews.Length - 1);
        _currentStep = idx;
        StageHost.Content = _stepViews[idx];

        // Update stepper node visuals.
        for (int i = 0; i < 4; i++)
        {
            var node = (Border)FindName($"StepNode{i}")!;
            var num = (TextBlock)FindName($"StepNum{i}")!;
            var check = (Viewbox)FindName($"StepCheck{i}")!;
            var title = (TextBlock)FindName($"StepTitle{i}")!;
            var sub = (TextBlock)FindName($"StepSub{i}")!;
            var lineL = (Border)FindName($"StepLineL{i}")!;
            var lineR = (Border)FindName($"StepLineR{i}")!;

            // Default state
            node.Background = (Brush)FindResource("CardBrush");
            node.BorderBrush = (Brush)FindResource("LineBrush");
            node.Effect = null;
            num.Foreground = (Brush)FindResource("Muted2Brush");
            num.Visibility = Visibility.Visible;
            check.Visibility = Visibility.Collapsed;
            title.Foreground = (Brush)FindResource("MutedBrush");

            if (i < idx)
            {
                // done
                node.Background = (Brush)FindResource("PrimaryBrush");
                node.BorderBrush = (Brush)FindResource("PrimaryBrush");
                num.Visibility = Visibility.Collapsed;
                check.Visibility = Visibility.Visible;
                title.Foreground = (Brush)FindResource("InkBrush");
            }
            else if (i == idx)
            {
                // current
                node.Background = (Brush)FindResource("PrimaryBrush");
                node.BorderBrush = (Brush)FindResource("PrimaryBrush");
                node.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = (Color)FindResource("PrimaryColor"),
                    Opacity = 0.22,
                    ShadowDepth = 0,
                    BlurRadius = 14
                };
                num.Foreground = Brushes.White;
                title.Foreground = (Brush)FindResource("InkBrush");
            }

            // Lines: left = done if i<=idx (connects to previous done step), right = done if i<idx
            lineL.Background = (i <= idx && i > 0)
                ? (Brush)FindResource("PrimaryBrush")
                : (Brush)FindResource("LineBrush");
            lineR.Background = (i < idx)
                ? (Brush)FindResource("PrimaryBrush")
                : (Brush)FindResource("LineBrush");
        }

        // Footer: Back enabled if step > 0, Skip visible for optional non-last,
        // Primary label/chevron based on whether last step.
        BackBtn.IsEnabled = idx > 0;
        bool isLast = idx == _stepViews.Length - 1;
        var meta = _stepMeta[idx];
        SkipBtn.Visibility = (meta.optional && !isLast) ? Visibility.Visible : Visibility.Collapsed;
        PrimaryBtnLabel.Text = meta.primary;
        PrimaryBtnChev.Visibility = isLast ? Visibility.Collapsed : Visibility.Visible;
    }
}
