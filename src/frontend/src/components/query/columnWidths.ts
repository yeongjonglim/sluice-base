import { measureText } from "@/utils/measureText";

// The initial column widths are estimated once from the header + a sample of rows,
// which keeps the layout stable while rows are virtualized — with content-based
// sizing the columns would jump as different rows scroll into view. These widths are
// the starting defaults; users can drag or double-click to resize (see useColumnWidths).
const CELL_CHROME_PX = 24; // padding + border allowance
export const MIN_COL_PX = 56;
const MAX_COL_PX = 360; // cap for the auto-estimate only
const SAMPLE_ROWS = 200;

// Manual drags and double-click auto-fit may exceed the estimate cap, up to this.
export const MANUAL_MAX_COL_PX = 800;

function clamp(value: number, lo: number, hi: number): number {
  return Math.min(hi, Math.max(lo, value));
}

// Header cells render semibold; data cells use the base xs font. Font strings are
// built from Mantine's CSS variables so measurement matches what the table renders.
function cellFonts(): { header: string; body: string } {
  const root = getComputedStyle(document.documentElement);
  const family =
    root.getPropertyValue("--mantine-font-family").trim() ||
    "-apple-system, BlinkMacSystemFont, Segoe UI, Roboto, sans-serif";
  const size = root.getPropertyValue("--mantine-font-size-xs").trim() || "12px";
  return { header: `600 ${size} ${family}`, body: `${size} ${family}` };
}

// Widest rendered content in a column (header vs. sampled cells), plus cell chrome.
function contentWidth(
  columns: Array<string>,
  rows: Array<Array<string | null>>,
  index: number,
  fonts: { header: string; body: string },
): number {
  const sample = rows.length > SAMPLE_ROWS ? rows.slice(0, SAMPLE_ROWS) : rows;
  let max = measureText(columns[index], fonts.header);
  for (const row of sample) {
    const value = row[index];
    const width = measureText(value === null ? "NULL" : value, fonts.body);
    if (width > max) max = width;
  }
  return Math.ceil(max) + CELL_CHROME_PX;
}

export function columnWidths(
  columns: Array<string>,
  rows: Array<Array<string | null>>,
): Array<number> {
  const fonts = cellFonts();
  return columns.map((_, index) =>
    Math.round(clamp(contentWidth(columns, rows, index, fonts), MIN_COL_PX, MAX_COL_PX)),
  );
}

export function autoFitWidth(
  columns: Array<string>,
  rows: Array<Array<string | null>>,
  index: number,
): number {
  const fonts = cellFonts();
  return Math.round(
    clamp(contentWidth(columns, rows, index, fonts), MIN_COL_PX, MANUAL_MAX_COL_PX),
  );
}
