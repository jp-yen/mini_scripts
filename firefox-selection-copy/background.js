/**
 * Firefox Selection Clean Copy - Background Service
 * 
 * 主な役割:
 * 1. コンテキストメニュー & キーボードショートカット (Alt+C) の管理
 * 2. 拡張機能ロード時・起動時の既存タブへの content.js 自動注入
 * 3. ページ側制限をバイパスする特権クリップボード書き込み (bg_write_clipboard)
 * 4. バックグラウンドサービスのアイドル停止防止 (Keep-Alive)
 * 5. 完全ホワイトリスト方式による有効ドメイン判定
 */

// Cross-browser compatibility wrapper (browser / chrome)
const extApi = typeof browser !== 'undefined' ? browser : chrome;

/* ==========================================================================
   1. コンテキストメニュー設定
   ========================================================================== */

/**
 * 右クリックコンテキストメニューの登録
 */
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

// スクリプト読み込み時に同期登録
setupContextMenu();

/* ==========================================================================
   2. ドメイン判定 (完全ホワイトリスト方式 & Copilotファミリー相互認識)
   ========================================================================== */

/**
 * ドメインの一致判定（サブドメイン対応 & 関連サービス連動）
 */
function isDomainMatched(hostname, domainPattern) {
  if (!hostname || !domainPattern) return false;
  const h = hostname.toLowerCase().trim();
  let p = domainPattern.toLowerCase().trim();
  p = p.replace(/^[a-zA-Z]+:\/\//, '').split('/')[0].split(':')[0];
  if (!p) return false;

  // 1. 完全一致またはサブドメイン一致
  if (h === p || h.endsWith('.' + p)) return true;

  // 2. Copilot / Bing サービスファミリーの相互認識
  const copilotFamily = ['copilot.microsoft.com', 'bing.com', 'edgeservices.bing.com', 'cloud.microsoft'];
  const pIsCopilot = copilotFamily.some(f => p === f || p.endsWith('.' + f));
  const hIsCopilot = copilotFamily.some(f => h === f || h.endsWith('.' + f));
  return pIsCopilot && hIsCopilot;
}

/**
 * タブの URL が登録済み有効ドメインに含まれているかを判定
 */
async function isDomainEnabledForTab(tab) {
  if (!tab || !tab.url) return false;
  try {
    const url = new URL(tab.url);
    const hostname = url.hostname.toLowerCase();
    if (!hostname) return false;

    const data = await extApi.storage.sync.get({ enabledDomains: [] });
    const list = Array.isArray(data.enabledDomains) ? data.enabledDomains : [];

    // 完全ホワイトリスト方式：未登録（空）または指定外では動作しない
    if (list.length === 0) return false;
    return list.some(d => isDomainMatched(hostname, d));
  } catch (e) {
    return false;
  }
}

/* ==========================================================================
   3. メニュー操作 & ショートカットハンドラ
   ========================================================================== */

// コンテキストメニュークリック時のハンドラ
extApi.contextMenus.onClicked.addListener(async (info, tab) => {
  if (!tab || !tab.id) return;
  if (!await isDomainEnabledForTab(tab)) return;

  const stripPrompts = (info.menuItemId === "clean-copy-with-prompts");

  // Content script にクリーンコピーを要求
  extApi.tabs.sendMessage(tab.id, {
    action: "clean_and_copy",
    stripPrompts: stripPrompts
  }).catch(() => {
    // Content script 未準備時のフォールバック処理
    if (info.selectionText) {
      const cleaned = cleanCodeString(info.selectionText, { stripPrompts });
      copyTextToClipboard(cleaned, tab.id);
    }
  });
});

// ショートカットキー (Alt+C) のハンドラ
extApi.commands.onCommand.addListener(async (command) => {
  if (command === "copy-clean-selection") {
    try {
      const tabs = await extApi.tabs.query({ active: true, currentWindow: true });
      if (tabs && tabs[0] && tabs[0].id) {
        if (!await isDomainEnabledForTab(tabs[0])) return;
        extApi.tabs.sendMessage(tabs[0].id, {
          action: "clean_and_copy",
          stripPrompts: false
        }).catch(() => {
          injectIntoTab(tabs[0].id);
        });
      }
    } catch (e) {}
  }
});

/* ==========================================================================
   4. タブへのスクリプト自動注入 & ライフサイクル管理
   ========================================================================== */

/**
 * 指定タブに content script と CSS を注入
 */
function injectIntoTab(tabId) {
  if (!tabId || !extApi.scripting) return;
  extApi.scripting.executeScript({
    target: { tabId, allFrames: true },
    files: ["content.js"]
  }).catch(() => {});

  extApi.scripting.insertCSS({
    target: { tabId, allFrames: true },
    files: ["content.css"]
  }).catch(() => {});
}

/**
 * 開いているすべての通常タブにスクリプトを注入
 */
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

// スクリプト起動時に開いている全タブへ即時注入
injectIntoAllTabs();

// インストール・アップデート・起動時リスナー
extApi.runtime.onInstalled.addListener(() => {
  setupContextMenu();
  injectIntoAllTabs();
});

extApi.runtime.onStartup.addListener(() => {
  setupContextMenu();
  injectIntoAllTabs();
});

/* ==========================================================================
   5. 特権クリップボード書き込み (Background Service)
   ========================================================================== */

/**
 * ページ内 document.execCommand 制限をバイパスする特権書き込みリスナー
 */
extApi.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message && message.action === "bg_write_clipboard") {
    const textToCopy = message.text || "";

    // 1. 特権 Background 環境での navigator.clipboard.writeText
    if (typeof navigator !== 'undefined' && navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(textToCopy).then(() => {
        sendResponse({ status: "success" });
      }).catch(() => {
        const ok = fallbackExecCommand(textToCopy);
        if (ok) {
          sendResponse({ status: "success" });
        } else if (sender.tab && sender.tab.id) {
          copyTextToClipboard(textToCopy, sender.tab.id);
          sendResponse({ status: "success" });
        } else {
          sendResponse({ status: "error", message: "Copy failed" });
        }
      });
      return true;
    }

    // 2. navigator.clipboard 非対応時のフォールバック
    const ok = fallbackExecCommand(textToCopy);
    if (ok) {
      sendResponse({ status: "success" });
    } else if (sender.tab && sender.tab.id) {
      copyTextToClipboard(textToCopy, sender.tab.id);
      sendResponse({ status: "success" });
    } else {
      sendResponse({ status: "error", message: "Copy failed" });
    }
    return true;
  }
});

