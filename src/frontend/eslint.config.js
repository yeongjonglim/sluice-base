import globals from "globals";
import reactHooks from "eslint-plugin-react-hooks";
import { reactRefresh } from "eslint-plugin-react-refresh";
import jseslint from "@eslint/js";
import tseslint from "typescript-eslint";
import { tanstackConfig } from "@tanstack/eslint-config";
import { defineConfig, globalIgnores } from "eslint/config";
import reactYouMightNotNeedAnEffect from "eslint-plugin-react-you-might-not-need-an-effect";

export default defineConfig([
  globalIgnores(["dist", "**/functions/*", "*.cjs", "**/routeTree.gen.ts"]),
  ...tanstackConfig,
  {
    // https://github.com/TanStack/table/issues/6137
    rules: {
      "react-hooks/incompatible-library": "off",
    },
  },
  {
    files: ["**/*.{ts,tsx}"],
    extends: [
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite({
        extraHOCs: [
          "createFileRoute",
          "createLazyFileRoute",
          "createRootRoute",
          "createRootRouteWithContext",
          "createLink",
          "createRoute",
          "createLazyRoute",
        ],
      }), // https://github.com/ArnaudBarre/eslint-plugin-react-refresh/issues/102
      reactYouMightNotNeedAnEffect.configs.recommended, // https://react.dev/learn/you-might-not-need-an-effect
    ],
    languageOptions: {
      globals: globals.browser,
    },
  },
  {
    files: ["*.js"],
    extends: [jseslint.configs.recommended, tseslint.configs.disableTypeChecked],
    languageOptions: {
      parserOptions: {
        project: false,
      },
    },
  },
]);
