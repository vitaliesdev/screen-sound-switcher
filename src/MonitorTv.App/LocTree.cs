using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using MonitorTv.Core;

namespace MonitorTv.App;

/// <summary>
/// Translates the fixed texts written in a window's XAML (labels, tooltips, placeholders, automation names) after it has loaded, so the
/// XAML keeps the Spanish source text. Text that is data bound or built in code goes through <see cref="Loc.T(string)"/> itself.
/// </summary>
internal static class LocTree
{
    public static void Translate(DependencyObject root)
    {
        TranslateOne(root);
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            Translate(child);
    }

    // What each element said in the XAML (the Spanish source) and what it shows now, so the window can be translated again when the
    // language changes. A text the code has changed since is left alone: the code that owns it refreshes it.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DependencyObject, Dictionary<string, (string Source, string Shown)>> Seen = new();

    private static void Apply(DependencyObject d, string key, string current, Action<string> set)
    {
        var map = Seen.GetOrCreateValue(d);
        var source = current;
        if (map.TryGetValue(key, out var seen))
        {
            if (current != seen.Shown) return;
            source = seen.Source;
        }
        var shown = Loc.T(source);
        map[key] = (source, shown);
        if (shown != current) set(shown);
    }

    private static void TranslateOne(DependencyObject d)
    {
        switch (d)
        {
            case TextBlock text when text.Text.Length > 0 && !System.Windows.Data.BindingOperations.IsDataBound(text, TextBlock.TextProperty):
                Apply(d, "Text", text.Text, v => text.Text = v);
                break;
            case Wpf.Ui.Controls.TextBox box when !string.IsNullOrEmpty(box.PlaceholderText):
                Apply(d, "Placeholder", box.PlaceholderText, v => box.PlaceholderText = v);
                break;
            case Wpf.Ui.Controls.TitleBar bar when !string.IsNullOrEmpty(bar.Title):
                Apply(d, "Title", bar.Title, v => bar.Title = v);
                break;
            case Window window when !string.IsNullOrEmpty(window.Title):
                Apply(d, "Title", window.Title, v => window.Title = v);
                break;
            case HeaderedContentControl headered when headered.Header is string header:
                Apply(d, "Header", header, v => headered.Header = v);
                break;
            case ContentControl content when content.Content is string label && !System.Windows.Data.BindingOperations.IsDataBound(content, ContentControl.ContentProperty):
                Apply(d, "Content", label, v => content.Content = v);
                break;
        }

        if (d is FrameworkElement element && element.ToolTip is string tip)
            Apply(d, "ToolTip", tip, v => element.ToolTip = v);

        var name = AutomationProperties.GetName(d);
        if (!string.IsNullOrEmpty(name)) Apply(d, "AutoName", name, v => AutomationProperties.SetName(d, v));
        var help = AutomationProperties.GetHelpText(d);
        if (!string.IsNullOrEmpty(help)) Apply(d, "AutoHelp", help, v => AutomationProperties.SetHelpText(d, v));
    }
}
