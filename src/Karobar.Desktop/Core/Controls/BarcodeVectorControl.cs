using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Karobar.Application.Barcodes;

namespace Karobar.Desktop.Views;

/// <summary>
/// Crisp pure-vector WPF Code 128 barcode control for live sticker preview and native FixedDocument printing.
/// </summary>
public sealed class BarcodeVectorControl : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(string),
        typeof(BarcodeVectorControl),
        new FrameworkPropertyMetadata(
            "000000",
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarHeightProperty = DependencyProperty.Register(
        nameof(BarHeight),
        typeof(double),
        typeof(BarcodeVectorControl),
        new FrameworkPropertyMetadata(
            28.0,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarWidthProperty = DependencyProperty.Register(
        nameof(BarWidth),
        typeof(double),
        typeof(BarcodeVectorControl),
        new FrameworkPropertyMetadata(
            1.3,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty QuietZoneProperty = DependencyProperty.Register(
        nameof(QuietZone),
        typeof(int),
        typeof(BarcodeVectorControl),
        new FrameworkPropertyMetadata(
            4,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowTextProperty = DependencyProperty.Register(
        nameof(ShowText),
        typeof(bool),
        typeof(BarcodeVectorControl),
        new FrameworkPropertyMetadata(
            true,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CodeFontSizeProperty = DependencyProperty.Register(
        nameof(CodeFontSize),
        typeof(double),
        typeof(BarcodeVectorControl),
        new FrameworkPropertyMetadata(
            8.5,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Typeface MonospaceTypeface = new(
        new FontFamily("Consolas, Courier New"),
        FontStyles.Normal,
        FontWeights.SemiBold,
        FontStretches.Normal);

    public BarcodeVectorControl()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double BarHeight
    {
        get => (double)GetValue(BarHeightProperty);
        set => SetValue(BarHeightProperty, value);
    }

    public double BarWidth
    {
        get => (double)GetValue(BarWidthProperty);
        set => SetValue(BarWidthProperty, value);
    }

    public int QuietZone
    {
        get => (int)GetValue(QuietZoneProperty);
        set => SetValue(QuietZoneProperty, value);
    }

    public bool ShowText
    {
        get => (bool)GetValue(ShowTextProperty);
        set => SetValue(ShowTextProperty, value);
    }

    public double CodeFontSize
    {
        get => (double)GetValue(CodeFontSizeProperty);
        set => SetValue(CodeFontSizeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var geometry = Code128Encoder.BuildGeometry(Value, BarWidth, BarHeight, QuietZone);
        var textExtra = ShowText ? Math.Max(8.0, CodeFontSize + 2.5) : 0.0;
        var naturalWidth = Math.Max(20.0, geometry.TotalWidth);
        var naturalHeight = Math.Max(10.0, geometry.BarHeight + textExtra);

        var measuredWidth = !double.IsInfinity(availableSize.Width) && availableSize.Width > 0
            ? Math.Min(naturalWidth, availableSize.Width)
            : naturalWidth;

        var measuredHeight = !double.IsInfinity(availableSize.Height) && availableSize.Height > 0
            ? Math.Min(naturalHeight, availableSize.Height)
            : naturalHeight;

        return new Size(measuredWidth, measuredHeight);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var actualW = ActualWidth;
        var actualH = ActualHeight;
        if (actualW <= 2 || actualH <= 2)
            return;

        var geometry = Code128Encoder.BuildGeometry(Value, BarWidth, BarHeight, QuietZone);
        if (geometry.TotalWidth <= 0 || geometry.Bars.Count == 0)
            return;

        var textHeight = ShowText ? Math.Min(Math.Max(8.0, CodeFontSize + 2.5), actualH * 0.35) : 0.0;
        var renderBarHeight = Math.Max(6.0, Math.Min(geometry.BarHeight, actualH - textHeight));

        var scaleX = geometry.TotalWidth > actualW ? actualW / geometry.TotalWidth : 1.0;
        var renderedBarcodeWidth = geometry.TotalWidth * scaleX;
        var offsetX = Math.Max(0.0, (actualW - renderedBarcodeWidth) / 2.0);

        foreach (var bar in geometry.Bars)
        {
            var x = offsetX + bar.X * scaleX;
            var w = Math.Max(0.5, bar.Width * scaleX);
            drawingContext.DrawRectangle(Brushes.Black, null, new Rect(x, 0, w, renderBarHeight));
        }

        if (ShowText && renderBarHeight + 5.0 <= actualH)
        {
            var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var fontSize = Math.Clamp(CodeFontSize, 6.0, Math.Max(6.5, textHeight - 1.5));
            var formatted = new FormattedText(
                geometry.CleanText,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                MonospaceTypeface,
                fontSize,
                Brushes.Black,
                pixelsPerDip)
            {
                MaxTextWidth = Math.Max(10.0, actualW),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center
            };

            drawingContext.DrawText(formatted, new Point(0, renderBarHeight + 1.0));
        }
    }
}
