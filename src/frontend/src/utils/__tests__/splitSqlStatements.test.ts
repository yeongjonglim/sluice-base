import { describe, expect, it } from "vitest";
import { splitSqlStatements } from "@/utils/splitSqlStatements";

describe("splitSqlStatements", () => {
  it("returns empty array for blank input", () => {
    expect(splitSqlStatements("   \n\t ")).toEqual([]);
  });

  it("ignores comment-only input", () => {
    expect(splitSqlStatements("-- just a comment\n/* block */")).toEqual([]);
  });

  it("splits on semicolons and trims each statement", () => {
    const s = splitSqlStatements("SELECT 1;\nSELECT 2;");
    expect(s.map((x) => x.text)).toEqual(["SELECT 1", "SELECT 2"]);
  });

  it("keeps a trailing statement without a terminator", () => {
    const s = splitSqlStatements("SELECT 1;\nSELECT 2");
    expect(s.map((x) => x.text)).toEqual(["SELECT 1", "SELECT 2"]);
  });

  it("does not split on a semicolon inside a string literal", () => {
    const s = splitSqlStatements("SELECT ';' AS a; SELECT 2");
    expect(s.map((x) => x.text)).toEqual(["SELECT ';' AS a", "SELECT 2"]);
  });

  it("handles doubled single-quote escapes inside a literal", () => {
    const s = splitSqlStatements("SELECT 'it''s; ok' AS a; SELECT 2");
    expect(s.map((x) => x.text)).toEqual(["SELECT 'it''s; ok' AS a", "SELECT 2"]);
  });

  it("does not split inside a $$ dollar-quoted body", () => {
    const s = splitSqlStatements("DO $$ BEGIN raise notice ';'; END $$; SELECT 2");
    expect(s.map((x) => x.text)).toEqual([
      "DO $$ BEGIN raise notice ';'; END $$",
      "SELECT 2",
    ]);
  });

  it("does not split inside a tagged $tag$ dollar-quoted body", () => {
    const s = splitSqlStatements("SELECT $body$a;b$body$ AS x; SELECT 2");
    expect(s.map((x) => x.text)).toEqual(["SELECT $body$a;b$body$ AS x", "SELECT 2"]);
  });

  it("does not split on a semicolon inside a line comment", () => {
    const s = splitSqlStatements("SELECT 1 -- a;b\n; SELECT 2");
    expect(s.map((x) => x.text)).toEqual(["SELECT 1 -- a;b", "SELECT 2"]);
  });

  it("does not split on a semicolon inside a block comment", () => {
    const s = splitSqlStatements("SELECT 1 /* a;b */; SELECT 2");
    expect(s.map((x) => x.text)).toEqual(["SELECT 1 /* a;b */", "SELECT 2"]);
  });

  it("reports absolute char and 1-based line ranges", () => {
    const sql = "SELECT 1;\nSELECT 2;";
    const s = splitSqlStatements(sql);
    expect(s[0]).toMatchObject({ fromPos: 0, toPos: 8, fromLine: 1, toLine: 1 });
    expect(s[1]).toMatchObject({ fromPos: 10, toPos: 18, fromLine: 2, toLine: 2 });
    expect(sql.slice(s[1].fromPos, s[1].toPos)).toBe("SELECT 2");
  });
});
