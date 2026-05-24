using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class SensitiveColumn
{
#pragma warning disable CS8618
    private SensitiveColumn() { }
#pragma warning restore CS8618

    private SensitiveColumn(
        SensitiveColumnId id, DatabaseId databaseId,
        string schemaName, string tableName, string columnName,
        UserId? markedById, DateTimeOffset at)
    {
        Id = id;
        DatabaseId = databaseId;
        SchemaName = schemaName;
        TableName = tableName;
        ColumnName = columnName;
        MarkedById = markedById;
        MarkedAt = at;
    }

    public SensitiveColumnId Id { get; private set; }
    public DatabaseId DatabaseId { get; private set; }
    public string SchemaName { get; private set; }
    public string TableName { get; private set; }
    public string ColumnName { get; private set; }
    public DateTimeOffset MarkedAt { get; private set; }
    public UserId? MarkedById { get; private set; }

    public static SensitiveColumn Mark(
        DatabaseId databaseId, string schemaName, string tableName, string columnName,
        UserId? markedById, DateTimeOffset at) =>
        new(SensitiveColumnId.FromNewVersion7Guid(), databaseId,
            schemaName, tableName, columnName, markedById, at);
}
