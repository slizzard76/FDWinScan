using FDWinScan.Configuration;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Text.Json;

namespace FDWinScan.Services;

public class SettingsService
{
    private readonly string _userSettingsPath;
    private readonly IConfigurationRoot _configuration;

    public BackendSettings Backend { get; private set; }
    public RecognitionSettings Recognition { get; private set; }

    public SettingsService()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AiScanner");
        Directory.CreateDirectory(appDataDir);
        _userSettingsPath = Path.Combine(appDataDir, "settings.json");

        _configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile(_userSettingsPath, optional: true, reloadOnChange: false)
            .Build();

        Backend = _configuration.GetSection("Backend").Get<BackendSettings>()
                  ?? new BackendSettings();
        Recognition = _configuration.GetSection("Recognition").Get<RecognitionSettings>()
                      ?? new RecognitionSettings();
    }

    /// <summary>
    /// Сохраняет настройки в пользовательский файл и обновляет текущий экземпляр.
    /// </summary>
    public void Save(BackendSettings backend, RecognitionSettings recognition)
    {
        var payload = new
        {
            Backend = backend,
            Recognition = recognition
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_userSettingsPath, json);

        Backend = backend;
        Recognition = recognition;
    }

    public string UserSettingsPath => _userSettingsPath;
}