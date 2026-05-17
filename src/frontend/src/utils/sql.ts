const SAFE_IDENTIFIER = /^[a-z_][a-z0-9_]*$/;

export function quoteIdentifier(name: string): string {
  if (SAFE_IDENTIFIER.test(name)) return name;
  return `"${name.replace(/"/g, '""')}"`;
}