/**
 * Background ページコンテキストでの execCommand コピー補助
 */
function fallbackExecCommand(text) {
  try {
    if (typeof document !== 'undefined' && document.body) {
      const textarea = document.createElement("textarea");
      textarea.value = text;
      textarea.style.position = "fixed";
      textarea.style.left = "-9999px";
      document.body.appendChild(textarea);
      textarea.select();
      const ok = document.execCommand("copy");
      document.body.removeChild(textarea);
      return ok;
    }
  } catch (e) {}
  return false;
}

/**
 * タブコンテキストでのスクリプト実行によるクリップボード書き込みフォールバック
 */
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

/**
 * 文字列ベースの簡易コードクリーニング（フォールバック用）
 */
function cleanCodeString(text, options = {}) {
  if (!text) return "";
  const normalized = text.replace(/\u00A0/g, ' ');
  const lines = normalized.split(/\r?\n/);
  
  // Copilot 分離行番号の判定
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

  // コードらしい特徴の有無を判定
  const looksLikeCode = isCopilotInterleaved ||
    /^\s*\d+[\s:\|\t]+/m.test(normalized) ||
    /^\s*\[\d+\]/m.test(normalized) ||
    /\b(import|def|function|const|let|var|class|return|export)\b/.test(normalized);

  const result = processed.map(line => {
    let l = line;
    // 行番号 (" 1 | ", "1: ", "[1] ") を除去
    l = l.replace(/^\s*\d+[\s:\|\t]+/, '');
    l = l.replace(/^\s*\[\d+\]\s*/, '');

    // コードと判定された場合のみ番号付きリスト・プロンプトを除去
    if (looksLikeCode) {
      l = l.replace(/^\s*\d+\.\s+/, '');
      if (options.stripPrompts) {
        l = l.replace(/^\s*[$%>#]\s+/, '');
      }
    }
    return l;
  });

  return result.join('\n');
}

/* ==========================================================================
   6. Keep-Alive & アイドル停止防止
   ========================================================================== */

// Content Script からの接続ポート管理
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

// アラームによる定期ウェイクアップ（コンテキストメニューの整合性維持）
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

// アイドルタイムアウト防止用ループ
setInterval(() => {
  if (extApi.runtime && extApi.runtime.getPlatformInfo) {
    extApi.runtime.getPlatformInfo().catch(() => {});
  }
}, 20000);
