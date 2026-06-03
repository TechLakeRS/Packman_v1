using System.Windows;

namespace Packman;

public static class PlaceholderText
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(PlaceholderText),
        new FrameworkPropertyMetadata(string.Empty));

    public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string value) => d.SetValue(TextProperty, value);
}
