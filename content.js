(() => {
  "use strict";

  const INSTALL_FLAG = "__doubleClickCloseTabInstalledV2";
  if (globalThis[INSTALL_FLAG]) {
    return;
  }
  globalThis[INSTALL_FLAG] = true;

  const DEFAULT_SETTINGS = Object.freeze({
    pageBlankDoubleClick: true,
  });

  const NON_BLANK_SELECTOR = [
    "a",
    "button",
    "input",
    "textarea",
    "select",
    "option",
    "label",
    "summary",
    "details",
    "iframe",
    "object",
    "embed",
    "video",
    "audio",
    "img",
    "picture",
    "svg",
    "canvas",
    "[contenteditable]:not([contenteditable=\"false\"])",
    "[role=\"button\"]",
    "[role=\"link\"]",
    "[role=\"textbox\"]",
    "[role=\"searchbox\"]",
    "[role=\"checkbox\"]",
    "[role=\"radio\"]",
    "[role=\"switch\"]",
    "[role=\"slider\"]",
    "[role=\"menuitem\"]",
    "[role=\"tab\"]",
    "[role=\"option\"]",
    "[draggable=\"true\"]",
  ].join(",");

  let closeOnBlankDoubleClick = false;

  chrome.storage.sync
    .get(DEFAULT_SETTINGS)
    .then((settings) => {
      closeOnBlankDoubleClick = settings.pageBlankDoubleClick !== false;
    })
    .catch(() => {
      closeOnBlankDoubleClick = DEFAULT_SETTINGS.pageBlankDoubleClick;
    });

  chrome.storage.onChanged.addListener((changes, areaName) => {
    if (areaName === "sync" && changes.pageBlankDoubleClick) {
      closeOnBlankDoubleClick = changes.pageBlankDoubleClick.newValue !== false;
    }
  });

  function getEventPath(event) {
    if (typeof event.composedPath === "function") {
      const composedPath = event.composedPath();
      if (composedPath.length > 0) {
        return composedPath;
      }
    }

    const path = [];
    let node = event.target;

    while (node) {
      path.push(node);
      if (node instanceof ShadowRoot) {
        node = node.host;
      } else {
        node = node.parentNode;
      }
    }

    return path;
  }

  function hasDirectText(element) {
    for (const child of element.childNodes) {
      if (child.nodeType === Node.TEXT_NODE && (child.textContent || "").trim()) {
        return true;
      }
    }

    return false;
  }


  function isBlankArea(event) {
    const path = getEventPath(event);
    const elements = path.filter((node) => node instanceof Element);

    if (elements.length === 0) {
      return false;
    }

    for (const element of elements) {
      if (element.matches(NON_BLANK_SELECTOR)) {
        return false;
      }

      const isRoot = element === document.documentElement || element === document.body;
      if (!isRoot && element.tabIndex >= 0) {
        return false;
      }

      if (hasDirectText(element)) {
        return false;
      }

      const cursor = window.getComputedStyle(element).cursor;
      if (cursor && cursor !== "auto" && cursor !== "default") {
        return false;
      }
    }

    return true;
  }

  document.addEventListener(
    "dblclick",
    (event) => {
      if (!closeOnBlankDoubleClick || event.button !== 0) {
        return;
      }

      if (event.ctrlKey || event.altKey || event.metaKey || event.shiftKey) {
        return;
      }

      if (!(event.target instanceof Element) || !isBlankArea(event)) {
        return;
      }

      const selection = window.getSelection();
      if (selection && !selection.isCollapsed) {
        return;
      }

      event.preventDefault();
      event.stopImmediatePropagation();

      chrome.runtime
        .sendMessage({ type: "CLOSE_CURRENT_TAB", source: "blank-double-click" })
        .catch(() => {
          // 当前标签关闭时，扩展上下文可能先失效，此时无需额外处理。
        });
    },
    true,
  );
})();
