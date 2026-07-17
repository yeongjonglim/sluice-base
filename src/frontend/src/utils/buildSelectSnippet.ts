import { quoteSqlIdentifier } from "@/utils/quoteSqlIdentifier";

/**
 * Builds the `SELECT … FROM … LIMIT 1000;` snippet inserted by the schema
 * sidebar's generate-query action. Every schema, table, and column name is run
 * through {@link quoteSqlIdentifier} so mixed-case, special-character, and
 * reserved-word identifiers (e.g. `__EFMigrationHistory`, `group`) reference the
 * real object instead of a lowercase-folded name that doesn't exist.
 */
export function buildSelectSnippet(
  schemaName: string,
  tableName: string,
  columnNames: Array<string>,
): string {
  const colList = columnNames.map(quoteSqlIdentifier).join(", ");
  const target = `${quoteSqlIdentifier(schemaName)}.${quoteSqlIdentifier(tableName)}`;
  return `SELECT ${colList}\nFROM ${target}\nLIMIT 1000;\n`;
}
