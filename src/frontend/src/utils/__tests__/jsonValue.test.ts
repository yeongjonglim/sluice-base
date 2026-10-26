import { describe, expect, it } from "vitest";
import { readJsonValue, serializeJsonTree } from "@/utils/jsonValue";

function formatted(raw: string): string {
  const value = readJsonValue(raw);
  expect(value).not.toBeNull();
  return value!.formatted;
}

describe("readJsonValue formatting", () => {
  it("indents objects and arrays with two spaces", () => {
    expect(formatted('{"a":1,"b":[true,null]}')).toBe(
      ['{', '  "a": 1,', '  "b": [', "    true,", "    null", "  ]", "}"].join("\n"),
    );
  });

  it("keeps empty containers on one line", () => {
    expect(formatted('{"a":{},"b":[]}')).toBe(
      ['{', '  "a": {},', '  "b": []', "}"].join("\n"),
    );
  });

  it("does not touch braces, brackets, commas, or colons inside strings", () => {
    expect(formatted('{"s":"{ a: 1, b: [2] }"}')).toBe(
      ['{', '  "s": "{ a: 1, b: [2] }"', "}"].join("\n"),
    );
  });

  it("preserves escaped quotes", () => {
    expect(formatted('{"s":"a\\"b"}')).toBe(
      ['{', '  "s": "a\\"b"', "}"].join("\n"),
    );
  });

  it("keeps every digit of numbers beyond JS integer precision", () => {
    expect(formatted('{"n":12345678901234567890}')).toContain("12345678901234567890");
  });

  it("keeps duplicate keys in a json value", () => {
    expect(formatted('{"a":1,"a":2}')).toBe(
      ['{', '  "a": 1,', '  "a": 2', "}"].join("\n"),
    );
  });

  it("handles nested containers", () => {
    expect(formatted('{"a":[{"b":1}]}')).toBe(
      ["{", '  "a": [', "    {", '      "b": 1', "    }", "  ]", "}"].join("\n"),
    );
  });
});

describe("readJsonValue indentation", () => {
  it("keeps the four-space layout of jsonb_pretty", () => {
    const pretty = '{\n    "a": 1,\n    "b": [\n        2\n    ]\n}';
    expect(formatted(pretty)).toBe(pretty);
  });

  it("uses the detected indent unit for the tree", () => {
    expect(readJsonValue('{\n\t"a": 1\n}')?.indent).toBe("  ");
    expect(readJsonValue('{\n    "a": 1\n}')?.indent).toBe("    ");
    expect(readJsonValue('{"a":1}')?.indent).toBe("  ");
  });
});

describe("readJsonValue tree", () => {
  it("exposes members without losing their source tokens", () => {
    const value = readJsonValue('{"a":1,"b":[true,null]}');
    expect(value?.root.kind).toBe("object");
    if (value?.root.kind !== "object") return;

    expect(value.root.members.map((member) => member.key)).toEqual(["a", "b"]);
    expect(value.root.members[0].keyText).toBe('"a"');
    expect(value.root.members[0].value).toEqual({ kind: "scalar", text: "1" });
    expect(value.root.members[1].value.kind).toBe("array");
  });

  it("marks containers with children as collapsible, and empty ones as not", () => {
    const value = readJsonValue('{"a":{},"b":[{"c":1}]}');
    expect(value).not.toBeNull();

    // Root, the array, and the object inside it have children; the empty
    // object does not, and must not become a fold target.
    expect(value!.collapsibleIds).toHaveLength(3);
    const root = value!.root;
    if (root.kind !== "object") return;
    const empty = root.members[0].value;
    const array = root.members[1].value;
    expect(empty.kind).toBe("object");
    if (empty.kind !== "object") return;
    expect(array.kind).toBe("array");
    if (array.kind !== "array") return;
    expect(value!.collapsibleIds).toContain(root.id);
    expect(value!.collapsibleIds).toContain(array.id);
    expect(value!.collapsibleIds).not.toContain(empty.id);
  });
});

describe("readJsonValue detection", () => {
  it("ignores non-object and non-array values", () => {
    expect(readJsonValue("42")).toBeNull();
    expect(readJsonValue("true")).toBeNull();
    expect(readJsonValue('"a string"')).toBeNull();
    expect(readJsonValue("not json")).toBeNull();
  });

  it("ignores malformed objects", () => {
    expect(readJsonValue('{"a": }')).toBeNull();
    expect(readJsonValue('{"a":1')).toBeNull();
  });

  it("trims surrounding whitespace before formatting", () => {
    expect(readJsonValue('  {"a":1}  ')?.formatted).toBe('{\n  "a": 1\n}');
  });
});

describe("serializeJsonTree", () => {
  it("round-trips the formatted text", () => {
    const value = readJsonValue('{"a":[1,{"b":2}]}')!;
    expect(serializeJsonTree(value.root, value.indent)).toBe(value.formatted);
  });
});
