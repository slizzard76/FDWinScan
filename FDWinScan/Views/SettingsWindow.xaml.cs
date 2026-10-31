using FDWinScan.Configuration;
using FDWinScan.Services;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;

namespace FDWinScan.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly ApiClientFactory _factory;

    public SettingsWindow(SettingsService settings, ApiClientFactory factory)
    {
        InitializeComponent();
        _settings = settings;
        _factory = factory;

        BaseUrlBox.Text = settings.Backend.BaseUrl;
        TimeoutBox.Text = settings.Backend.TimeoutSeconds.ToString();
        PromptBox.Text = settings.Recognition.Prompt;
    }

    private void OnResetPromptClick(object sender, RoutedEventArgs e)
    {
        PromptBox.Text = RecognitionSettings.DefaultPrompt;
    }

    // ============ Проверка промпта ============
    private async void OnTestPromptClick(object sender, RoutedEventArgs e)
    {
        TestPromptButton.IsEnabled = false;
        TestResultText.Text = "Проверка...";

        try
        {
            // 1. Тестовое изображение генерируется локально — файл от пользователя не нужен
            var imageBytes = TestImageFactory.CreatePng();

            // 2. Используем временный HttpClient с текущими (ещё не сохранёнными!)
            //    настройками из полей диалога, чтобы проверка отражала то,
            //    что пользователь видит на экране
            if (!TryBuildBackend(out var backend, out var backendError))
            {
                TestResultText.Text = backendError;
                return;
            }

            using var client = new HttpClient
            {
                BaseAddress = new Uri(backend.BaseUrl),
                Timeout = TimeSpan.FromSeconds(Math.Max(30, backend.TimeoutSeconds))
            };

            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(imageBytes);
            fileContent.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            form.Add(fileContent, "file", "test.png");

            var prompt = PromptBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(prompt))
                form.Add(new StringContent(prompt), "prompt");

            var response = await client.PostAsync("api/scan/test-prompt", form);
            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync();
                TestResultText.Text = $"Сервер ответил {(int)response.StatusCode}: {Truncate(errBody, 200)}";
                return;
            }

            var result = await response.Content.ReadFromJsonAsync<TestPromptResult>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result == null)
            {
                TestResultText.Text = "Пустой ответ от сервера.";
                return;
            }

            // 3. Формируем читаемое сообщение
            var summary =
                $"✓ Полей: {result.FieldCount}, строк: {result.RowCount}, " +
                $"время: {result.ElapsedMs} мс";

            if (result.SampleFields.Count > 0)
            {
                var names = string.Join(", ", result.SampleFields.Select(f => f.Name));
                summary += $"\nПримеры полей: {Truncate(names, 120)}";
            }

            TestResultText.Text = summary;
        }
        catch (TaskCanceledException)
        {
            TestResultText.Text = "Таймаут: сервер не ответил вовремя.";
        }
        catch (Exception ex)
        {
            TestResultText.Text = $"Ошибка: {ex.Message}";
        }
        finally
        {
            TestPromptButton.IsEnabled = true;
        }
    }

    private async void OnTestClick(object sender, RoutedEventArgs e)
    {
        if (!TryBuildBackend(out var backend, out var error))
        {
            MessageBox.Show(error, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var testClient = new HttpClient
            {
                BaseAddress = new Uri(backend.BaseUrl),
                Timeout = TimeSpan.FromSeconds(10)
            };
            var resp = await testClient.GetAsync("swagger/v1/swagger.json");
            if (resp.IsSuccessStatusCode)
                MessageBox.Show("Подключение успешно.", "Проверка",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show($"Сервер ответил {(int)resp.StatusCode}.",
                    "Проверка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось подключиться: {ex.Message}",
                "Проверка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryBuildBackend(out var backend, out var backendError))
        {
            MessageBox.Show(backendError, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Если пользователь стёр промпт — сохраняем пустую строку:
        // сервер сам подставит дефолтный промпт.
        var recognition = new RecognitionSettings
        {
            Prompt = PromptBox.Text.Trim()
        };

        _settings.Save(backend, recognition);
        _factory.Recreate();
        DialogResult = true;
        Close();
    }

    private bool TryBuildBackend(out BackendSettings backend, out string error)
    {
        backend = new BackendSettings();
        error = "";

        var url = BaseUrlBox.Text.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            error = "Укажите корректный URL (http:// или https://).";
            return false;
        }

        if (!int.TryParse(TimeoutBox.Text.Trim(), out var timeout) ||
            timeout <= 0 || timeout > 3600)
        {
            error = "Таймаут должен быть целым числом от 1 до 3600 секунд.";
            return false;
        }

        backend.BaseUrl = url.EndsWith("/") ? url : url + "/";
        backend.TimeoutSeconds = timeout;
        return true;
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";

    private class TestPromptResult
    {
        public string Prompt { get; set; } = "";
        public int FieldCount { get; set; }
        public int RowCount { get; set; }
        public long ElapsedMs { get; set; }
        public List<RecognizedField> SampleFields { get; set; } = new();
        public List<RecognizedRow> SampleRows { get; set; } = new();
    }
}