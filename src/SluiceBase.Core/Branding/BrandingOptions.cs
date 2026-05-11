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

    public string GetValidatedPrimaryColor(ILogger logger)
    {
        if (ValidMantineColors.Contains(PrimaryColor))
        {
            return PrimaryColor;
        }
#pragma warning disable CA1848
        logger.LogWarning(
            "Branding:PrimaryColor '{Color}' is not a valid Mantine colour name. Falling back to 'teal'.",
            PrimaryColor);
#pragma warning restore CA1848
        return "teal";
    }
}
