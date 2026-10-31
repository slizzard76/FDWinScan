using NTwain;
using NTwain.Data;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace FDWinScan.Services;

public class ScannerService : IDisposable
{
    private TwainSession? _session;
    private DataSource? _source;
    private ReturnCode t;
    /// <summary>
    /// Инициализирует TWAIN-сессию и выбирает первый доступный сканер.
    /// </summary>
    public void Initialize()
    {
        if (_session != null) return;

        var appId = TWIdentity.CreateFromAssembly(DataGroups.Image, Assembly.GetExecutingAssembly());
        NTwain.PlatformInfo.Current.PreferNewDSM = false;
        _session = new TwainSession(appId);

        _session.Open();

        // Берём первый доступный источник (сканер)
        _source = _session.FirstOrDefault();
        if (_source == null)
            throw new InvalidOperationException("Сканер не найден. Проверьте подключение и драйверы.");
    }

    /// <summary>
    /// Запускает сканирование и возвращает изображение в виде BitmapSource.
    /// </summary>
    public async Task<BitmapSource?> ScanAsync()
    {
        if (_session == null || _source == null)
            throw new InvalidOperationException("Сканер не инициализирован.");

        var tcs = new TaskCompletionSource<BitmapSource?>();

        // Подписываемся на событие у SESSION, а не у SOURCE
        EventHandler<DataTransferredEventArgs>? handler = null;
        handler = (s, e) =>
        {
            _session.DataTransferred -= handler; // отписка у session

            BitmapSource? bitmap = null;
            if (e.NativeData != IntPtr.Zero)
            {
                using var stream = e.GetNativeImageStream();
                if (stream != null && stream.Length > 0)
                    bitmap = CreateFrozenBitmapSource(stream);
            }
            tcs.TrySetResult(bitmap);
        };

        _session.DataTransferred += handler; // ✅ подписка у session

        // Настройки и запуск сканирования (без изменений)
        //_source.Capabilities.ICapXResolution.SetValue(300);
        //_source.Capabilities.ICapYResolution.SetValue(300);
        //_source.Capabilities.ICapPixelType.SetValue(PixelType.RGB);


        if (_source.IsOpen)
        {
            _source.Close();
        }

        t = _source.Open();
        _source.Enable(SourceEnableMode.NoUI, false, IntPtr.Zero);
        

        return await tcs.Task;
    }

    /// <summary>
    /// Преобразует поток из TWAIN в BitmapSource с правильным DPI.
    /// </summary>
    private static BitmapSource CreateFrozenBitmapSource(Stream stream)
    {
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];

        // Создаём новый BitmapSource на основе пикселей — это отвязывает его от декодера
        var stride = (frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8;
        var pixels = new byte[frame.PixelHeight * stride];
        frame.CopyPixels(pixels, stride, 0);

        var frozen = BitmapSource.Create(
            frame.PixelWidth, frame.PixelHeight,
            300, 300,
            frame.Format, frame.Palette,
            pixels, stride);

        frozen.Freeze(); // замораживаем в фоновом потоке
        return frozen;
    }

    public void Dispose()
    {
        try { _source?.Close(); } catch { /* ignore */ }
        try { _session?.Close(); } catch { /* ignore */ }
        _session = null;
        _source = null;
    }
}