using System.Windows;
using Microsoft.Win32;

namespace GitHubTopicReports.Services;

public static class ThemeManager
{
    private const string ThemeFolder = "Resources/Themes/";
    private static bool _initialized;

    public static bool IsDark { get; private set; }

    public static void ApplySystemTheme()
    {
        if (_initialized)
        {
            return;
        }

        bool dark;
        try
        {
            dark = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1) is int value && value == 0;
        }
        catch
        {
            dark = false;
        }

        Apply(dark);
    }

    public static bool Toggle()
    {
        Apply(!IsDark);
        return IsDark;
    }

    public static void SetDark(bool dark) => Apply(dark);

    private static void Apply(bool dark)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var source = new Uri(
            "pack://application:,,,/GitHubTopicReports;component/" +
            ThemeFolder +
            (dark ? "Dark.xaml" : "Light.xaml"),
            UriKind.Absolute);
        var theme = new ResourceDictionary { Source = source };
        var dictionaries = app.Resources.MergedDictionaries;
        var existingIndex = -1;
        for (var index = 0; index < dictionaries.Count; index++)
        {
            if (dictionaries[index].Source?.OriginalString.Contains(
                    ThemeFolder,
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                existingIndex = index;
                break;
            }
        }

        if (existingIndex >= 0)
        {
            dictionaries[existingIndex] = theme;
        }
        else
        {
            dictionaries.Insert(0, theme);
        }

        IsDark = dark;
        _initialized = true;
    }
}
