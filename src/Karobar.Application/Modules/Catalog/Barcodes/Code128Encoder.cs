using System.Text;

namespace Karobar.Application.Barcodes;

public readonly record struct BarcodeBarSegment(double X, double Width);

public sealed class BarcodeLayoutGeometry
{
    public string CleanText { get; init; } = "000000";
    public IReadOnlyList<BarcodeBarSegment> Bars { get; init; } = [];
    public double TotalWidth { get; init; }
    public double BarHeight { get; init; }
}

/// <summary>
/// Zero-dependency pure vector Code 128 (Auto Mode B/C) barcode encoder.
/// Produces exact module runs for crisp screen preview and native Windows vector printing.
/// </summary>
public static class Code128Encoder
{
    private static readonly string[] Patterns =
    [
        "212222","222122","222221","121223","121322","131222","122213","122312","132212","221213",
        "221312","231212","112232","122132","122231","113222","123122","123221","223211","221132",
        "221231","213212","223112","312131","311222","321122","321221","312212","322112","322211",
        "212123","212321","232121","111323","131123","131321","112313","132113","132311","211313",
        "231113","231311","112133","112331","132131","113123","113321","133121","313121","211331",
        "231131","213113","213311","213131","311123","311321","331121","312113","312311","332111",
        "314111","221411","431111","111224","111422","121124","121421","141122","141221","112214",
        "112412","122114","122411","142112","142211","241211","221114","413111","241112","134111",
        "111242","121142","121241","114212","124112","124211","411212","421112","421211","212141",
        "214121","412121","111143","111341","131141","114113","114311","411113","411311","113141",
        "114131","311141","411131","211412","211214","211232","2331112"
    ];

    private const int CodeStartB = 104;
    private const int CodeStartC = 105;
    private const int CodeStop = 106;
    private const int CodeToB = 100;
    private const int CodeToC = 99;

    public static string SanitizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "000000";

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch >= 0x20 && ch <= 0x7E)
                sb.Append(ch);
        }

        var cleaned = sb.ToString().Trim();
        return string.IsNullOrEmpty(cleaned) ? "000000" : cleaned;
    }

    public static List<int> EncodeValues(string? text)
    {
        var clean = SanitizeText(text);
        var values = new List<int>(clean.Length + 4);
        var i = 0;
        char currentMode;

        if (HasConsecutiveAsciiDigits(clean, 0, 4))
        {
            currentMode = 'C';
            values.Add(CodeStartC);
        }
        else
        {
            currentMode = 'B';
            values.Add(CodeStartB);
        }

        while (i < clean.Length)
        {
            if (currentMode == 'C')
            {
                if (HasConsecutiveAsciiDigits(clean, i, 2))
                {
                    var pair = (clean[i] - '0') * 10 + (clean[i + 1] - '0');
                    values.Add(pair);
                    i += 2;
                }
                else
                {
                    currentMode = 'B';
                    values.Add(CodeToB);
                }
            }
            else
            {
                if (HasConsecutiveAsciiDigits(clean, i, 4))
                {
                    currentMode = 'C';
                    values.Add(CodeToC);
                }
                else
                {
                    var code = clean[i] - 32;
                    values.Add(code is >= 0 and <= 95 ? code : 0);
                    i++;
                }
            }
        }

        long checksum = values[0];
        for (var pos = 1; pos < values.Count; pos++)
        {
            checksum += (long)values[pos] * pos;
        }

        values.Add((int)(checksum % 103));
        values.Add(CodeStop);
        return values;
    }

    public static string ValuesToBinary(IReadOnlyList<int> values)
    {
        var sb = new StringBuilder(values.Count * 11 + 2);
        foreach (var val in values)
        {
            if (val < 0 || val >= Patterns.Length)
                continue;

            var pattern = Patterns[val];
            var isBar = true;
            foreach (var ch in pattern)
            {
                var width = ch - '0';
                if (width > 0)
                    sb.Append(isBar ? '1' : '0', width);
                isBar = !isBar;
            }
        }

        return sb.ToString();
    }

    public static BarcodeLayoutGeometry BuildGeometry(
        string? text,
        double barWidth = 1.3,
        double barHeight = 28.0,
        int quietZoneModules = 6)
    {
        var safeBarWidth = Math.Clamp(barWidth, 0.6, 5.0);
        var safeBarHeight = Math.Clamp(barHeight, 8.0, 200.0);
        var safeQuietZone = Math.Clamp(quietZoneModules, 0, 20);

        var clean = SanitizeText(text);
        var values = EncodeValues(clean);
        var binary = ValuesToBinary(values);

        var bars = new List<BarcodeBarSegment>(binary.Length / 2);
        var inBar = false;
        var barStart = 0;

        for (var i = 0; i < binary.Length; i++)
        {
            if (binary[i] == '1')
            {
                if (!inBar)
                {
                    inBar = true;
                    barStart = i;
                }
            }
            else if (inBar)
            {
                inBar = false;
                bars.Add(new BarcodeBarSegment(
                    (safeQuietZone + barStart) * safeBarWidth,
                    (i - barStart) * safeBarWidth));
            }
        }

        if (inBar)
        {
            bars.Add(new BarcodeBarSegment(
                (safeQuietZone + barStart) * safeBarWidth,
                (binary.Length - barStart) * safeBarWidth));
        }

        var totalModules = binary.Length + safeQuietZone * 2;
        return new BarcodeLayoutGeometry
        {
            CleanText = clean,
            Bars = bars,
            TotalWidth = totalModules * safeBarWidth,
            BarHeight = safeBarHeight
        };
    }

    private static bool HasConsecutiveAsciiDigits(string s, int startIndex, int count)
    {
        if (startIndex + count > s.Length)
            return false;

        for (var k = 0; k < count; k++)
        {
            var c = s[startIndex + k];
            if (c < '0' || c > '9')
                return false;
        }

        return true;
    }
}
