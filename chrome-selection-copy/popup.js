/**
 * Firefox Selection Clean Copy - ポップアップ画面ロジック
 * 
 * 主な役割:
 * 1. 現在のタブのホスト名取得および有効/無効トグルの表示・操作
 * 2. 有効ドメインの管理（一覧表示、削除、手動追加）
 * 3. 行番号・プロンプト・フローティングHUD・Ctrl+C介入の設定管理
 * 4. 設定変更時の storage.sync / storage.local 保存とアクティブタブへの通知
 */

// Cross-browser compatibility wrapper
const extApi = typeof browser !== 'undefined' ? browser : chrome;

/* ==========================================================================
   1. 定数 & 状態管理
   ========================================================================== */

const defaultSettings = {
  removeLineNumbers: true,
  removePromptPrefixes: true,
  showFloatingButton: true,
  interceptCtrlC: true,
  enabledDomains: []
};

let currentTab = null;
let currentHostname = "";
let enabledDomains = [];

// DOM 要素キャッシュ
const dom = {};

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
 * 現在のホスト名が有効リストに含まれているか判定
 */
function isCurrentDomainActive() {
  if (!currentHostname) return false;
  return enabledDomains.some(d => isDomainMatched(currentHostname, d));
}

/* ==========================================================================
   3. UI 描画 & ストレージ同期
   ========================================================================== */

/**
 * 現在のサイトカード（ドメイン名、ステータスバッジ、トグルスイッチ）のUI更新
 */
function updateDomainCardUI() {
  if (!dom.currentDomainElem) return;

  if (!currentHostname) {
    dom.currentDomainElem.textContent = "このページでは利用できません";
    dom.domainStatusPill.textContent = "対象外";
    dom.domainStatusPill.className = "status-pill status-disabled";
    dom.domainToggle.disabled = true;
    dom.domainToggle.checked = false;
    dom.domainCard.classList.add("is-disabled");
    return;
  }

  dom.currentDomainElem.textContent = currentHostname;
  const active = isCurrentDomainActive();

  if (active) {
    dom.domainStatusPill.textContent = "有効";
    dom.domainStatusPill.className = "status-pill status-enabled";
    dom.domainToggle.checked = true;
    dom.domainToggle.disabled = false;
    dom.domainCard.classList.remove("is-disabled");
  } else {
    dom.domainStatusPill.textContent = "無効";
    dom.domainStatusPill.className = "status-pill status-disabled";
    dom.domainToggle.checked = false;
    dom.domainToggle.disabled = false;
    dom.domainCard.classList.add("is-disabled");
  }
}

/**
 * 有効ドメイン一覧リストの再描画
 */
function renderEnabledDomainsList() {
  if (!dom.enabledDomainList) return;

  dom.enabledDomainCount.textContent = enabledDomains.length;
  dom.enabledDomainList.innerHTML = "";

  if (enabledDomains.length === 0) {
    const emptyHint = document.createElement("div");
    emptyHint.className = "domain-empty-hint";
    emptyHint.textContent = "登録されたドメインはありません（未登録サイトでは無効）";
    dom.enabledDomainList.appendChild(emptyHint);
    return;
  }

  enabledDomains.forEach((domain) => {
    const item = document.createElement("div");
    item.className = "domain-item";

    const nameSpan = document.createElement("span");
    nameSpan.className = "domain-item-name";
    nameSpan.textContent = domain;
    nameSpan.title = domain;

    const removeBtn = document.createElement("button");
    removeBtn.type = "button";
    removeBtn.className = "domain-remove-btn";
    removeBtn.textContent = "✕";
    removeBtn.title = "このドメインをリストから削除";
    removeBtn.addEventListener("click", () => {
      removeEnabledDomain(domain);
    });

    item.appendChild(nameSpan);
    item.appendChild(removeBtn);
    dom.enabledDomainList.appendChild(item);
  });
}

/**
 * 有効ドメインリストをストレージ (sync + local) に保存し、タブに通知
 */
async function persistEnabledDomains() {
  try {
    if (extApi.storage && extApi.storage.sync) {
      await extApi.storage.sync.set({ enabledDomains });
    }
  } catch (e) {}
  try {
    if (extApi.storage && extApi.storage.local) {
      await extApi.storage.local.set({ enabledDomains });
    }
  } catch (e) {}

  updateDomainCardUI();
  renderEnabledDomainsList();

  if (currentTab && currentTab.id) {
    extApi.tabs.sendMessage(currentTab.id, {
      action: "domain_status_changed",
      enabledDomains
    }).catch(() => {});
  }
}

/**
 * ドメインを有効リストに追加
 */
