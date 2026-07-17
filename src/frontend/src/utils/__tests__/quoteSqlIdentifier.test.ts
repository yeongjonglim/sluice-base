import { describe, expect, it } from "vitest";
import { quoteSqlIdentifier } from "@/utils/quoteSqlIdentifier";

describe("quoteSqlIdentifier", () => {
  it("leaves a safe lowercase identifier unquoted", () => {
    expect(quoteSqlIdentifier("users")).toBe("users");
    expect(quoteSqlIdentifier("id")).toBe("id");
    expect(quoteSqlIdentifier("public")).toBe("public");
    expect(quoteSqlIdentifier("_private")).toBe("_private");
    expect(quoteSqlIdentifier("col_1")).toBe("col_1");
  });

  it("quotes identifiers containing uppercase letters", () => {
    expect(quoteSqlIdentifier("__EFMigrationHistory")).toBe('"__EFMigrationHistory"');
    expect(quoteSqlIdentifier("MigrationId")).toBe('"MigrationId"');
    expect(quoteSqlIdentifier("ProductVersion")).toBe('"ProductVersion"');
  });

  it("quotes reserved keywords even when all-lowercase", () => {
    expect(quoteSqlIdentifier("group")).toBe('"group"');
    expect(quoteSqlIdentifier("order")).toBe('"order"');
    expect(quoteSqlIdentifier("select")).toBe('"select"');
    expect(quoteSqlIdentifier("user")).toBe('"user"');
    expect(quoteSqlIdentifier("table")).toBe('"table"');
  });

  it("does not quote non-reserved keywords usable as identifiers", () => {
    // "name" and "value" are non-reserved in PostgreSQL — valid bare column names.
    expect(quoteSqlIdentifier("name")).toBe("name");
    expect(quoteSqlIdentifier("value")).toBe("value");
  });

  it("quotes identifiers with special characters or a leading digit", () => {
    expect(quoteSqlIdentifier("weird-name")).toBe('"weird-name"');
    expect(quoteSqlIdentifier("2fa")).toBe('"2fa"');
    expect(quoteSqlIdentifier("has space")).toBe('"has space"');
    expect(quoteSqlIdentifier("has$dollar")).toBe('"has$dollar"');
  });

  it("escapes embedded double quotes by doubling them", () => {
    expect(quoteSqlIdentifier('a"b')).toBe('"a""b"');
    expect(quoteSqlIdentifier('"')).toBe('""""');
  });

  it("quotes an empty identifier", () => {
    expect(quoteSqlIdentifier("")).toBe('""');
  });
});
