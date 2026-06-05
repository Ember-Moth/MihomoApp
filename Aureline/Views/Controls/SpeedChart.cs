using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Aureline.Views.Controls;

public sealed class SpeedChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> PrimarySamplesProperty =
        AvaloniaProperty.Register<SpeedChart, IReadOnlyList<double>?>(nameof(PrimarySamples));

    public static readonly StyledProperty<IReadOnlyList<double>?> SecondarySamplesProperty =
        AvaloniaProperty.Register<SpeedChart, IReadOnlyList<double>?>(nameof(SecondarySamples));

    public static readonly StyledProperty<IBrush?> PrimaryStrokeProperty =
        AvaloniaProperty.Register<SpeedChart, IBrush?>(nameof(PrimaryStroke));

    public static readonly StyledProperty<IBrush?> SecondaryStrokeProperty =
        AvaloniaProperty.Register<SpeedChart, IBrush?>(nameof(SecondaryStroke));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<SpeedChart, IBrush?>(nameof(Fill));

    static SpeedChart()
    {
        AffectsRender<SpeedChart>(
            PrimarySamplesProperty,
            SecondarySamplesProperty,
            PrimaryStrokeProperty,
            SecondaryStrokeProperty,
            FillProperty);
    }

    public IReadOnlyList<double>? PrimarySamples
    {
        get => GetValue(PrimarySamplesProperty);
        set => SetValue(PrimarySamplesProperty, value);
    }

    public IReadOnlyList<double>? SecondarySamples
    {
        get => GetValue(SecondarySamplesProperty);
        set => SetValue(SecondarySamplesProperty, value);
    }

    public IBrush? PrimaryStroke
    {
        get => GetValue(PrimaryStrokeProperty);
        set => SetValue(PrimaryStrokeProperty, value);
    }

    public IBrush? SecondaryStroke
    {
        get => GetValue(SecondaryStrokeProperty);
        set => SetValue(SecondaryStrokeProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        var rect = new Rect(bounds.Size);
        if (Fill != null)
        {
            context.FillRectangle(Fill, rect);
        }

        var max = MaxSample(PrimarySamples, SecondarySamples);
        if (max <= 0)
        {
            max = 1;
        }

        DrawSeries(context, SecondarySamples, max, SecondaryStroke, rect);
        DrawSeries(context, PrimarySamples, max, PrimaryStroke, rect);
    }

    private static double MaxSample(params IReadOnlyList<double>?[] series)
    {
        var max = 0d;
        foreach (var samples in series)
        {
            if (samples == null)
            {
                continue;
            }

            foreach (var sample in samples)
            {
                max = Math.Max(max, sample);
            }
        }

        return max;
    }

    private static void DrawSeries(
        DrawingContext context,
        IReadOnlyList<double>? samples,
        double max,
        IBrush? stroke,
        Rect rect)
    {
        if (samples == null || samples.Count < 2 || stroke == null)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            for (var i = 0; i < samples.Count; i++)
            {
                var x = rect.X + rect.Width * i / (samples.Count - 1);
                var normalized = Math.Clamp(samples[i] / max, 0, 1);
                var y = rect.Bottom - rect.Height * normalized;
                var point = new Point(x, y);
                if (i == 0)
                {
                    stream.BeginFigure(point, false);
                }
                else
                {
                    stream.LineTo(point);
                }
            }
        }

        context.DrawGeometry(null, new Pen(stroke, 2), geometry);
    }
}
