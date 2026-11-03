"use strict";

const state = {
  windows: [],
  currentWindowId: null,
  query: "",
  isInitialLoad: true,
  closingTabIds: new Set(),
};

const elements = {
  tabList: document.getElementById("tabList"),
  tabCount: document.getElementById("tabCount"),
  searchInput: document.getElementById("searchInput"),
  refreshButton: document.getElementById("refreshButton"),
  pageDoubleClick: document.getElementById("pageDoubleClick"),
  sidePositionCard: document.getElementById("sidePositionCard"),
  sidePositionTitle: document.getElementById("sidePositionTitle"),
  sidePositionText: document.getElementById("sidePositionText"),
  openAppearanceSettings: document.getElementById("openAppearanceSettings"),
  toast: document.getElementById("toast"),
};

let refreshTimer;
let toastTimer;

function scheduleRefresh(delay = 80) {
  window.clearTimeout(refreshTimer);
  refreshTimer = window.setTimeout(() => {
    void refreshTabs();
  }, delay);
}

async function refreshTabs() {
  try {
    const [currentWindow, windows] = await Promise.all([
      chrome.windows.getCurrent(),
      chrome.windows.getAll({ populate: true, windowTypes: ["normal"] }),
    ]);

    state.currentWindowId = currentWindow?.id ?? null;
    state.windows = windows
      .filter((browserWindow) => browserWindow.type === "normal" && Array.isArray(browserWindow.tabs))
      .sort((left, right) => {
        if (left.id === state.currentWindowId) return -1;
        if (right.id === state.currentWindowId) return 1;
        return left.id - right.id;
      });
  } catch (error) {
    console.error("读取标签页失败：", error);
    showToast("读取标签页失败，请尝试重新打开侧栏。");
  } finally {
    state.isInitialLoad = false;
    render();
  }
}

function getFilteredWindows() {
  const query = state.query.trim().toLocaleLowerCase("zh-CN");
  if (!query) {
    return state.windows;
  }

  return state.windows
    .map((browserWindow) => ({
      ...browserWindow,
      tabs: browserWindow.tabs.filter((tab) => {
        const searchable = `${tab.title ?? ""} ${tab.url ?? ""}`.toLocaleLowerCase("zh-CN");
        return searchable.includes(query);
      }),
    }))
    .filter((browserWindow) => browserWindow.tabs.length > 0);
}

function render() {
  const filteredWindows = getFilteredWindows();
  const totalTabs = state.windows.reduce((sum, browserWindow) => sum + browserWindow.tabs.length, 0);
  const visibleTabs = filteredWindows.reduce((sum, browserWindow) => sum + browserWindow.tabs.length, 0);

  elements.tabCount.textContent = state.query.trim()
    ? `${visibleTabs} / ${totalTabs} 个标签页`
    : `${totalTabs} 个标签页`;

  elements.tabList.replaceChildren();

  if (filteredWindows.length === 0) {
    elements.tabList.append(createEmptyState(Boolean(state.query.trim())));
    return;
  }

  for (const browserWindow of filteredWindows) {
    elements.tabList.append(createWindowGroup(browserWindow));
  }
}

function createWindowGroup(browserWindow) {
  const group = document.createElement("section");
  group.className = "window-group";

  const heading = document.createElement("div");
  heading.className = "window-heading";
  heading.textContent =
    browserWindow.id === state.currentWindowId ? "当前窗口" : `窗口 ${browserWindow.id}`;
  group.append(heading);

  const tabs = [...browserWindow.tabs].sort((left, right) => left.index - right.index);
  for (const tab of tabs) {
    group.append(createTabRow(tab));
  }

  return group;
}

