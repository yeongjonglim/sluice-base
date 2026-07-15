import { useCallback, useRef, useState } from "react";
import type { ExecuteQueryResponse } from "@/api/hooks";
import type { SqlStatement } from "@/utils/splitSqlStatements";
import type { paths } from "@/api/schema";
import { runLimited } from "@/utils/runLimited";
import { ApiError, apiRequest } from "@/api/client";

const MAX_CONCURRENCY = 6;

type QueryRequestBody =
  paths["/api/query"]["post"]["requestBody"]["content"]["application/json"];

export interface RunEntry {
  id: string;
  index: number;
  text: string;
  fromPos: number;
  toPos: number;
  fromLine: number;
  toLine: number;
  status: "pending" | "success" | "error" | "blocked";
  response: ExecuteQueryResponse | null;
  error: unknown;
}

function isBlocked(err: unknown): boolean {
  return (
    err instanceof ApiError &&
    err.status === 403 &&
    (err.body as { type?: string } | null)?.type === "sensitive_columns"
  );
}

export function useQueryRuns() {
  const [runs, setRuns] = useState<Array<RunEntry>>([]);
  const batchRef = useRef(0);

  const run = useCallback((databaseId: string, statements: Array<SqlStatement>) => {
    const batchId = ++batchRef.current;

    const initial: Array<RunEntry> = statements.map((s, index) => ({
      id: `${batchId}-${index}`,
      index,
      text: s.text,
      fromPos: s.fromPos,
      toPos: s.toPos,
      fromLine: s.fromLine,
      toLine: s.toLine,
      status: "pending",
      response: null,
      error: null,
    }));
    setRuns(initial);

    const patch = (id: string, update: Partial<RunEntry>) => {
      // Ignore results from a superseded batch.
      if (batchId !== batchRef.current) return;
      setRuns((prev) => prev.map((r) => (r.id === id ? { ...r, ...update } : r)));
    };

    void runLimited(initial, MAX_CONCURRENCY, async (entry) => {
      try {
        const response = await apiRequest<QueryRequestBody, ExecuteQueryResponse>(
          "/api/query",
          { method: "POST", body: { databaseId, sql: entry.text } },
        );
        patch(entry.id, {
          status: response.error ? "error" : "success",
          response,
          error: null,
        });
      } catch (err) {
        patch(entry.id, {
          status: isBlocked(err) ? "blocked" : "error",
          response: null,
          error: err,
        });
      }
    });
  }, []);

  const isRunning = runs.some((r) => r.status === "pending");
  return { runs, run, isRunning };
}
