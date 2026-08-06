import type { SqlStatement } from "@/utils/splitSqlStatements";
import { splitSqlStatements } from "@/utils/splitSqlStatements";
import { selectStatements } from "@/utils/selectStatements";

// The minimal shape of a CodeMirror EditorView this module needs — narrower
// than the real `EditorView` type so it stays trivially unit-testable with a
// plain object, while a real `EditorView` still satisfies it structurally
// (its `state.doc.toString()` and `state.selection.main` shape matches).
export interface QueryEditorViewLike {
  state: {
    doc: { toString: () => string };
    selection: { main: { from: number; to: number; empty: boolean } };
  };
}

// Reads the live document + selection off the editor view and resolves which
// statements a Run/Explain action should target: every statement when
// `runAll` is true, otherwise whatever `selectStatements` picks for the
// current selection/cursor. Shared by handleRun and handleExplain so both
// scope identically.
export function resolveTargetStatements(
  view: QueryEditorViewLike | null | undefined,
  runAll: boolean,
): Array<SqlStatement> {
  if (!view) return [];
  const stmts = splitSqlStatements(view.state.doc.toString());
  if (stmts.length === 0) return [];
  const { from, to, empty } = view.state.selection.main;
  return selectStatements(stmts, { from, to, empty }, runAll);
}

export interface ExplainDispatch {
  databaseId: string;
  targets: Array<SqlStatement>;
  analyze: boolean;
}

// Resolves an Explain click into a dispatchable request, or null when there's
// nothing to explain (no database selected, or no statement under the
// cursor/selection). Explain always scopes to selection/cursor — there is no
// "explain all" — so `resolveTargetStatements` is called with `runAll: false`.
export function buildExplainDispatch(
  databaseId: string | null | undefined,
  view: QueryEditorViewLike | null | undefined,
  analyze: boolean,
): ExplainDispatch | null {
  if (!databaseId) return null;
  const targets = resolveTargetStatements(view, false);
  if (targets.length === 0) return null;
  return { databaseId, targets, analyze };
}
