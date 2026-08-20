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
            var settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            }) ?? new AppSettings();
            Migrate(settings);
            return settings;
        }
        catch { return new AppSettings(); }
    }

    /// <summary>
    /// Moves a per-package group that was configured with the Uninstall intent onto the
    /// dedicated uninstall group, which is where that intent lives now.
    /// </summary>
    private static void Migrate(AppSettings settings)
    {
        var groups = settings.GroupAssignment;
        if (!groups.CreateGroupPerPackage || groups.NewGroupIntent != AssignmentIntent.Uninstall) return;

        groups.CreateUninstallGroupPerPackage = true;
        groups.UninstallGroupNameTemplate = groups.GroupNameTemplate;
        groups.CreateGroupPerPackage = false;
        groups.NewGroupIntent = AssignmentIntent.Required;
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
