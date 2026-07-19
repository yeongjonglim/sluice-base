import { useCallback, useRef, useState } from "react";
import type { ExplainResponse } from "@/api/hooks";
import type { SqlStatement } from "@/utils/splitSqlStatements";
import type { paths } from "@/api/schema";
import { runLimited } from "@/utils/runLimited";
import { apiRequest } from "@/api/client";
import { isBlocked } from "@/api/useQueryRuns";

const MAX_CONCURRENCY = 6;

type ExplainRequestBody =
  paths["/api/query/explain"]["post"]["requestBody"]["content"]["application/json"];

export interface ExplainEntry {
  id: string;
  index: number;
  text: string;
  fromPos: number;
  toPos: number;
  fromLine: number;
  toLine: number;
  analyze: boolean;
  status: "pending" | "success" | "error" | "blocked";
  plan: ExplainResponse | null;
  error: unknown;
}

export function useExplainRuns() {
  const [runs, setRuns] = useState<Array<ExplainEntry>>([]);
  const batchRef = useRef(0);

  const run = useCallback(
    (databaseId: string, statements: Array<SqlStatement>, analyze: boolean) => {
      const batchId = ++batchRef.current;

      const initial: Array<ExplainEntry> = statements.map((s, index) => ({
        id: `${batchId}-${index}`,
        index,
        text: s.text,
        fromPos: s.fromPos,
        toPos: s.toPos,
        fromLine: s.fromLine,
        toLine: s.toLine,
        analyze,
        status: "pending",
        plan: null,
        error: null,
      }));
      setRuns(initial);

      const patch = (id: string, update: Partial<ExplainEntry>) => {
        // Ignore results from a superseded batch.
        if (batchId !== batchRef.current) return;
        setRuns((prev) => prev.map((r) => (r.id === id ? { ...r, ...update } : r)));
      };

      void runLimited(initial, MAX_CONCURRENCY, async (entry) => {
        try {
          const plan = await apiRequest<ExplainRequestBody, ExplainResponse>(
            "/api/query/explain",
            { method: "POST", body: { databaseId, sql: entry.text, analyze } },
          );
          patch(entry.id, { status: "success", plan, error: null });
        } catch (err) {
          patch(entry.id, {
            status: isBlocked(err) ? "blocked" : "error",
            plan: null,
            error: err,
          });
        }
      });
    },
    [],
  );

  const isRunning = runs.some((r) => r.status === "pending");
  return { runs, run, isRunning };
}
