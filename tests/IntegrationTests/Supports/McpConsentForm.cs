using System.Text.RegularExpressions;
using System.Web;

namespace IntegrationTests.Supports;

/// <summary>Reads the MCP consent page's hidden form fields (authorize parameters + antiforgery token).</summary>
public static partial class McpConsentForm
{
    public static Dictionary<string, string> Fields(string html, string decision)
    {
        var fields = HiddenInputRegex().Matches(html)
            .ToDictionary(m => HttpUtility.HtmlDecode(m.Groups[1].Value), m => HttpUtility.HtmlDecode(m.Groups[2].Value));
        fields["decision"] = decision;
        return fields;
    }

    [GeneratedRegex("""<input type="hidden" name="([^"]+)" value="([^"]*)">""")]
    private static partial Regex HiddenInputRegex();
}
