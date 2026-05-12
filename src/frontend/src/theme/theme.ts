import { NavLink, Table, createTheme } from "@mantine/core";

export function createAppTheme(primaryColor: string) {
  return createTheme({
    primaryColor,
    primaryShade: { light: 7, dark: 5 },
    fontFamily: 'system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif',
    defaultRadius: "sm",
    lineHeights: {
      xs: "1.2",
      sm: "1.3",
      md: "1.4",
      lg: "1.5",
      xl: "1.6",
    },
    spacing: {
      xs: "0.3rem",
      sm: "0.5rem",
      md: "0.75rem",
      lg: "1rem",
      xl: "1.25rem",
    },
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
