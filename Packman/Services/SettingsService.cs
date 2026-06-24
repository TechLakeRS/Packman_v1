using Packman.Models;
using System.IO;
using System.Text.Json;

namespace Packman.Services;

public class SettingsService
{
    private readonly string _path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
    private AppSettings? _settings;

    public AppSettings Settings => _settings ??= Load();

    private AppSettings Load()
    {
        if (!File.Exists(_path)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            }) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });
        File.WriteAllText(_path, json);
    }
}
