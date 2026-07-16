export interface SqlStatement {
  text: string;
  fromPos: number;
  toPos: number;
  fromLine: number;
  toLine: number;
}

type Mode = "normal" | "single" | "lineComment" | "blockComment" | "dollar";

// At a '$', match a dollar-quote opener like $$ or $tag$ and return the full
// tag (including both $). Returns null when it's not a dollar quote (e.g. $1).
function matchDollarTag(sql: string, i: number): string | null {
  let j = i + 1;
  while (j < sql.length && /[A-Za-z0-9_]/.test(sql[j])) j++;
  return sql[j] === "$" ? sql.slice(i, j + 1) : null;
}

export function splitSqlStatements(sql: string): Array<SqlStatement> {
  const statements: Array<SqlStatement> = [];
  const n = sql.length;

  // Precompute newline offsets once so each statement's line numbers are an
  // O(log n) binary search rather than an O(pos) rescan from the start — the
  // rescan made the whole parse O(n²) and froze typing on large scripts.
  const newlineOffsets: Array<number> = [];
  for (let k = 0; k < n; k++) {
    if (sql[k] === "\n") newlineOffsets.push(k);
  }
  const lineAt = (pos: number): number => {
    let lo = 0;
    let hi = newlineOffsets.length;
    while (lo < hi) {
      const mid = (lo + hi) >> 1;
      if (newlineOffsets[mid] < pos) lo = mid + 1;
      else hi = mid;
    }
    return lo + 1;
  };

  let segStart = 0;
  let mode: Mode = "normal";
  let dollarTag = "";
  let hasNonCommentContent = false;

  const pushSegment = (rawStart: number, rawEnd: number, hasContent: boolean) => {
    const raw = sql.slice(rawStart, rawEnd);
    const trimmed = raw.trim();
    if (trimmed.length === 0 || !hasContent) return;
    const fromPos = rawStart + (raw.length - raw.trimStart().length);
    const toPos = fromPos + trimmed.length;
    statements.push({
      text: trimmed,
      fromPos,
      toPos,
      fromLine: lineAt(fromPos),
      toLine: lineAt(toPos),
    });
  };

  let i = 0;
  while (i < n) {
    const c = sql[i];
    const next = sql[i + 1];

    if (mode === "normal") {
      if (c === "'") { mode = "single"; hasNonCommentContent = true; i++; continue; }
      if (c === "-" && next === "-") { mode = "lineComment"; i += 2; continue; }
      if (c === "/" && next === "*") { mode = "blockComment"; i += 2; continue; }
      if (c === "$") {
        const tag = matchDollarTag(sql, i);
        if (tag) { dollarTag = tag; mode = "dollar"; hasNonCommentContent = true; i += tag.length; continue; }
      }
      if (c === ";") {
        pushSegment(segStart, i, hasNonCommentContent); // exclude the ';'
        i++;
        segStart = i;
        hasNonCommentContent = false;
        continue;
      }
      if (!/\s/.test(c)) {
        hasNonCommentContent = true;
      }
      i++;
      continue;
    }

    if (mode === "single") {
      if (c === "'") {
        if (next === "'") { i += 2; continue; } // escaped quote
        mode = "normal"; i++; continue;
      }
      i++; continue;
    }

    if (mode === "lineComment") {
      if (c === "\n") mode = "normal";
      i++; continue;
    }

    if (mode === "blockComment") {
      if (c === "*" && next === "/") { mode = "normal"; i += 2; continue; }
      i++; continue;
    }

    // mode === "dollar"
    if (c === "$" && sql.startsWith(dollarTag, i)) {
      mode = "normal"; i += dollarTag.length; continue;
    }
    i++;
  }

  pushSegment(segStart, n, hasNonCommentContent); // trailing statement (no terminator)
  return statements;
}
