using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FDWinScan.Configuration;
using FDWinScan.Services;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows.Media.Imaging;

namespace FDWinScan.ViewModels;

public partial class ScanViewModel : ObservableObject
{
    private readonly ApiClientFactory _apiFactory;
    private readonly SettingsService _settings;
    private readonly ScannerService _scanner = new();
    private BitmapSource? _scannedBitmap;
    private ScanResultDto? _current;

    [ObservableProperty] private string _statusMessage = "Готов к работе";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private BitmapImage? _previewImage;
    [ObservableProperty] private List<RecognizedField> _fields = new();
    [ObservableProperty] private System.Data.DataView? _rowsView;

    // Промпт теперь редактируется и в главном окне, и в настройках
    [ObservableProperty] private string _prompt = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecognizeCommand))]
    private bool _hasScannedDocument;

    partial void OnIsBusyChanged(bool value)
    {
        RecognizeCommand.NotifyCanExecuteChanged();
        DownloadExcelCommand.NotifyCanExecuteChanged();
        ScanDocumentCommand.NotifyCanExecuteChanged();
    }

    public ScanViewModel()
    {
        _settings = new SettingsService();
        _apiFactory = new ApiClientFactory(_settings);

        Prompt = _settings.Recognition.Prompt;
        StatusMessage = $"Бэкенд: {_settings.Backend.BaseUrl}";
    }

    [RelayCommand]
    private void ResetPrompt() => Prompt = RecognitionSettings.DefaultPrompt;

    [RelayCommand]
    private void OpenSettings()
    {
        var window = new Views.SettingsWindow(_settings, _apiFactory)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        if (window.ShowDialog() == true)
        {
            // Синхронизируем промпт: пользователь мог изменить его в настройках
            Prompt = _settings.Recognition.Prompt;
            StatusMessage = $"Бэкенд: {_settings.Backend.BaseUrl}";
        }
    }

    // ============ Сканирование ============
    [RelayCommand]
    private async Task ScanDocumentAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            StatusMessage = "Поиск сканера...";
            _scanner.Initialize();

            StatusMessage = "Сканирование...";
            var bitmap = await _scanner.ScanAsync();
            if (bitmap == null)
            {
                StatusMessage = "Не удалось получить изображение.";
                return;
            }

            _scannedBitmap = bitmap;

            using var ms = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(ms);
            ms.Position = 0;

            var preview = new BitmapImage();
            preview.BeginInit();
            preview.CacheOption = BitmapCacheOption.OnLoad;
            preview.StreamSource = ms;
            preview.EndInit();
            preview.Freeze();

            PreviewImage = preview;
            HasScannedDocument = true;

            _current = null;
            Fields = new();
            RowsView = null;

            StatusMessage = "Документ отсканирован. Нажмите «Распознать».";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка сканирования: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    // ============ Загрузка из файла ============
    [RelayCommand]
    private void LoadFromFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Images|*.jpg;*.jpeg;*.png;*.tiff;*.bmp"
        };
        if (dlg.ShowDialog() != true) return;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(dlg.FileName);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();

        _scannedBitmap = bmp;
        PreviewImage = bmp;
        HasScannedDocument = true;

        _current = null;
        Fields = new();
        RowsView = null;

        StatusMessage = "Файл загружен. Нажмите «Распознать».";
    }

    // ============ Распознавание ============
    private bool CanRecognize() => HasScannedDocument && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRecognize))]
    private async Task RecognizeAsync()
    {
        if (_scannedBitmap == null) return;
        try
        {
            IsBusy = true;
            StatusMessage = $"Отправка на {_settings.Backend.BaseUrl}...";

            using var ms = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(_scannedBitmap));
            encoder.Save(ms);
            var bytes = ms.ToArray();

            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            form.Add(fileContent, "file", "scan.png");

            if (!string.IsNullOrWhiteSpace(Prompt))
                form.Add(new StringContent(Prompt), "prompt");

            var response = await _apiFactory.Client.PostAsync("api/scan/process", form);
            response.EnsureSuccessStatusCode();

            var dto = await response.Content.ReadFromJsonAsync<ScanResultDto>();
            _current = dto;

            Fields = dto?.Fields ?? new();
            RowsView = BuildRowsView(dto?.Rows);

            StatusMessage = "Готово. Проверьте данные и сохраните Excel.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка распознавания: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    // ============ Скачивание Excel ============
    private bool CanDownload() => _current != null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadExcelAsync()
    {
        if (_current == null) return;
        try
        {
            IsBusy = true;
            var bytes = await _apiFactory.Client
                .GetByteArrayAsync($"api/scan/{_current.Id}/excel");

            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel Files|*.xlsx",
                FileName = Path.GetFileNameWithoutExtension(_current.FileName) + ".xlsx"
            };
            if (saveDialog.ShowDialog() == true)
            {
                await File.WriteAllBytesAsync(saveDialog.FileName, bytes);
                StatusMessage = "Файл сохранён.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private static System.Data.DataView? BuildRowsView(List<RecognizedRow>? rows)
    {
        if (rows == null || rows.Count == 0) return null;
        var headers = rows.SelectMany(r => r.Cells.Keys).Distinct().ToList();
        var table = new System.Data.DataTable();
        table.Columns.Add("#", typeof(int));
        foreach (var h in headers) table.Columns.Add(h, typeof(string));
        foreach (var r in rows)
        {
            var row = table.NewRow();
            row["#"] = r.RowNumber;
            foreach (var h in headers)
                row[h] = r.Cells.TryGetValue(h, out var v) ? v : "";
            table.Rows.Add(row);
        }
        return table.DefaultView;
    }
}