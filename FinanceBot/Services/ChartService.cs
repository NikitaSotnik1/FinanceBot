using SkiaSharp;
using FinanceBot.Models;

namespace FinanceBot.Services;

public static class ChartService
{
    public static byte[] GenerateExpenseChart(string title, List<(string Category, decimal Sum, double Percent)> data)
    {
        int width = 600;
        int height = 100 + (data.Count * 70); // Динамическая высота

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        var paintText = new SKPaint { Color = SKColors.Black, TextSize = 24, IsAntialias = true, FakeBoldText = true };
        var paintBarBg = new SKPaint { Color = SKColor.Parse("#EEEEEE") };
        var paintBarFill = new SKPaint { Color = SKColor.Parse("#4CAF50") };

        canvas.DrawText(title, 30, 50, paintText);

        int y = 100;
        foreach (var item in data)
        {
            // Название категории
            canvas.DrawText(item.Category, 30, y, paintText);

            // Фоновая полоска
            var rectBg = new SKRect(30, y + 10, width - 150, y + 40);
            canvas.DrawRoundRect(rectBg, 5, 5, paintBarBg);

            // Заполненная полоска (процент)
            float fillWidth = (float)((width - 180) * (item.Percent / 100));
            var rectFill = new SKRect(30, y + 10, 30 + fillWidth, y + 40);
            canvas.DrawRoundRect(rectFill, 5, 5, paintBarFill);

            // Текст процентов
            canvas.DrawText($"{item.Percent:0}% ({item.Sum} ₽)", width - 140, y + 35, new SKPaint { TextSize = 20, IsAntialias = true });

            y += 70;
        }

        using var image = surface.Snapshot();
        using var dataImg = image.Encode(SKEncodedImageFormat.Png, 100);
        return dataImg.ToArray();
    }
}