function createTabRow(tab) {
  const row = document.createElement("button");
  row.type = "button";
  row.className = "tab-row";
  row.dataset.tabId = String(tab.id);
  row.title = "双击关闭标签页";
  row.setAttribute("aria-label", `${tab.title || "无标题页面"}，双击关闭`);

  if (tab.active) {
    row.classList.add("is-active");
    row.setAttribute("aria-current", "page");
  }

  if (state.closingTabIds.has(tab.id)) {
    row.classList.add("is-closing");
  }

  const favicon = document.createElement("span");
  favicon.className = "tab-favicon";
  favicon.style.setProperty("--hue", String(getHue(tab)));
  favicon.textContent = getMonogram(tab);
  favicon.setAttribute("aria-hidden", "true");

  const copy = document.createElement("span");
  copy.className = "tab-copy";

  const title = document.createElement("span");
  title.className = "tab-title";
  title.textContent = tab.title?.trim() || "无标题页面";

  const url = document.createElement("span");
  url.className = "tab-url";
  url.textContent = formatTabUrl(tab.url);

  copy.append(title, url);

  if (tab.pinned) {
    const pinBadge = document.createElement("span");
    pinBadge.className = "pin-badge";
    pinBadge.textContent = "固定";
    row.append(favicon, copy, pinBadge);
  } else {
    row.append(favicon, copy);
  }

  if (tab.active) {
    const activeDot = document.createElement("span");
    activeDot.className = "tab-active-dot";
    activeDot.title = "当前标签";
    activeDot.setAttribute("aria-label", "当前标签");
    row.append(activeDot);
  }

  let singleClickTimer;
  row.addEventListener("click", (event) => {
    if (event.detail !== 1) {
      return;
    }

    window.clearTimeout(singleClickTimer);
    singleClickTimer = window.setTimeout(() => {
      void activateTab(tab);
    }, 240);
  });

  row.addEventListener("dblclick", (event) => {
    event.preventDefault();
    event.stopPropagation();
    window.clearTimeout(singleClickTimer);
    void closeTab(tab, row);
  });

  row.addEventListener("keydown", (event) => {
    if (event.key !== "Delete" && event.key !== "Backspace") {
      return;
    }

    event.preventDefault();
    void closeTab(tab, row);
  });

  return row;
}

function createEmptyState(isSearching) {
  const empty = document.createElement("div");
  empty.className = "empty-state";

  const title = document.createElement("strong");
  title.textContent = isSearching ? "没有匹配的标签页" : "暂时没有打开的标签页";

  const description = document.createElement("span");
  description.textContent = isSearching ? "换一个关键词再试试。" : "打开新标签页后，这里会自动更新。";

  empty.append(title, description);
  return empty;
}

async function activateTab(tab) {
  try {
    await chrome.tabs.update(tab.id, { active: true });
    await chrome.windows.update(tab.windowId, { focused: true });
  } catch (error) {
    console.error("切换标签页失败：", error);
    showToast("无法切换到该标签页。");
  }
}

async function closeTab(tab, row) {
  if (state.closingTabIds.has(tab.id)) {
    return;
  }

  state.closingTabIds.add(tab.id);
  row.classList.add("is-closing");

  try {
    await chrome.tabs.remove(tab.id);
    showToast(tab.pinned ? "已关闭固定标签页。" : "标签页已关闭。");
  } catch (error) {
    console.error("关闭标签页失败：", error);
    state.closingTabIds.delete(tab.id);
    row.classList.remove("is-closing");
    showToast("无法关闭该标签页。");
  }
}

function getMonogram(tab) {
  let source = tab.title?.trim() || "";

  if (!source && tab.url) {
    try {
      source = new URL(tab.url).hostname.replace(/^www\./, "");
    } catch {
      source = tab.url;
    }
  }

  return Array.from(source)[0]?.toLocaleUpperCase("zh-CN") || "?";
}

function getHue(tab) {
  const source = `${tab.id ?? 0}:${tab.url ?? tab.title ?? ""}`;
  let hash = 0;

  for (let index = 0; index < source.length; index += 1) {
    hash = (hash * 31 + source.charCodeAt(index)) % 360;
  }

  return hash;
}

function formatTabUrl(urlValue) {
  if (!urlValue) {
    return "新标签页";
  }

  try {
    const url = new URL(urlValue);

    if (url.protocol === "http:" || url.protocol === "https:") {
      const path = url.pathname === "/" ? "" : url.pathname;
      return `${url.hostname.replace(/^www\./, "")}${path}`;
    }

    if (urlValue === "chrome://newtab/" || urlValue === "chrome://new-tab-page/") {
      return "新标签页";
    }

    return urlValue;
  } catch {
    return urlValue;
  }
}

function showToast(message) {
  window.clearTimeout(toastTimer);
  elements.toast.textContent = message;
  elements.toast.classList.add("is-visible");

  toastTimer = window.setTimeout(() => {
    elements.toast.classList.remove("is-visible");
  }, 1900);
}

function isEditableTarget(target) {
  return (
    target instanceof HTMLInputElement ||
    target instanceof HTMLTextAreaElement ||
    (target instanceof HTMLElement && target.isContentEditable)
  );
}

