using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FDWinScan.Services;

public static class TestImageFactory
{
    /// <summary>
    /// Рисует простое «тестовое» изображение документа и возвращает PNG в байтах.
    /// </summary>
    public static byte[] CreatePng(int width = 800, int height = 600)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));

            var titleBrush = Brushes.Black;
            var textBrush = Brushes.DimGray;

            var title = new FormattedText(
                "TEST DOCUMENT",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                28, titleBrush, 1.0);
            dc.DrawText(title, new Point(40, 40));

            var lines = new[]
            {
                "Invoice No: TEST-0001",
                "Date: 2026-10-31",
                "Supplier: Test Company LLC",
                "",
                "Items:",
                "  1. Sample service   qty 1   price 1000   total 1000",
                "  2. Sample service   qty 2   price 250    total 500",
                "",
                "Total: 1500.00"
            };

            double y = 100;
            foreach (var line in lines)
            {
                var ft = new FormattedText(
                    line,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Consolas"),
                    18, textBrush, 1.0);
                dc.DrawText(ft, new Point(40, y));
                y += 28;
            }
        }

        var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}

