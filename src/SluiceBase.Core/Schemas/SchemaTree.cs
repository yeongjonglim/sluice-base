namespace SluiceBase.Core.Schemas;

public sealed record SchemaTree(IReadOnlyList<SchemaInfo> Schemas);
public sealed record SchemaInfo(string Name, IReadOnlyList<TableInfo> Tables);
public sealed record TableInfo(string Name, IReadOnlyList<ColumnInfo> Columns);
public sealed record ColumnInfo(string Name, string DataType, bool IsNullable, bool IsSensitive = false, bool IsRestricted = false);