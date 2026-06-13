namespace SluiceBase.Core.Schemas;

public sealed record SchemaTree(
    IReadOnlyList<SchemaInfo> Schemas,
    IReadOnlyList<PrimaryKey> PrimaryKeys,
    IReadOnlyList<ForeignKey> ForeignKeys);

public sealed record SchemaInfo(string Name, IReadOnlyList<TableInfo> Tables);
public sealed record TableInfo(string Name, IReadOnlyList<ColumnInfo> Columns);
public sealed record ColumnInfo(string Name, string DataType, bool IsNullable, bool IsSensitive = false, bool IsRestricted = false);

public sealed record PrimaryKey(
    string Schema,
    string Table,
    IReadOnlyList<string> Columns);

public sealed record ForeignKey(
    string ConstraintName,
    string Schema,
    string Table,
    IReadOnlyList<string> Columns,
    string ReferencedSchema,
    string ReferencedTable,
    IReadOnlyList<string> ReferencedColumns);
