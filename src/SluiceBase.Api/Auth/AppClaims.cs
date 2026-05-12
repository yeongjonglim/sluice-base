using System.Globalization;
using System.Security.Claims;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Auth;

internal static class AppClaims
{
    private static string Sub => "sub";

    private static List<string> Email =>
    [
        "email",
        "preferred_username",
        "upn",
        "mail",
        // https://stackoverflow.com/questions/51976600/unique-name-claim-which-user-attribute
        "unique_name"
    ];

    private static List<List<string>> Name =>
    [
        ["name"],
        ["display_name"],
        ["given_name", "family_name"]
    ];

    public const string InternalUserIdClaim = "sb:uid";

    extension(ClaimsPrincipal principal)
    {
        public UserId GetInternalUserId()
        {
            var userId = principal.FindFirstValue(InternalUserIdClaim);
            return string.IsNullOrEmpty(userId)
                ? throw new InvalidOperationException("No internal user id claim found.")
                : UserId.Parse(userId, CultureInfo.InvariantCulture);
        }

        public IEnumerable<ClaimRecord> GetClaims() => principal.Claims.Select(c => new ClaimRecord(c.Type, c.Value, c.ValueType));

        public string GetSubject() => principal.FindFirstValue(Sub) ?? throw new InvalidOperationException("No sub claim found."); // Sub is required

        public string? GetEmail()
        {
            return Email.Select(principal.FindFirstValue).FirstOrDefault(found => !string.IsNullOrEmpty(found));
        }

        public string? GetName()
        {
            foreach (var name in Name)
            {
                var enumerable = name.Select(principal.FindFirstValue).ToList();
                if (enumerable.All(f => !string.IsNullOrEmpty(f)))
                {
                    return string.Join(" ", enumerable);
                }
            }

            return null;
        }

        public string GetIssuer() => principal.FindFirstValue("iss") ?? throw new InvalidOperationException("No issuer claim found.");
    }
}