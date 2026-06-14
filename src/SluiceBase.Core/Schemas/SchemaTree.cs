namespace SluiceBase.Core.Schemas;

public sealed record SchemaTree(IReadOnlyList<SchemaInfo> Schemas);

public sealed record SchemaInfo(string Name, IReadOnlyList<TableInfo> Tables);

public sealed record TableInfo(
    string Name,
    IReadOnlyList<ColumnInfo> Columns,
    PrimaryKey? PrimaryKey,
    IReadOnlyList<ForeignKey> ForeignKeys);

public sealed record ColumnInfo(string Name, string DataType, bool IsNullable, bool IsSensitive = false, bool IsRestricted = false);

// A table's primary key: the ordered columns that compose it. Schema/table identity is
// implied by the owning TableInfo.
public sealed record PrimaryKey(IReadOnlyList<string> Columns);

// An outbound foreign key on the owning table. Columns are this table's columns; the
// referenced* fields point at the parent table (possibly in another schema).
public sealed record ForeignKey(
    string ConstraintName,
    IReadOnlyList<string> Columns,
    string ReferencedSchema,
    string ReferencedTable,
    IReadOnlyList<string> ReferencedColumns);
