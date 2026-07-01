namespace SluiceBase.Core.Schemas;

public sealed record SchemaTree(
    IReadOnlyList<SchemaInfo> Schemas,
    IReadOnlyList<ExtensionInfo> Extensions);

public sealed record SchemaInfo(
    string Name,
    IReadOnlyList<TableInfo> Tables,
    IReadOnlyList<ViewInfo> Views,
    IReadOnlyList<MaterializedViewInfo> MaterializedViews,
    IReadOnlyList<RoutineInfo> Routines,
    IReadOnlyList<SequenceInfo> Sequences,
    IReadOnlyList<TypeInfo> Types);

public sealed record TableInfo(
    string Name,
    IReadOnlyList<ColumnInfo> Columns,
    PrimaryKey? PrimaryKey,
    IReadOnlyList<ForeignKey> ForeignKeys,
    IReadOnlyList<IndexInfo> Indexes);

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

public sealed record ViewInfo(string Name, IReadOnlyList<ColumnInfo> Columns);

public sealed record MaterializedViewInfo(
    string Name,
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<IndexInfo> Indexes);

// A function or procedure. Signature is the rendered argument list (e.g. "uid integer");
// it carries the parameter metadata directly, so no separate parameter records are needed.
public sealed record RoutineInfo(
    string Name,
    string Kind,
    string? ReturnType,
    string Language,
    string Signature);

public sealed record SequenceInfo(
    string Name,
    string DataType,
    long Start,
    long Increment,
    long MinValue,
    long MaxValue,
    bool Cycle,
    string? OwnedByColumn);

// A user-defined type. Kind selects which optional payload is populated: EnumLabels for
// enums, Attributes for composites, BaseType for domains; ranges carry none.
public sealed record TypeInfo(
    string Name,
    string Kind,
    IReadOnlyList<string>? EnumLabels,
    IReadOnlyList<string>? Attributes,
    string? BaseType);

public sealed record IndexInfo(
    string Name,
    IReadOnlyList<string> Columns,
    bool IsUnique,
    bool IsPrimary,
    string Method);

public sealed record ExtensionInfo(string Name, string Version, string Schema);