function addEnabledDomain(rawDomain) {
  let d = rawDomain.trim().toLowerCase();
  d = d.replace(/^[a-zA-Z]+:\/\//, '').split('/')[0].split(':')[0];
  if (!d) return;

  if (!enabledDomains.includes(d)) {
    enabledDomains.push(d);
    persistEnabledDomains();
  }
}

/**
 * ドメインを有効リストから削除
 */
function removeEnabledDomain(domain) {
  enabledDomains = enabledDomains.filter(d => d.toLowerCase() !== domain.toLowerCase());
  persistEnabledDomains();
}

/**
 * クリーニング共通設定（チェックボックス群）の保存
 */
function saveSettings() {
  const current = {
    removeLineNumbers: dom.lineNumCheckbox.checked,
    removePromptPrefixes: dom.promptsCheckbox.checked,
    showFloatingButton: dom.floatBtnCheckbox.checked,
    interceptCtrlC: dom.ctrlCCheckbox.checked
  };

  if (extApi.storage && extApi.storage.sync) {
    extApi.storage.sync.set(current);
  }
}

/* ==========================================================================
   4. 初期化 & イベントリスナー接続
   ========================================================================== */

document.addEventListener("DOMContentLoaded", async () => {
  // DOM 要素のキャッシュ
  dom.domainCard = document.getElementById("domainCard");
  dom.currentDomainElem = document.getElementById("currentDomainName");
  dom.domainStatusPill = document.getElementById("domainStatusPill");
  dom.domainToggle = document.getElementById("domainToggle");

  dom.domainManagerToggle = document.getElementById("domainManagerToggle");
  dom.domainManagerArrow = document.getElementById("domainManagerArrow");
  dom.domainManagerContent = document.getElementById("domainManagerContent");
  dom.enabledDomainCount = document.getElementById("enabledDomainCount");
  dom.manualDomainInput = document.getElementById("manualDomainInput");
  dom.manualAddDomainBtn = document.getElementById("manualAddDomainBtn");
  dom.enabledDomainList = document.getElementById("enabledDomainList");

  dom.lineNumCheckbox = document.getElementById("removeLineNumbers");
  dom.promptsCheckbox = document.getElementById("removePromptPrefixes");
  dom.floatBtnCheckbox = document.getElementById("showFloatingButton");
  dom.ctrlCCheckbox = document.getElementById("interceptCtrlC");

  // 1. 現在開いているタブのドメインを取得（Firefox に最も確実な lastFocusedWindow を最優先）
  try {
    let tabs = await extApi.tabs.query({ active: true, lastFocusedWindow: true });
    if (!tabs || tabs.length === 0 || !tabs[0].url) {
      tabs = await extApi.tabs.query({ active: true, currentWindow: true });
    }
    if (!tabs || tabs.length === 0 || !tabs[0].url) {
      tabs = await extApi.tabs.query({ active: true });
    }

    if (tabs && tabs[0]) {
      currentTab = tabs[0];
      if (currentTab.url) {
        try {
          const url = new URL(currentTab.url);
          if (/^(http:|https:)/i.test(url.protocol)) {
            currentHostname = url.hostname.toLowerCase();
          }
        } catch (e) {}
      }
    }
  } catch (err) {}

  // URL から直接取得できなかった場合、content.js にホスト名を問い合わせ
  if (!currentHostname && currentTab && currentTab.id) {
    try {
      const resp = await extApi.tabs.sendMessage(currentTab.id, { action: "get_hostname" });
      if (resp && resp.hostname) {
        currentHostname = resp.hostname.toLowerCase();
      }
    } catch (e) {}
  }

  // 2. ストレージから設定値を読み込み (sync -> local フォールバック)
  const storageArea = (extApi.storage && extApi.storage.sync) ? extApi.storage.sync : (extApi.storage ? extApi.storage.local : null);
  if (storageArea) {
    storageArea.get(defaultSettings, (saved) => {
      dom.lineNumCheckbox.checked = (saved.removeLineNumbers !== undefined) ? saved.removeLineNumbers : true;
      dom.promptsCheckbox.checked = (saved.removePromptPrefixes !== undefined) ? saved.removePromptPrefixes : true;
      dom.floatBtnCheckbox.checked = (saved.showFloatingButton !== undefined) ? saved.showFloatingButton : true;
      dom.ctrlCCheckbox.checked = (saved.interceptCtrlC !== undefined) ? saved.interceptCtrlC : true;
      
      enabledDomains = Array.isArray(saved.enabledDomains) ? saved.enabledDomains : [];

      updateDomainCardUI();
      renderEnabledDomainsList();
    });
  } else {
    updateDomainCardUI();
    renderEnabledDomainsList();
  }

  // 3. 現在のサイト有効/無効トグルのイベント設定
  dom.domainToggle.addEventListener("change", () => {
    if (!currentHostname) return;

    if (dom.domainToggle.checked) {
      addEnabledDomain(currentHostname);
    } else {
      enabledDomains = enabledDomains.filter(d => !isDomainMatched(currentHostname, d));
      persistEnabledDomains();
    }
  });

  // 4. ドメイン管理アコーディオンの開閉イベント
  dom.domainManagerToggle.addEventListener("click", () => {
    const isCollapsed = dom.domainManagerContent.classList.contains("domain-manager-collapsed");
    if (isCollapsed) {
      dom.domainManagerContent.classList.remove("domain-manager-collapsed");
      dom.domainManagerArrow.classList.add("is-open");
    } else {
      dom.domainManagerContent.classList.add("domain-manager-collapsed");
      dom.domainManagerArrow.classList.remove("is-open");
    }
  });

  // 5. 手動ドメイン追加イベント
  function handleManualAdd() {
    const val = dom.manualDomainInput.value.trim();
    if (val) {
      addEnabledDomain(val);
      dom.manualDomainInput.value = "";
    }
  }

  dom.manualAddDomainBtn.addEventListener("click", handleManualAdd);
  dom.manualDomainInput.addEventListener("keydown", (e) => {
    if (e.key === "Enter") {
      handleManualAdd();
    }
  });

  // 6. 各設定チェックボックスの変更イベント
  dom.lineNumCheckbox.addEventListener("change", saveSettings);
  dom.promptsCheckbox.addEventListener("change", saveSettings);
  dom.floatBtnCheckbox.addEventListener("change", saveSettings);
  dom.ctrlCCheckbox.addEventListener("change", saveSettings);
});
