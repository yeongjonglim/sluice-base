using Microsoft.Extensions.Logging;

namespace SluiceBase.Core.Branding;

public sealed class BrandingOptions
{
    public const string SectionName = "Branding";

    private static readonly HashSet<string> ValidMantineColors =
    [
        "dark", "gray", "red", "pink", "grape", "violet", "indigo",
        "blue", "cyan", "green", "lime", "yellow", "orange", "teal"
    ];

    public string AppName { get; init; } = "SluiceBase";
    public string PrimaryColor { get; init; } = "teal";
    public string LogoUrl { get; init; } = "";
    public string FaviconUrl { get; init; } = "";

    public string GetValidatedPrimaryColor(ILogger? logger = null)
    {
        if (ValidMantineColors.Contains(PrimaryColor))
        {
            return PrimaryColor;
        }

        logger?.WarningInvalidColour(PrimaryColor);
        return "teal";
    }
}

public static partial class LoggerMessage
{
    [LoggerMessage(
        LogLevel.Warning,
        Message = "Branding:PrimaryColor '{Color}' is not a valid Mantine colour name. Falling back to 'teal'.")]
    public static partial void WarningInvalidColour(this ILogger logger, string color);
}