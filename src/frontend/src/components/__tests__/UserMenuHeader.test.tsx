import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import React from "react";
import { UserMenuHeader } from "@/components/UserMenuHeader";

afterEach(cleanup);

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: () => ({
      matches: false,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    }),
  });
});

function Wrapper({ children }: { children: React.ReactNode }) {
  return React.createElement(MantineProvider, null, children);
}

describe("UserMenuHeader", () => {
  it("renders both name and email when provided", () => {
    render(
      React.createElement(UserMenuHeader, { name: "Ada Lovelace", email: "ada@example.com" }),
      { wrapper: Wrapper },
    );
    expect(screen.getByText("Ada Lovelace")).toBeInTheDocument();
    expect(screen.getByText("ada@example.com")).toBeInTheDocument();
  });

  it("renders email only when name is missing", () => {
    render(React.createElement(UserMenuHeader, { name: null, email: "ada@example.com" }), {
      wrapper: Wrapper,
    });
    expect(screen.getByText("ada@example.com")).toBeInTheDocument();
  });

  it("renders name only when email is missing", () => {
    render(React.createElement(UserMenuHeader, { name: "Ada Lovelace", email: null }), {
      wrapper: Wrapper,
    });
    expect(screen.getByText("Ada Lovelace")).toBeInTheDocument();
    expect(screen.queryByText(/@/)).toBeNull();
  });

  it("renders nothing when both name and email are missing", () => {
    render(
      React.createElement(
        "div",
        { "data-testid": "host" },
        React.createElement(UserMenuHeader, { name: null, email: null }),
      ),
      { wrapper: Wrapper },
    );
    expect(screen.getByTestId("host")).toBeEmptyDOMElement();
  });
});
