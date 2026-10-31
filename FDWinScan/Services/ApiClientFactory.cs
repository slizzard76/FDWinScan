using FDWinScan.Services;
using System.Net.Http;
using FDWinScan.Configuration;

namespace FDWinScan.Services;

public class ApiClientFactory : IDisposable
{
    private readonly SettingsService _settings;
    private HttpClient? _client;

    public ApiClientFactory(SettingsService settings)
    {
        _settings = settings;
        CreateClient();
    }

    public HttpClient Client => _client
        ?? throw new InvalidOperationException("HttpClient не инициализирован.");

    public void Recreate()
    {
        _client?.Dispose();
        CreateClient();
    }

    private void CreateClient()
    {
        var backend = _settings.Backend;
        _client = new HttpClient
        {
            BaseAddress = new Uri(backend.BaseUrl),
            Timeout = TimeSpan.FromSeconds(backend.TimeoutSeconds)
        };
    }

    public void Dispose() => _client?.Dispose();
}