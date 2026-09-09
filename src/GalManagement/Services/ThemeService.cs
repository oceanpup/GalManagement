using System.Windows;

namespace GalManagement.Services;

/// <summary>深/浅主题字典的动态切换。</summary>
public class ThemeService
{
    private ResourceDictionary? _current;

    public string Current { get; private set; } = "Dark";

    public void Apply(string theme)
    {
        var themeName = theme == "Light" ? "Light" : "Dark";
        Current = themeName;

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null)
            merged.Remove(_current);

        _current = new ResourceDictionary
        {
            Source = new Uri($"Themes/Theme.{themeName}.xaml", UriKind.Relative),
        };
        merged.Add(_current);
    }
}
