/**
 * Firefox Selection Clean Copy - Background Service
 * Handles context menus, command hotkeys (Alt+C), and background clipboard actions
 */

// Cross-browser compatibility wrapper (browser / chrome)
const extApi = typeof browser !== 'undefined' ? browser : chrome;

// Setup context menu on install / startup
extApi.runtime.onInstalled.addListener(() => {
  setupContextMenu();
});

function setupContextMenu() {
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
}

// Handle Context Menu clicks
extApi.contextMenus.onClicked.addListener((info, tab) => {
  if (!tab || !tab.id) return;

  const stripPrompts = (info.menuItemId === "clean-copy-with-prompts");

  // Send message to content script to clean selection and copy
  extApi.tabs.sendMessage(tab.id, {
    action: "clean_and_copy",
    stripPrompts: stripPrompts
  }).catch((err) => {
    // If content script was not injected or threw error, fallback using selectionText
    if (info.selectionText) {
      const cleaned = cleanCodeString(info.selectionText, { stripPrompts });
      copyTextToClipboard(cleaned, tab.id);
    }
  });
});

// Handle Keyboard Shortcut (e.g., Alt+C)
extApi.commands.onCommand.addListener((command) => {
  if (command === "copy-clean-selection") {
    extApi.tabs.query({ active: true, currentWindow: true }).then((tabs) => {
      if (tabs && tabs[0] && tabs[0].id) {
        extApi.tabs.sendMessage(tabs[0].id, {
          action: "clean_and_copy",
          stripPrompts: false
        }).catch(console.warn);
      }
    });
  }
});

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

  const result = processed.map(line => {
    let l = line;
    // Strip line numbers like "  1 | ", "1: ", "1  ", "[1] "
    l = l.replace(/^\s*\d+[\s:\|\t]+/, '');
    l = l.replace(/^\s*\[\d+\]\s*/, '');
    l = l.replace(/^\s*\d+\.\s+/, '');
    if (options.stripPrompts) {
      l = l.replace(/^\s*[$%>#]\s+/, '');
    }
    return l;
  });

  return result.join('\n');
}

// Fallback clipboard writer via activeTab script injection
function copyTextToClipboard(text, tabId) {
  if (navigator.clipboard && navigator.clipboard.writeText) {
    navigator.clipboard.writeText(text).catch(() => {});
  }
}
