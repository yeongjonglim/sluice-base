import { NavLink, Table, createTheme } from "@mantine/core";

export function createAppTheme(primaryColor: string) {
  return createTheme({
    primaryColor,
    primaryShade: { light: 7, dark: 5 },
    fontFamily: 'system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif',
    defaultRadius: "sm",
    scale: 0.875,
    components: {
      Table: Table.extend({
        defaultProps: {
          verticalSpacing: "xs",
          horizontalSpacing: "xs",
        },
      }),
      NavLink: NavLink.extend({
        styles: {
          root: { padding: "5px 8px" },
        },
      }),
    },
  });
}
