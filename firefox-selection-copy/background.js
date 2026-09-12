/**
 * Firefox Selection Clean Copy - Background Service
 * Manifest V3 Resilient Background Service:
 * 1. Keep-Alive Heartbeat: Prevents idle suspension and keeps service active
 * 2. Auto-Injection: Injects content script into existing tabs immediately upon load
 * 3. Context Menu Synchronization: Synchronously registered at top-level
 * 4. Keyboard Hotkeys (Alt+C) & Cross-Context Clipboard Copy
 */

// Cross-browser compatibility wrapper (browser / chrome)
const extApi = typeof browser !== 'undefined' ? browser : chrome;

// Top-level Context Menu Setup
function setupContextMenu() {
  if (!extApi.contextMenus) return;
  try {
    extApi.contextMenus.removeAll(() => {
      extApi.contextMenus.create({
        id: "clean-copy-selection",
        title: "📋 行番号を除去してコピー",
        contexts: ["selection"]
      });

      extApi.contextMenus.create({
        id: "clean-copy-with-prompts",
        title: "⚡ 行番号 & プロンプト記号($/>)を除去してコピー",
        contexts: ["selection"]
      });
    });
  } catch (e) {
    console.warn("[CleanCopy] setupContextMenu error:", e);
  }
}

// Ensure context menus are created immediately on top-level script load
setupContextMenu();

// Handle Context Menu clicks (Top-level listener)
extApi.contextMenus.onClicked.addListener((info, tab) => {
  if (!tab || !tab.id) return;

  const stripPrompts = (info.menuItemId === "clean-copy-with-prompts");

  // Send message to content script to clean selection and copy
  extApi.tabs.sendMessage(tab.id, {
    action: "clean_and_copy",
    stripPrompts: stripPrompts
  }).catch((err) => {
    // If content script was not ready, fallback using selectionText + tab injection
    if (info.selectionText) {
      const cleaned = cleanCodeString(info.selectionText, { stripPrompts });
      copyTextToClipboard(cleaned, tab.id);
    }
  });
});

// Handle Keyboard Shortcut (Alt+C) (Top-level listener)
extApi.commands.onCommand.addListener((command) => {
  if (command === "copy-clean-selection") {
    extApi.tabs.query({ active: true, currentWindow: true }).then((tabs) => {
      if (tabs && tabs[0] && tabs[0].id) {
        extApi.tabs.sendMessage(tabs[0].id, {
          action: "clean_and_copy",
          stripPrompts: false
        }).catch(() => {
          // If content script is missing, inject and retry
          injectIntoTab(tabs[0].id);
        });
      }
    });
  }
});

// Auto-inject into currently open tabs (Solves: "Loaded extension but already open tabs don't work")
function injectIntoTab(tabId) {
  if (!tabId || !extApi.scripting) return;
  extApi.scripting.executeScript({
    target: { tabId, allFrames: true },
    files: ["content.js"]
  }).catch((err) => {
    // Some tabs (e.g. restricted URLs) cannot be scripted
    console.debug("[CleanCopy] injectIntoTab error:", err);
  });
  extApi.scripting.insertCSS({
    target: { tabId, allFrames: true },
    files: ["content.css"]
  }).catch(() => {});
}

function injectIntoAllTabs() {
  if (!extApi.tabs || !extApi.scripting) return;
  extApi.tabs.query({}).then((tabs) => {
    for (const tab of tabs) {
      if (!tab.id || !tab.url) continue;
      if (/^(about:|chrome:|moz-extension:|edge:|resource:)/i.test(tab.url)) continue;
      injectIntoTab(tab.id);
    }
  }).catch(() => {});
}

// Immediately inject into tabs on script execution
injectIntoAllTabs();

// Lifecycle listeners
extApi.runtime.onInstalled.addListener(() => {
  setupContextMenu();
  injectIntoAllTabs();
});

extApi.runtime.onStartup.addListener(() => {
  setupContextMenu();
  injectIntoAllTabs();
});

