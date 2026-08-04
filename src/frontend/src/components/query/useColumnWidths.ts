import { useEffect, useMemo, useRef, useState } from "react";
import type { PointerEvent as ReactPointerEvent } from "react";
import { MANUAL_MAX_COL_PX, autoFitWidth, columnWidths } from "@/components/query/columnWidths";

const MIN_COL_PX = 56; // resize floor; matches the columnWidths clamp minimum

function clamp(value: number, lo: number, hi: number): number {
  return Math.min(hi, Math.max(lo, value));
}

export function useColumnWidths(
  columns: Array<string>,
  rows: Array<Array<string | null>>,
) {
  const estimated = useMemo(() => columnWidths(columns, rows), [columns, rows]);

  // Widths are local to the current result. When the column set changes (a new
  // query), reset to a fresh estimate — the React-endorsed "adjust state during
  // render on prop change" pattern, keyed on the column-name signature. NUL is
  // used as the join separator so ordinary names can't collide.
  const signature = columns.join("\u0000");
  const [state, setState] = useState({ signature, widths: estimated });
  if (state.signature !== signature) {
    setState({ signature, widths: estimated });
  }
  const widths = state.signature === signature ? state.widths : estimated;
  const totalWidth = widths.reduce((a, b) => a + b, 0);

  // Listeners for the in-flight drag, so we can detach them if the grid unmounts
  // mid-drag. Only one column resizes at a time.
  const drag = useRef<{ move: (e: PointerEvent) => void; up: () => void } | null>(null);

  useEffect(
    () => () => {
      if (drag.current) {
        window.removeEventListener("pointermove", drag.current.move);
        window.removeEventListener("pointerup", drag.current.up);
        document.body.style.userSelect = "";
      }
    },
    [],
  );

  function setWidth(index: number, width: number) {
    setState((s) => {
      const next = s.widths.slice();
      next[index] = width;
      return { signature: s.signature, widths: next };
    });
  }

  function onResizeStart(index: number, event: ReactPointerEvent) {
    event.preventDefault();
    const startX = event.clientX;
    const startWidth = widths[index];
    document.body.style.userSelect = "none";

    const move = (e: PointerEvent) => {
      setWidth(index, Math.round(clamp(startWidth + (e.clientX - startX), MIN_COL_PX, MANUAL_MAX_COL_PX)));
    };
    const up = () => {
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", up);
      document.body.style.userSelect = "";
      drag.current = null;
    };

    drag.current = { move, up };
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", up);
  }

  function onAutoFit(index: number) {
    setWidth(index, autoFitWidth(columns, rows, index));
  }

  return { widths, totalWidth, onResizeStart, onAutoFit };
}
