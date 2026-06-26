import "@testing-library/jest-dom/vitest";

global.ResizeObserver = class ResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
};

// jsdom doesn't implement scrollIntoView; Mantine's Combobox calls it on the
// active option when a dropdown opens, which otherwise surfaces as an
// unhandled async error.
Element.prototype.scrollIntoView = () => {};
