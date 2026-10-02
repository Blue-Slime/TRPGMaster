using SkiaSharp;

namespace MapEngine.Render.UI;

public sealed class SkiaLabel
{
    public string Text { get; init; } = string.Empty;

    public SKPoint Position { get; init; }

    public float FontSize { get; init; } = 14f;

    public SKColor Color { get; init; } = SKColors.White;

    public bool HasShadow { get; init; } = true;

    public void Draw(SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            TextSize = FontSize,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };

        if (HasShadow)
        {
            paint.Color = new SKColor(0, 0, 0, 128);
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2f);
            canvas.DrawText(Text, Position.X + 1, Position.Y + 1, paint);
            paint.MaskFilter = null;
        }

        paint.Color = SKColors.Black;
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 2f;
        canvas.DrawText(Text, Position.X, Position.Y, paint);

        paint.Color = Color;
        paint.Style = SKPaintStyle.Fill;
        canvas.DrawText(Text, Position.X, Position.Y, paint);
    }
}

public sealed class SkiaHealthBar
{
    public int HP { get; init; }

    public int MaxHP { get; init; }

    public SKPoint Position { get; init; }

    public float Width { get; init; } = 80f;

    public float Height { get; init; } = 10f;

    public void Draw(SKCanvas canvas)
    {
        var rect = new SKRect(Position.X, Position.Y, Position.X + Width, Position.Y + Height);
        var radius = 2f;

        using var bgPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(139, 0, 0),
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRoundRect(rect, radius, radius, bgPaint);

        if (HP > 0 && MaxHP > 0)
        {
            var fillWidth = Width * ((float)HP / MaxHP);
            var fillRect = new SKRect(Position.X, Position.Y, Position.X + fillWidth, Position.Y + Height);

            using var fillPaint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(50, 205, 50),
                Style = SKPaintStyle.Fill
            };
            canvas.DrawRoundRect(fillRect, radius, radius, fillPaint);
        }

        using var borderPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };
        canvas.DrawRoundRect(rect, radius, radius, borderPaint);
    }
}

public sealed class SkiaBadge
{
    public SKBitmap? Icon { get; init; }

    public SKPoint Position { get; init; }

    public float Size { get; init; } = 16f;

    public void Draw(SKCanvas canvas)
    {
        if (Icon == null) return;

        var destRect = new SKRect(Position.X, Position.Y, Position.X + Size, Position.Y + Size);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High
        };

        canvas.DrawBitmap(Icon, destRect, paint);
    }
}
