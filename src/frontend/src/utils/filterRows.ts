// Case-insensitive substring filter over result rows: keeps a row if any of its
// cells contains the query. NULL cells never match. An empty/whitespace query
// returns the original array unchanged (same reference).
export function filterRows(
  rows: Array<Array<string | null>>,
  query: string,
): Array<Array<string | null>> {
  const q = query.trim().toLowerCase();
  if (q === "") return rows;
  return rows.filter((row) =>
    row.some((cell) => cell !== null && cell.toLowerCase().includes(q)),
  );
}
