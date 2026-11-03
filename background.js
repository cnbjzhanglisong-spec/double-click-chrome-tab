const DEFAULT_SETTINGS = Object.freeze({
  pageBlankDoubleClick: true,
});

async function ensureDefaultSettings() {
  try {
    const stored = await chrome.storage.sync.get(Object.keys(DEFAULT_SETTINGS));
    const missing = {};

    for (const [key, value] of Object.entries(DEFAULT_SETTINGS)) {
      if (typeof stored[key] === "undefined") {
        missing[key] = value;
      }
    }

    if (Object.keys(missing).length > 0) {
      await chrome.storage.sync.set(missing);
    }
  } catch (error) {
    console.error("初始化设置失败：", error);
  }
}

async function injectContentScriptIntoTabs() {
  try {
    const tabs = await chrome.tabs.query({});
    const injectableTabs = tabs.filter(
      (tab) =>
        typeof tab.id === "number" &&
        typeof tab.url === "string" &&
        /^(https?|file):/i.test(tab.url),
    );

    await Promise.allSettled(
      injectableTabs.map((tab) =>
        chrome.scripting.executeScript({
          target: { tabId: tab.id },
          files: ["content.js"],
        }),
      ),
    );
  } catch (error) {
    console.error("向已打开页面注入双击检测失败：", error);
  }
}

chrome.runtime.onInstalled.addListener(() => {
  void ensureDefaultSettings();
  void injectContentScriptIntoTabs();
});

chrome.runtime.onStartup.addListener(() => {
  void ensureDefaultSettings();
  void injectContentScriptIntoTabs();
});

void ensureDefaultSettings();

chrome.sidePanel
  .setPanelBehavior({ openPanelOnActionClick: true })
  .catch((error) => console.error("设置侧栏行为失败：", error));

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type !== "CLOSE_CURRENT_TAB") {
    return false;
  }

  const tabId = sender.tab?.id;
  if (typeof tabId !== "number") {
    sendResponse({ ok: false, reason: "无法确定当前标签页" });
    return false;
  }

  chrome.tabs
    .remove(tabId)
    .then(() => sendResponse({ ok: true }))
    .catch((error) => {
      console.error("关闭标签页失败：", error);
      sendResponse({ ok: false, reason: error.message });
    });

  return true;
});
