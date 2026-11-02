import { useMediaQuery } from "@mantine/hooks";

// A phone in landscape is wider than the navbar breakpoint but still has no room
// for two-axis splitters, so width alone cannot decide the layout.
const COMPACT = "(max-width: 47.99em), ((max-height: 32em) and (pointer: coarse))";

/** True when the surface should use the single-column, touch-first layout. */
export function useCompactLayout(): boolean {
  return useMediaQuery(COMPACT, false, { getInitialValueInEffect: false });
}
