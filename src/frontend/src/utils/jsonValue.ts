/**
 * JSON-aware presentation for the result grid's value inspector.
 *
 * Postgres hands these values over as text: `json` keeps whatever whitespace the
 * writer used, `jsonb` is canonical and compact, and `jsonb_pretty` is already
 * indented. The grid renders every value on one line, so the inspector is the
 * only place the structure can survive — and structure means a tree the reader
 * can fold, not just indentation.
 *
 * Detection is content-based rather than type-based: the query response carries
 * column names but no data types, and JSON stored in a `text` column deserves
 * the same treatment. Only values that are objects or arrays are considered —
 * a bare string, number, or boolean is not worth a JSON surface.
 *
 * Parsing is token-preserving: it keeps the exact source text of scalars and
 * keys instead of re-serializing JS values, so big integers keep every digit
 * and duplicate keys in a `json` value stay visible.
 */

export interface JsonScalarNode {
  kind: "scalar";
  /** The exact token text, e.g. `12345678901234567890` or `"a\\"b"`. */
  text: string;
}

export interface JsonObjectMember {
  /** Decoded key, for accessible labels and paths. */
  key: string;
  /** The exact quoted key as it appears in the source. */
  keyText: string;
  value: JsonNode;
}

export interface JsonObjectNode {
  kind: "object";
  /** Stable within this value; used as the collapse-state key. */
  id: number;
  members: Array<JsonObjectMember>;
}

export interface JsonArrayNode {
  kind: "array";
  id: number;
  items: Array<JsonNode>;
}

export type JsonNode = JsonScalarNode | JsonObjectNode | JsonArrayNode;

export interface JsonValue {
  /** Indent unit detected from the source (4 spaces for jsonb_pretty, else 2). */
  indent: string;
  root: JsonNode;
  /** Fully expanded text, used for copying. */
  formatted: string;
  /** Ids of containers with children — the nodes that can be folded. */
  collapsibleIds: Array<number>;
}

/** First newline's leading whitespace is the document's indent unit. */
function detectIndent(source: string): string {
  const match = /\n([ \t]+)\S/.exec(source);
  if (!match) return "  ";
  const unit = match[1];
  if (unit.includes("\t")) return "  ";
  return unit.length > 8 ? "    " : unit;
}

export function serializeJsonTree(node: JsonNode, indent: string, depth = 0): string {
  if (node.kind === "scalar") return node.text;

  const pad = indent.repeat(depth);
  const childPad = indent.repeat(depth + 1);

  if (node.kind === "object") {
    if (node.members.length === 0) return "{}";
    const body = node.members
      .map((member) => `${childPad}${member.keyText}: ${serializeJsonTree(member.value, indent, depth + 1)}`)
      .join(",\n");
    return `{\n${body}\n${pad}}`;
  }

  if (node.items.length === 0) return "[]";
  const body = node.items
    .map((item) => `${childPad}${serializeJsonTree(item, indent, depth + 1)}`)
    .join(",\n");
  return `[\n${body}\n${pad}]`;
}

/**
 * Returns the tree and its display text, or null when the cell is not a JSON
 * object/array. Malformed JSON falls back to plain text.
 */
export function readJsonValue(raw: string): JsonValue | null {
  const source = raw.trim();
  if (!source.startsWith("{") && !source.startsWith("[")) return null;

  try {
    JSON.parse(source);
  } catch {
    return null;
  }

  const indent = detectIndent(source);
  const collapsibleIds: Array<number> = [];
  let index = 0;
  let nextId = 0;

  const skipWhitespace = () => {
    while (index < source.length && /\s/.test(source[index])) index += 1;
  };

  const readString = (): string => {
    const start = index;
    let cursor = index + 1;
    while (cursor < source.length) {
      const char = source[cursor];
      if (char === "\\") {
        cursor += 2;
        continue;
      }
      cursor += 1;
      if (char === '"') break;
    }
    index = cursor;
    return source.slice(start, cursor);
  };

  const readScalar = (): JsonScalarNode => {
    const start = index;
    if (source[index] === '"') {
      readString();
    } else {
      while (index < source.length && !/[\s,\]}]/.test(source[index])) index += 1;
    }
    return { kind: "scalar", text: source.slice(start, index) };
  };

  function readValue(): JsonNode {
    skipWhitespace();

    if (source[index] === "{") {
      const id = nextId;
      nextId += 1;
      index += 1;
      const members: Array<JsonObjectMember> = [];
      skipWhitespace();
      if (source[index] === "}") {
        index += 1;
        return { kind: "object", id, members };
      }
      for (;;) {
        skipWhitespace();
        const keyText = readString();
        const key = JSON.parse(keyText) as string;
        skipWhitespace();
        index += 1; // colon
        const value = readValue();
        members.push({ key, keyText, value });
        skipWhitespace();
        if (source[index] === ",") {
          index += 1;
          continue;
        }
        index += 1; // closing brace
        break;
      }
      if (members.length > 0) collapsibleIds.push(id);
      return { kind: "object", id, members };
    }

    if (source[index] === "[") {
      const id = nextId;
      nextId += 1;
      index += 1;
      const items: Array<JsonNode> = [];
      skipWhitespace();
      if (source[index] === "]") {
        index += 1;
        return { kind: "array", id, items };
      }
      for (;;) {
        items.push(readValue());
        skipWhitespace();
        if (source[index] === ",") {
          index += 1;
          continue;
        }
        index += 1; // closing bracket
        break;
      }
      if (items.length > 0) collapsibleIds.push(id);
      return { kind: "array", id, items };
    }

    return readScalar();
  }

  const root = readValue();

  return {
    indent,
    root,
    formatted: serializeJsonTree(root, indent),
    collapsibleIds,
  };
}
