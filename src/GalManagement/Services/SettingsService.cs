using System.IO;
using System.Text.Json;
using GalManagement.Models;

namespace GalManagement.Services;

/// <summary>应用设置的读写(JSON)。</summary>
public class SettingsService
{
    private readonly string _path;
    private AppSettings _settings = new();

    public SettingsService(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "settings.json");
        Load();
    }

    public AppSettings Current => _settings;

    public void Save()
    {
        var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json);
    }

    private void Load()
    {
        if (!File.Exists(_path))
            return;

        try
        {
            var json = File.ReadAllText(_path);
            _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            _settings = new AppSettings();
        }
    }
}
