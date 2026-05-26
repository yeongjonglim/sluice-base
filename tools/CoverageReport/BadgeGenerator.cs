using System.Globalization;
using System.Text;

namespace CoverageReport;

public static class BadgeGenerator
{
    private static readonly CompositeFormat BadgeFormat = CompositeFormat.Parse("""
        <svg xmlns="http://www.w3.org/2000/svg" width="{0}" height="20">
          <linearGradient id="b" x2="0" y2="100%">
            <stop offset="0" stop-color="#bbb" stop-opacity=".1"/>
            <stop offset="1" stop-opacity=".1"/>
          </linearGradient>
          <clipPath id="a">
            <rect width="{0}" height="20" rx="3" fill="#fff"/>
          </clipPath>
          <g clip-path="url(#a)">
            <rect width="{1}" height="20" fill="#555"/>
            <rect x="{1}" width="{2}" height="20" fill="{3}"/>
            <rect width="{0}" height="20" fill="url(#b)"/>
          </g>
          <g fill="#fff" text-anchor="middle" font-family="DejaVu Sans,Verdana,Geneva,sans-serif" font-size="11">
            <text x="{4}" y="15" fill="#010101" fill-opacity=".3">{5}</text>
            <text x="{4}" y="14">{5}</text>
            <text x="{6}" y="15" fill="#010101" fill-opacity=".3">{7}%</text>
            <text x="{6}" y="14">{7}%</text>
          </g>
        </svg>
        """);

    public static string GetColor(double rate) => rate switch
    {
        >= 70 => "#4c1",
        >= 50 => "#dfb317",
        _ => "#e05d44"
    };

    public static string Generate(string label, double rate)
    {
        var color = GetColor(rate);
        var value = rate.ToString("F1", CultureInfo.InvariantCulture);
        var labelWidth = label.Length * 7 + 10;
        var valueWidth = value.Length * 7 + 18;
        var totalWidth = labelWidth + valueWidth;

        return string.Format(
            CultureInfo.InvariantCulture,
            BadgeFormat,
            totalWidth,
            labelWidth,
            valueWidth,
            color,
            labelWidth / 2.0,
            label,
            labelWidth + valueWidth / 2.0,
            value);
    }
}
