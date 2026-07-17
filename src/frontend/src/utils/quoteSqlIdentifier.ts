// A bare (unquoted) SQL identifier is safe only when it is a lowercase
// [a-z_][a-z0-9_]* token AND is not a reserved keyword. PostgreSQL folds any
// unquoted identifier to lowercase, so anything mixed-case, containing special
// characters, starting with a digit, or colliding with a reserved word must be
// double-quoted to reference the real object (e.g. "__EFMigrationHistory").
const SAFE_IDENTIFIER = /^[a-z_][a-z0-9_]*$/;

// PostgreSQL reserved keywords — categories R (reserved) and T (reserved, can be
// function or type name). These are exactly the words that cannot be used as a
// bare table/column/schema name; non-reserved keywords (categories C and U) are
// left alone so the common case stays clean.
//
// Generated from postgres:18.3 (the image Aspire.Hosting.PostgreSQL 13.4.6
// provisions) — regenerate with:
//   SELECT word FROM pg_get_keywords() WHERE catcode IN ('R','T') ORDER BY word;
const RESERVED_KEYWORDS: ReadonlySet<string> = new Set([
  "all", "analyse", "analyze", "and", "any", "array", "as", "asc",
  "asymmetric", "authorization", "binary", "both", "case", "cast", "check",
  "collate", "collation", "column", "concurrently", "constraint", "create",
  "cross", "current_catalog", "current_date", "current_role", "current_schema",
  "current_time", "current_timestamp", "current_user", "default", "deferrable",
  "desc", "distinct", "do", "else", "end", "except", "false", "fetch", "for",
  "foreign", "freeze", "from", "full", "grant", "group", "having", "ilike",
  "in", "initially", "inner", "intersect", "into", "is", "isnull", "join",
  "lateral", "leading", "left", "like", "limit", "localtime", "localtimestamp",
  "natural", "not", "notnull", "null", "offset", "on", "only", "or", "order",
  "outer", "overlaps", "placing", "primary", "references", "returning", "right",
  "select", "session_user", "similar", "some", "symmetric", "system_user",
  "table", "tablesample", "then", "to", "trailing", "true", "union", "unique",
  "user", "using", "variadic", "verbose", "when", "where", "window", "with",
]);

/**
 * Quotes a SQL identifier for PostgreSQL when — and only when — it needs it.
 * Safe lowercase identifiers pass through untouched; everything else is wrapped
 * in double quotes with any embedded `"` doubled to `""`.
 */
export function quoteSqlIdentifier(name: string): string {
  if (SAFE_IDENTIFIER.test(name) && !RESERVED_KEYWORDS.has(name)) {
    return name;
  }
  return `"${name.replace(/"/g, '""')}"`;
}
