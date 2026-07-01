import "@testing-library/jest-dom/vitest";
import { afterEach, vi } from "vitest";
import { cleanup } from "@testing-library/react";

global.ResizeObserver = class ResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
};

// With globals: false in vitest config, RTL cannot auto-detect vitest and register its own
// afterEach(cleanup). Register it globally here so every test file gets a clean DOM.
afterEach(cleanup);

// jsdom doesn't implement matchMedia; Mantine's MantineProvider calls it for colour-scheme
// detection. Provide a stub so all component tests can use MantineProvider without error.
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

// jsdom doesn't implement scrollIntoView; Mantine's Combobox calls it on the
// active option when a dropdown opens, which otherwise surfaces as an
// unhandled async error.
Element.prototype.scrollIntoView = () => {};
