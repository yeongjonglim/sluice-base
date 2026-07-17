import { describe, expect, it } from "vitest";
import { EditorState } from "@codemirror/state";
import { CompletionContext } from "@codemirror/autocomplete";
import { PostgreSQL, schemaCompletionSource } from "@codemirror/lang-sql";
import { schemaToCompletions } from "@/api/hooks";

// Drives the real @codemirror/lang-sql completion source the SqlEditor uses, so
// these assertions reflect what actually gets inserted into the editor — not the
// internal completion structure. `apply` is the text inserted on accept; when it
// is absent the bare `label` is inserted.
const tree = {
  schemas: [
    {
      name: "public",
      tables: [
        {
          name: "users",
          columns: [
            { name: "id", isRestricted: false },
            { name: "group", isRestricted: false },
          ],
        },
        {
          name: "order", // reserved-word table name
          columns: [{ name: "id", isRestricted: false }],
        },
        {
          name: "__EFMigrationHistory",
          columns: [
            { name: "MigrationId", isRestricted: false },
            { name: "ProductVersion", isRestricted: false },
          ],
        },
      ],
    },
  ],
};

function completionsAt(doc: string): Array<{ label: string; apply?: string }> {
  const schema = schemaToCompletions(tree);
  const source = schemaCompletionSource({ dialect: PostgreSQL, schema });
  const state = EditorState.create({ doc, extensions: [PostgreSQL.language] });
  const ctx = new CompletionContext(state, doc.length, true);
  const result = source(ctx) as { options: Array<{ label: string; apply?: unknown }> } | null;
  return (result?.options ?? []).map((o) => ({
    label: o.label,
    apply: typeof o.apply === "string" ? o.apply : undefined,
  }));
}

describe("autocomplete identifier quoting", () => {
  it("inserts a reserved-word column quoted, leaving safe columns bare", () => {
    const cols = completionsAt("SELECT * FROM public.users WHERE public.users.");
    expect(cols).toContainEqual({ label: "id", apply: undefined });
    expect(cols).toContainEqual({ label: "group", apply: '"group"' });
  });

  it("inserts mixed-case columns quoted", () => {
    const cols = completionsAt(
      'SELECT * FROM public."__EFMigrationHistory" WHERE public."__EFMigrationHistory".',
    );
    expect(cols).toContainEqual({ label: "MigrationId", apply: '"MigrationId"' });
    expect(cols).toContainEqual({ label: "ProductVersion", apply: '"ProductVersion"' });
  });

  it("inserts reserved-word and mixed-case table names quoted, safe ones bare", () => {
    const tables = completionsAt("SELECT * FROM public.");
    expect(tables).toContainEqual({ label: "users", apply: undefined });
    expect(tables).toContainEqual({ label: "order", apply: '"order"' });
    expect(tables).toContainEqual({
      label: "__EFMigrationHistory",
      apply: '"__EFMigrationHistory"',
    });
  });

  it("offers the schema name at the top level", () => {
    const schemas = completionsAt("SELECT * FROM ");
    expect(schemas).toContainEqual({ label: "public", apply: undefined });
  });
});