async function refreshSidePanelLayout() {
  if (!chrome.sidePanel?.getLayout) {
    elements.sidePositionCard.hidden = true;
    return;
  }

  try {
    const layout = await chrome.sidePanel.getLayout();
    const side = layout?.side;

    if (side !== "left" && side !== "right") {
      elements.sidePositionCard.hidden = true;
      return;
    }

    elements.sidePositionCard.hidden = false;
    elements.sidePositionCard.classList.toggle("is-left", side === "left");

    if (side === "left") {
      elements.sidePositionTitle.textContent = "侧栏已在左侧";
      elements.sidePositionText.textContent = "位置由 Chrome 浏览器设置控制。";
      elements.openAppearanceSettings.hidden = true;
    } else {
      elements.sidePositionTitle.textContent = "侧栏当前在右侧";
      elements.sidePositionText.textContent = "Chrome 不允许扩展自行移动，请在浏览器设置中改为左侧。";
      elements.openAppearanceSettings.hidden = false;
    }
  } catch (error) {
    console.error("读取侧栏位置失败：", error);
    elements.sidePositionCard.hidden = true;
  }
}

async function loadPageDoubleClickSetting() {
  try {
    const settings = await chrome.storage.sync.get({ pageBlankDoubleClick: true });
    elements.pageDoubleClick.checked = settings.pageBlankDoubleClick !== false;
  } catch (error) {
    console.error("读取设置失败：", error);
    elements.pageDoubleClick.checked = true;
  }
}

elements.searchInput.addEventListener("input", () => {
  state.query = elements.searchInput.value;
  render();
});

elements.refreshButton.addEventListener("click", async () => {
  elements.refreshButton.classList.remove("is-spinning");
  void elements.refreshButton.offsetWidth;
  elements.refreshButton.classList.add("is-spinning");
  await refreshTabs();
  window.setTimeout(() => elements.refreshButton.classList.remove("is-spinning"), 600);
});

elements.openAppearanceSettings.addEventListener("click", async () => {
  try {
    await chrome.tabs.create({ url: "chrome://settings/appearance", active: true });
    showToast("请在外观设置的“侧栏”中选择“左侧”。");
  } catch (error) {
    console.error("打开 Chrome 外观设置失败：", error);
    showToast("请手动打开 chrome://settings/appearance，并在“外观 > 侧栏”中选择左侧。");
  }
});

elements.pageDoubleClick.addEventListener("change", async () => {
  const nextValue = elements.pageDoubleClick.checked;
  try {
    await chrome.storage.sync.set({ pageBlankDoubleClick: nextValue });
    showToast(nextValue ? "已开启网页空白处双击关闭。" : "已关闭网页空白处双击关闭。");
  } catch (error) {
    console.error("保存设置失败：", error);
    elements.pageDoubleClick.checked = !nextValue;
    showToast("设置保存失败。");
  }
});

document.addEventListener("keydown", (event) => {
  if (event.key === "/" && !isEditableTarget(event.target)) {
    event.preventDefault();
    elements.searchInput.focus();
    return;
  }

  if (event.key === "Escape" && document.activeElement === elements.searchInput) {
    elements.searchInput.value = "";
    state.query = "";
    render();
    elements.searchInput.blur();
  }
});

chrome.tabs.onCreated.addListener(() => scheduleRefresh());
chrome.tabs.onRemoved.addListener((tabId) => {
  state.closingTabIds.delete(tabId);
  scheduleRefresh();
});
chrome.tabs.onUpdated.addListener(() => scheduleRefresh());
chrome.tabs.onMoved.addListener(() => scheduleRefresh());
chrome.tabs.onAttached.addListener(() => scheduleRefresh());
chrome.tabs.onDetached.addListener(() => scheduleRefresh());
chrome.windows.onCreated.addListener(() => scheduleRefresh());
chrome.windows.onRemoved.addListener(() => scheduleRefresh());
chrome.windows.onFocusChanged.addListener(() => scheduleRefresh());

chrome.storage.onChanged.addListener((changes, areaName) => {
  if (areaName === "sync" && changes.pageBlankDoubleClick) {
    elements.pageDoubleClick.checked = changes.pageBlankDoubleClick.newValue !== false;
  }
});

document.addEventListener("visibilitychange", () => {
  if (document.visibilityState === "visible") {
    scheduleRefresh(0);
    void refreshSidePanelLayout();
  }
});

void loadPageDoubleClickSetting();
void refreshSidePanelLayout();
void refreshTabs();
