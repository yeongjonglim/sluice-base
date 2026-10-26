import { IconChevronRight } from "@tabler/icons-react";
import type { ReactNode } from "react";
import type { JsonNode, JsonValue } from "@/utils/jsonValue";
import classes from "@/components/query/JsonTree.module.css";

interface TreeContext {
  indent: string;
  collapsed: ReadonlySet<number>;
  onToggle: (id: number) => void;
}

function Line({
  depth,
  indent,
  toggle,
  content,
}: {
  depth: number;
  indent: string;
  toggle?: ReactNode;
  content: string;
}) {
  return (
    <div className={classes.line} style={{ paddingLeft: `${depth * indent.length}ch` }}>
      <span className={classes.gutter}>{toggle}</span>
      <span className={classes.content}>{content}</span>
    </div>
  );
}

function NodeLines({
  node,
  depth,
  keyText,
  hasComma,
  path,
  ctx,
}: {
  node: JsonNode;
  depth: number;
  keyText?: string;
  hasComma: boolean;
  path: string;
  ctx: TreeContext;
}): ReactNode {
  const prefix = keyText === undefined ? "" : `${keyText}: `;
  const comma = hasComma ? "," : "";

  if (node.kind === "scalar") {
    return (
      <Line
        depth={depth}
        indent={ctx.indent}
        content={`${prefix}${node.text}${comma}`}
      />
    );
  }

  const open = node.kind === "object" ? "{" : "[";
  const close = node.kind === "object" ? "}" : "]";
  const childCount = node.kind === "object" ? node.members.length : node.items.length;

  if (childCount === 0) {
    return (
      <Line
        depth={depth}
        indent={ctx.indent}
        content={`${prefix}${open}${close}${comma}`}
      />
    );
  }

  const collapsed = ctx.collapsed.has(node.id);
  const toggle = (
    <button
      type="button"
      className={classes.chevron}
      aria-expanded={!collapsed}
      aria-label={`${collapsed ? "Expand" : "Collapse"} ${path}`}
      onClick={() => ctx.onToggle(node.id)}
    >
      <IconChevronRight
        size={12}
        className={classes.chevronIcon}
        style={{ transform: collapsed ? undefined : "rotate(90deg)" }}
      />
    </button>
  );

  if (collapsed) {
    return (
      <Line
        depth={depth}
        indent={ctx.indent}
        toggle={toggle}
        content={`${prefix}${open} … ${close}${comma}`}
      />
    );
  }

  return (
    <>
      <Line
        depth={depth}
        indent={ctx.indent}
        toggle={toggle}
        content={`${prefix}${open}`}
      />
      {node.kind === "object"
        ? node.members.map((member, index) => (
            <NodeLines
              key={`${path}.${member.key}.${index}`}
              node={member.value}
              depth={depth + 1}
              keyText={member.keyText}
              hasComma={index < node.members.length - 1}
              path={`${path}.${member.key}`}
              ctx={ctx}
            />
          ))
        : node.items.map((item, index) => (
            <NodeLines
              key={`${path}[${index}]`}
              node={item}
              depth={depth + 1}
              hasComma={index < node.items.length - 1}
              path={`${path}[${index}]`}
              ctx={ctx}
            />
          ))}
      <Line depth={depth} indent={ctx.indent} content={`${close}${comma}`} />
    </>
  );
}

/**
 * A collapsible JSON tree. Collapse state lives above this component so it
 * survives switching between the Formatted and Raw views.
 */
export function JsonTree({
  value,
  collapsed,
  onToggle,
}: {
  value: JsonValue;
  collapsed: ReadonlySet<number>;
  onToggle: (id: number) => void;
}) {
  return (
    <div className={classes.tree} data-json-tree>
      <NodeLines
        node={value.root}
        depth={0}
        hasComma={false}
        path="$"
        ctx={{ indent: value.indent, collapsed, onToggle }}
      />
    </div>
  );
}
