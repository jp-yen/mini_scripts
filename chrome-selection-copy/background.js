/**
 * Selection Clean Copy - Chrome Background Service Worker (Manifest V3)
 * 
 * 主な役割:
 * 1. コンテキストメニュー & キーボードショートカット (Alt+C) の管理
 * 2. 拡張機能ロード時・起動時の既存タブへの content.js 自動注入
 * 3. ページ側制限をバイパスする特権クリップボード書き込み (bg_write_clipboard)
 * 4. Service Worker のアイドル停止防止 (Keep-Alive)
 * 5. 完全ホワイトリスト方式による有効ドメイン判定
 */

// Cross-browser compatibility wrapper
const extApi = typeof chrome !== 'undefined' ? chrome : browser;

/* ==========================================================================
   1. コンテキストメニュー設定
   ========================================================================== */

/**
 * 右クリックコンテキストメニューの登録 (重複作成エラーを防止)
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

// サービスワーカー起動時に登録
setupContextMenu();

/* ==========================================================================
   2. ドメイン判定 (完全ホワイトリスト方式 & 関連ドメイン相互認識)
   ========================================================================== */

/**
 * ドメインの一致判定（完全一致・サブドメイン・ワイルドカード対応）
 */
function isDomainMatched(hostname, domainPattern) {
  if (!hostname || !domainPattern) return false;
  const h = hostname.toLowerCase().trim();
  let p = domainPattern.toLowerCase().trim();
  p = p.replace(/^[a-zA-Z]+:\/\//, '').split('/')[0].split(':')[0];
  if (!p) return false;

  // ワイルドカード指定 (*.example.com など) への対応
  if (p.includes('*')) {
    const escaped = p.replace(/[.+?^${}()|[\]\\]/g, '\\$&').replace(/\*/g, '.*');
    try {
      const regex = new RegExp(`^${escaped}$`, 'i');
      if (regex.test(h)) return true;
    } catch (e) {}
  }

  // 完全一致またはサブドメイン一致
  return (h === p || h.endsWith('.' + p));
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
      if (/^(chrome:|about:|edge:|chrome-extension:|resource:)/i.test(tab.url)) continue;
      injectIntoTab(tab.id);
    }
  }).catch(() => {});
}

// 拡張機能起動時に開いている全タブへ即時注入
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
/* ==========================================================================
   5. 特権クリップボード書き込み (Offscreen Document & Service Worker 対応)
   ========================================================================== */

/**
 * Offscreen Document の作成と特権書き込み (Chrome MV3 公式推奨)
 */
async function writeClipboardViaOffscreen(text) {
  if (!chrome.offscreen) return false;

  const offscreenUrl = 'offscreen.html';
  // 既存の Offscreen Document があるか確認
  try {
    if (chrome.runtime.getContexts) {
      const existing = await chrome.runtime.getContexts({
        contextTypes: ['OFFSCREEN_DOCUMENT'],
        documentUrls: [chrome.runtime.getURL(offscreenUrl)]
      });
      if (!existing || existing.length === 0) {
        await chrome.offscreen.createDocument({
          url: offscreenUrl,
          reasons: ['CLIPBOARD'],
          justification: 'Write copied code block to clipboard'
        });
      }
    } else {
      // getContexts 未対応環境のフォールバック
      await chrome.offscreen.createDocument({
        url: offscreenUrl,
        reasons: ['CLIPBOARD'],
        justification: 'Write copied code block to clipboard'
      }).catch(() => {});
    }

    // Offscreen Document に書き込みメッセージを送信
    const res = await new Promise((resolve) => {
      chrome.runtime.sendMessage({ action: 'offscreen_write_clipboard', text }, (response) => {
        if (chrome.runtime.lastError) {
          resolve(null);
        } else {
          resolve(response);
        }
      });
    });

    return !!(res && res.status === 'success');
  } catch (err) {
    return false;
  }
}

/**
 * Content Script からの特権クリップボード書き込み要求ハンドラ
 */
extApi.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message && message.action === "bg_write_clipboard") {
    const textToCopy = message.text || "";
    const targetTabId = (sender.tab && sender.tab.id) ? sender.tab.id : null;
    const targetFrameId = sender.frameId;

    // 非同期で処理を実行
    (async () => {
      // 1. Offscreen Document による特権書き込みを最優先実行 (Chrome MV3)
      const offscreenOk = await writeClipboardViaOffscreen(textToCopy);
      if (offscreenOk) {
        sendResponse({ status: "success" });
        return;
      }

      // 2. scripting.executeScript によるフォールバック書き込み
      if (targetTabId) {
        copyTextToClipboard(textToCopy, targetTabId, targetFrameId);
        sendResponse({ status: "success" });
      } else {
        const tabs = await extApi.tabs.query({ active: true, currentWindow: true }).catch(() => []);
        if (tabs && tabs[0] && tabs[0].id) {
          copyTextToClipboard(textToCopy, tabs[0].id);
          sendResponse({ status: "success" });
        } else {
          sendResponse({ status: "error", message: "No active tab" });
        }
      }
    })();

    return true; // 非同期レスポンス維持
  }
});

/**
 * タブコンテキストでのスクリプト実行によるクリップボード書き込み (フォールバック)
 */
function copyTextToClipboard(text, tabId, frameId) {
  if (!tabId || !extApi.scripting) return;
  const target = { tabId };
  if (typeof frameId === 'number') {
    target.frameIds = [frameId];
  }
  extApi.scripting.executeScript({
    target,
    func: (textToCopy) => {
      try {
        const textarea = document.createElement("textarea");
        textarea.value = textToCopy;
        textarea.setAttribute("readonly", "");
        textarea.style.position = "fixed";
        textarea.style.left = "-9999px";
        document.body.appendChild(textarea);
        textarea.focus();
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
  }).catch(() => {
    // frameId 指定で失敗した場合はタブ全体でフォールバック実行
    if (target.frameIds) {
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
            textarea.focus();
            textarea.select();
            document.execCommand("copy");
            document.body.removeChild(textarea);
          } catch (e) {}
        },
        args: [text]
      }).catch(() => {});
    }
  });
}

/**
 * 文字列ベースの簡易コードクリーニング（フォールバック用）
 */
function cleanCodeString(text, options = {}) {
  if (!text) return "";
  const normalized = text.replace(/\u00A0/g, ' ');
  const lines = normalized.split(/\r?\n/);
  
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

  const isInterleavedLineNumbers = (nonEmpty <= 4 && loneCount >= 1) || (nonEmpty > 4 && loneCount >= 2 && (loneCount / nonEmpty >= 0.25));
  let processed = [];

  if (isInterleavedLineNumbers) {
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

  const looksLikeCode = isInterleavedLineNumbers ||
    /^\s*\d+[\s:\|\t]+/m.test(normalized) ||
    /^\s*\[\d+\]/m.test(normalized) ||
    /\b(import|def|function|const|let|var|class|return|export)\b/.test(normalized);

  const result = processed.map(line => {
    let l = line;
    l = l.replace(/^\s*\d+[\s:\|\t]+/, '');
    l = l.replace(/^\s*\[\d+\]\s*/, '');

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

// Content Script からの接続ポート管理 (Service Worker 活性化維持)
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
