using SluiceBase.Core.Servers;

namespace SluiceBase.Api.Servers;

internal static class DatabaseQueryExtensions
{
    // Soft-deleted rows are kept for history, so every lookup that grants access or connects to a database
    // has to leave them out explicitly.
    public static IQueryable<Database> Live(this IQueryable<Database> databases) =>
        databases.Where(d => d.DeletedAt == null && d.Server!.DeletedAt == null);
}
