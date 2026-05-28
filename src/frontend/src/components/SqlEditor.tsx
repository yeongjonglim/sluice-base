import { forwardRef, useMemo } from "react";
import { Box, useComputedColorScheme } from "@mantine/core";
import CodeMirror from "@uiw/react-codemirror";
import { githubDark, githubLight } from "@uiw/codemirror-themes-all";
import { PostgreSQL, sql } from "@codemirror/lang-sql";
import { EditorView } from "@codemirror/view";
import type { Extension } from "@codemirror/state";
import type { BasicSetupOptions, ReactCodeMirrorRef } from "@uiw/react-codemirror";
import type React from "react";
import { useSchemaCompletions } from "@/api/hooks";

const noFocusOutline = EditorView.theme({ "&.cm-focused": { outline: "none" } });

interface SqlEditorProps {
  value: string;
  onChange?: (value: string) => void;
  databaseId?: string | null;
  extensions?: Array<Extension>;
  editable?: boolean;
  readOnly?: boolean;
  lineNumbers?: boolean;
  minLines?: number;
  height?: string;
  minHeight?: string;
  maxHeight?: string;
  style?: React.CSSProperties;
}

function padToMinLines(value: string, minLines: number): string {
  const lineCount = value.split("\n").length;
  if (lineCount >= minLines) return value;
  return value + "\n".repeat(minLines - lineCount);
}

export const SqlEditor = forwardRef<ReactCodeMirrorRef, SqlEditorProps>(
  function SqlEditorInner(
    {
      value,
      onChange,
      databaseId,
      extensions: extra = [],
      editable,
      readOnly,
      lineNumbers = true,
      minLines,
      height,
      minHeight,
      maxHeight,
      style,
    },
    ref,
  ) {
    const computedColorScheme = useComputedColorScheme();
    const theme = computedColorScheme === "dark" ? githubDark : githubLight;
    const completions = useSchemaCompletions(databaseId ?? null);

    const basicSetup: BasicSetupOptions = useMemo(
      () => ({ lineNumbers, foldGutter: false }),
      [lineNumbers],
    );

    const lang = useMemo(
      () => sql({ dialect: PostgreSQL, schema: completions.data }),
      [completions.data],
    );

    const extensions = useMemo(
      () => [lang, ...extra, EditorView.lineWrapping, noFocusOutline],
      [lang, extra],
    );

    const displayValue = minLines ? padToMinLines(value, minLines) : value;

    return (
      <Box
        style={{
          border: "1px solid var(--mantine-color-default-border)",
          borderRadius: "var(--mantine-radius-sm)",
          overflow: "hidden",
          ...style,
        }}
      >
        <CodeMirror
          ref={ref}
          value={displayValue}
          onChange={onChange}
          extensions={extensions}
          theme={theme}
          basicSetup={basicSetup}
          editable={editable}
          readOnly={readOnly}
          height={height}
          minHeight={minHeight}
          maxHeight={maxHeight}
        />
      </Box>
    );
  },
);
