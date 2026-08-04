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

// jsdom doesn't implement Element.scrollTo either; the editor highlight helper
// calls it to smooth-scroll a statement into view.
Element.prototype.scrollTo = () => {};

// jsdom doesn't implement canvas's 2D context (without the optional `canvas`
// package), which otherwise logs a noisy "Not implemented" warning every time
// the result-grid column-width estimator calls measureText(). Stubbing it to
// return null lets measureText() fall back to its char-count estimate, which
// is the path these tests already rely on.
HTMLCanvasElement.prototype.getContext = () => null;
