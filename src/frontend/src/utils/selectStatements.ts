import type { SqlStatement } from "@/utils/splitSqlStatements";

export interface EditorSelection {
  from: number;
  to: number;
  empty: boolean;
}

export function selectStatements(
  statements: Array<SqlStatement>,
  selection: EditorSelection,
  runAll: boolean,
): Array<SqlStatement> {
  if (statements.length === 0) return [];
  if (runAll) return statements;

  if (!selection.empty) {
    // Any statement whose range overlaps the selection, in full.
    return statements.filter(
      (s) => s.fromPos < selection.to && s.toPos > selection.from,
    );
  }

  const pos = selection.from;
  const containing = statements.find((s) => s.fromPos <= pos && pos <= s.toPos);
  if (containing) return [containing];

  // Cursor sits between statements: use the last one starting at/before it,
  // else the first statement.
  const preceding = [...statements].reverse().find((s) => s.fromPos <= pos);
  return [preceding ?? statements[0]];
}
