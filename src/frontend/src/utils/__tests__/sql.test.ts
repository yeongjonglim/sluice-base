import { describe, expect, it } from "vitest";
import { quoteIdentifier } from "@/utils/sql.ts";

describe("quoteIdentifier", () => {
  it("leaves simple lowercase identifiers unquoted", () => {
    expect(quoteIdentifier("users")).toBe("users");
  });

  it("leaves lowercase identifiers with underscores unquoted", () => {
    expect(quoteIdentifier("user_name")).toBe("user_name");
  });

  it("leaves identifiers starting with underscore unquoted", () => {
    expect(quoteIdentifier("_private")).toBe("_private");
  });

  it("quotes identifiers with uppercase letters", () => {
    expect(quoteIdentifier("Superman")).toBe('"Superman"');
  });

  it("quotes EF migration history table name with leading underscores and mixed case", () => {
    expect(quoteIdentifier("__EFMigrationHistory")).toBe('"__EFMigrationHistory"');
  });

  it("quotes identifiers containing dollar sign", () => {
    expect(quoteIdentifier("Count$er")).toBe('"Count$er"');
  });

  it("quotes identifiers starting with a digit", () => {
    expect(quoteIdentifier("0aaf")).toBe('"0aaf"');
  });

  it("quotes identifiers containing spaces", () => {
    expect(quoteIdentifier("my table")).toBe('"my table"');
  });

  it("quotes identifiers containing hyphens", () => {
    expect(quoteIdentifier("my-table")).toBe('"my-table"');
  });

  it("escapes embedded double-quotes by doubling them", () => {
    expect(quoteIdentifier('say"hi')).toBe('"say""hi"');
  });

  it("leaves all-lowercase identifiers with digits unquoted", () => {
    expect(quoteIdentifier("table1")).toBe("table1");
  });
});