// Keep-Alive Connection Handler from Content Scripts (Prevents background suspension)
extApi.runtime.onConnect.addListener((port) => {
  if (port.name === "clean-copy-keepalive") {
    port.onMessage.addListener((msg) => {
      if (msg && msg.type === "ping") {
        try {
          port.postMessage({ type: "pong", timestamp: Date.now() });
        } catch (e) {}
      }
    });
  }
});

// Privileged Clipboard Writer via Background Service (solves page-level document.execCommand restrictions)
extApi.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message && message.action === "bg_write_clipboard") {
    const textToCopy = message.text || "";
    if (sender.tab && sender.tab.id) {
      copyTextToClipboard(textToCopy, sender.tab.id);
      sendResponse({ status: "success" });
    } else {
      sendResponse({ status: "error", message: "No tab found" });
    }
    return true;
  }
});

// Periodic Alarms to reliably wake up background script and refresh listeners
if (extApi.alarms) {
  try {
    extApi.alarms.create("keepAliveAlarm", { periodInMinutes: 0.4 });
    extApi.alarms.onAlarm.addListener((alarm) => {
      if (alarm.name === "keepAliveAlarm") {
        setupContextMenu();
      }
    });
  } catch (e) {}
}

// Continuous runtime keepalive loop (prevents idle timeout while active)
setInterval(() => {
  if (extApi.runtime && extApi.runtime.getPlatformInfo) {
    extApi.runtime.getPlatformInfo().catch(() => {});
  }
}, 20000);

// Fallback line cleaner for string inputs
function cleanCodeString(text, options = {}) {
  if (!text) return "";
  let normalized = text.replace(/\u00A0/g, ' ');
  const lines = normalized.split(/\r?\n/);
  
  // Copilot lone number line detection
  const loneNumRegex = /^\s*\d+\s*$/;
  let loneCount = 0;
  let nonEmpty = 0;
  for (const line of lines) {
    const t = line.trim();
    if (t.length > 0) {
      nonEmpty++;
      if (loneNumRegex.test(t)) loneCount++;
    }
  }

  const isCopilotInterleaved = (loneCount >= 2) || (loneCount >= 1 && nonEmpty <= 4);
  let processed = [];

  if (isCopilotInterleaved) {
    for (let i = 0; i < lines.length; i++) {
      const line = lines[i];
      const trimmed = line.trim();
      if (loneNumRegex.test(trimmed)) {
        if (i > 0 && loneNumRegex.test(lines[i - 1].trim())) {
          processed.push("");
        }
        continue;
      }
      processed.push(line);
    }
  } else {
    processed = lines;
  }

  // Detect whether text appears to be code or plain text / prose (地の文)
  const looksLikeCode = isCopilotInterleaved ||
    /^\s*\d+[\s:\|\t]+/m.test(normalized) ||
    /^\s*\[\d+\]/m.test(normalized) ||
    /\b(import|def|function|const|let|var|class|return|export)\b/.test(normalized);

  const result = processed.map(line => {
    let l = line;
    // Strip code gutter line numbers like "  1 | ", "1: ", "1  ", "[1] "
    l = l.replace(/^\s*\d+[\s:\|\t]+/, '');
    l = l.replace(/^\s*\[\d+\]\s*/, '');

    // Only strip numbered lists if the text is code, NOT prose (地の文)
    if (looksLikeCode) {
      l = l.replace(/^\s*\d+\.\s+/, '');
    }

    if (options.stripPrompts && looksLikeCode) {
      l = l.replace(/^\s*[$%>#]\s+/, '');
    }
    return l;
  });

  return result.join('\n');
}

// Fallback clipboard writer via tab context script execution
function copyTextToClipboard(text, tabId) {
  if (!tabId || !extApi.scripting) return;
  extApi.scripting.executeScript({
    target: { tabId },
    func: (textToCopy) => {
      try {
        const textarea = document.createElement("textarea");
        textarea.value = textToCopy;
        textarea.setAttribute("readonly", "");
        textarea.style.position = "fixed";
        textarea.style.left = "-9999px";
        document.body.appendChild(textarea);
        textarea.select();
        document.execCommand("copy");
        document.body.removeChild(textarea);
      } catch (e) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
          navigator.clipboard.writeText(textToCopy).catch(() => {});
        }
      }
    },
    args: [text]
  }).catch(() => {});
}
