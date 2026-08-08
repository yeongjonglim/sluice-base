using SluiceBase.Api.Services;

namespace SluiceBase.Api.Mcp;

// Structured result returned by run_query when a query is blocked by the sensitive-column
// policy. The MCP SDK serializes a returned object into the tool result's text content —
// that content is what the client model reads — so the guidance lives here, not in a thrown
// exception. This is best-effort deterrence for cooperative agents; the actual enforcement
// is SensitiveColumnGuard, which hard-rejects and logs every blocked query server-side.
//
// blockedColumns carries column identities only (never values) so a cooperative agent can
// drop exactly those columns and re-run.
internal sealed record SensitiveColumnBlockPayload(
    string Error,
    IReadOnlyList<string> BlockedColumns,
    string Guidance)
{
    private const string ErrorDiscriminator = "sensitive_columns_blocked";

    private const string GuidanceText =
        "These columns are restricted by policy and cannot be returned. Re-run the query " +
        "without them. Do not attempt to work around this restriction (SELECT *, column " +
        "aliases, casts, row-serialization functions, or resubmitting the same request) — " +
        "every variation is blocked identically and each attempt is recorded. You cannot " +
        "override this from here; only an operator can grant a column bypass.";

    public static SensitiveColumnBlockPayload From(
        IReadOnlyList<BlockedColumn> blockedColumns, string? reason = null) =>
        new(
            ErrorDiscriminator,
            [.. blockedColumns.Select(c => $"{c.Schema}.{c.Table}.{c.Column}")],
            reason is null ? GuidanceText : $"{reason} {GuidanceText}");
}
