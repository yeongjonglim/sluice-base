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

    public const string Prefix = "sb:";
    public const string InternalUserIdClaim = Prefix + "uid";
    public const string SignedInAtClaim = Prefix + "signed_in_at";

    extension(ClaimsPrincipal principal)
    {
        public bool TryGetInternalUserId(out UserId userId)
        {
            var userIdValue = principal.FindFirstValue(InternalUserIdClaim);
            if (!string.IsNullOrEmpty(userIdValue) && UserId.TryParse(userIdValue, out userId))
            {
                return true;
            }

            userId = UserId.Unknown;
            return false;
        }

        public DateTimeOffset? GetSignedInAt() =>
            long.TryParse(principal.FindFirstValue(SignedInAtClaim), out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;

        public IEnumerable<ClaimRecord> GetClaims() => principal.Claims.Select(c => new ClaimRecord(c.Type, c.Value, c.ValueType));

        public string GetSubject() => principal.FindFirstValue(Sub) ?? throw new InvalidOperationException("No sub claim found."); // Sub is required

        public string? GetEmail()
        {
            return Email.Select(principal.FindFirstValue).FirstOrDefault(found => !string.IsNullOrEmpty(found));
        }

        // Only the IdP's own verified email claim, never the username-style fallbacks GetEmail accepts, since a
        // user may be able to choose those (or an unverified email) to look like someone else.
        public string? GetVerifiedEmail() =>
            string.Equals(principal.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase)
                ? principal.FindFirstValue("email")
                : null;

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