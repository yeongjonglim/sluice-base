const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

/** Compact, human-readable age for list rows ("just now", "4h ago", "3 days ago"). */
export function timeAgo(iso: string): string {
  const elapsed = Date.now() - new Date(iso).getTime();
  if (elapsed < MINUTE) return "just now";
  if (elapsed < HOUR) return `${Math.floor(elapsed / MINUTE)}m ago`;
  if (elapsed < DAY) return `${Math.floor(elapsed / HOUR)}h ago`;
  const days = Math.floor(elapsed / DAY);
  return days === 1 ? "1 day ago" : `${days} days ago`;
}
