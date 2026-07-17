import { describe, expect, it } from "vitest";
import { buildSelectSnippet } from "@/utils/buildSelectSnippet";

describe("buildSelectSnippet", () => {
  it("leaves safe lowercase identifiers unquoted", () => {
    expect(buildSelectSnippet("public", "users", ["id", "name"])).toBe(
      "SELECT id, name\nFROM public.users\nLIMIT 1000;\n",
    );
  });

  it("quotes mixed-case table and column identifiers", () => {
    expect(
      buildSelectSnippet("public", "__EFMigrationHistory", [
        "MigrationId",
        "ProductVersion",
      ]),
    ).toBe(
      'SELECT "MigrationId", "ProductVersion"\nFROM public."__EFMigrationHistory"\nLIMIT 1000;\n',
    );
  });

  it("quotes reserved-keyword columns while leaving others bare", () => {
    expect(buildSelectSnippet("public", "orders", ["id", "group"])).toBe(
      'SELECT id, "group"\nFROM public.orders\nLIMIT 1000;\n',
    );
  });

  it("quotes a mixed-case schema name", () => {
    expect(buildSelectSnippet("MySchema", "users", ["id"])).toBe(
      'SELECT id\nFROM "MySchema".users\nLIMIT 1000;\n',
    );
  });
});
