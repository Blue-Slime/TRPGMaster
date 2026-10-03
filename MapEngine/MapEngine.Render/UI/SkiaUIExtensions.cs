using SkiaSharp;

namespace MapEngine.Render.UI;

/// <summary>
/// Extension methods for rendering Token UI elements using SkiaSharp.
/// </summary>
public static class SkiaUIExtensions
{
    public static void Draw(this SkiaLabel label, SKCanvas canvas, double zoom)
    {
        if (string.IsNullOrEmpty(label.Text)) return;

        var screenX = (float)(label.CenterX * zoom);
        var screenY = (float)(label.CenterY * zoom);
        var fontSize = label.FontSize * (float)zoom;

        using var paint = new SKPaint
        {
            IsAntialias = true,
            TextSize = fontSize,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
            TextAlign = SKTextAlign.Center
        };

        // Shadow
        paint.Color = new SKColor(0, 0, 0, 128);
        paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2f);
        canvas.DrawText(label.Text, screenX + 1, screenY + 1, paint);
        paint.MaskFilter = null;

        // Stroke
        paint.Color = SKColors.Black;
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 2f;
        canvas.DrawText(label.Text, screenX, screenY, paint);

        // Fill
        paint.Color = new SKColor(
            (byte)(label.Color.R * 255),
            (byte)(label.Color.G * 255),
            (byte)(label.Color.B * 255),
            (byte)(label.Color.A * 255));
        paint.Style = SKPaintStyle.Fill;
        canvas.DrawText(label.Text, screenX, screenY, paint);
    }

    public static void Draw(this SkiaHealthBar bar, SKCanvas canvas, double zoom)
    {
        var screenX = (float)(bar.X * zoom);
        var screenY = (float)(bar.Y * zoom);
        var width = (float)(bar.Width * zoom);
        var height = (float)(bar.Height * zoom);
        var radius = 2f * (float)zoom;

        var rect = new SKRect(screenX, screenY, screenX + width, screenY + height);

        // Background (dark red)
        using var bgPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(139, 0, 0),
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRoundRect(rect, radius, radius, bgPaint);

        // Foreground (green, based on percentage)
        if (bar.Percentage > 0)
        {
            var fillWidth = width * bar.Percentage;
            var fillRect = new SKRect(screenX, screenY, screenX + fillWidth, screenY + height);

            using var fillPaint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(50, 205, 50),
                Style = SKPaintStyle.Fill
            };
            canvas.DrawRoundRect(fillRect, radius, radius, fillPaint);
        }

        // Border
        using var borderPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };
        canvas.DrawRoundRect(rect, radius, radius, borderPaint);
    }

    public static void Draw(this SkiaBadge badge, SKCanvas canvas, double zoom)
    {
        if (string.IsNullOrEmpty(badge.Icon)) return;

        var screenX = (float)(badge.CenterX * zoom);
        var screenY = (float)(badge.CenterY * zoom);
        var size = (float)(badge.Size * zoom);
        var radius = size / 2f;

        var rect = new SKRect(screenX - radius, screenY - radius, screenX + radius, screenY + radius);

        // Background circle
        using var bgPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(
                (byte)(badge.BackgroundColor.R * 255),
                (byte)(badge.BackgroundColor.G * 255),
                (byte)(badge.BackgroundColor.B * 255),
                (byte)(badge.BackgroundColor.A * 255)),
            Style = SKPaintStyle.Fill
        };
        canvas.DrawCircle(screenX, screenY, radius, bgPaint);

        // Icon text (emoji)
        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            TextSize = size * 0.6f,
            Color = SKColors.White,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI Emoji")
        };

        // Center text vertically
        var textBounds = new SKRect();
        textPaint.MeasureText(badge.Icon, ref textBounds);
        var textY = screenY - textBounds.MidY;

        canvas.DrawText(badge.Icon, screenX, textY, textPaint);

        // Stack count (if > 1)
        if (badge.StackCount > 1)
        {
            using var countPaint = new SKPaint
            {
                IsAntialias = true,
                TextSize = size * 0.3f,
                Color = SKColors.White,
                TextAlign = SKTextAlign.Right,
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
            };

            canvas.DrawText(badge.StackCount.ToString(), screenX + radius, screenY - radius + size * 0.3f, countPaint);
        }
    }
}
