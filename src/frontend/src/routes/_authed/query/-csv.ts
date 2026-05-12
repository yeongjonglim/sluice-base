export function buildCsv(
  columns: Array<string>,
  rows: Array<Array<string | null | undefined>>,
): string {
  const escape = (val: string | null | undefined): string => {
    const s = val == null ? "" : String(val);
    if (s.includes(",") || s.includes('"') || s.includes("\n")) {
      return `"${s.replace(/"/g, '""')}"`;
    }
    return s;
  };
  const lines = [
    columns.map(escape).join(","),
    ...rows.map((row) => row.map(escape).join(",")),
  ];
  return lines.join("\n");
}

export function exportToCsv(
  columns: Array<string>,
  rows: Array<Array<string | null | undefined>>,
  filename: string,
): void {
  const csv = buildCsv(columns, rows);
  const blob = new Blob([csv], { type: "text/csv" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}
