/**
 * Firefox Selection Clean Copy - Content Script
 * 
 * 主な機能:
 * 1. 行番号・プロンプト記号・UIゴミのインテリジェント自動除去 (コード / 地の文 自動判別)
 * 2. コピー制限・Ctrl+C禁止環境のキャプチャ相バイパス
 * 3. コードブロックに対する「行番号なし全コピー」ボタンの自動注入 (1ブロック1ボタン保証)
 * 4. テキスト選択時のフローティングHUDメニュー
 * 5. 完全ホワイトリスト方式による指定ドメイン限定動作
 */

(function () {
  if (window.__ffSelectionCleanCopyInjected) return;
  window.__ffSelectionCleanCopyInjected = true;

  const extApi = typeof browser !== 'undefined' ? browser : chrome;

  /* ==========================================================================
     1. 設定 & 状態管理
     ========================================================================== */
  let config = {
    removeLineNumbers: true,
    preserveIndentation: true,
    showFloatingButton: true,
    interceptCtrlC: true,
    showNotificationToast: true,
    enabledDomains: []
  };

  let floatingBtn = null;
  let toastElem = null;
  let savedSelectionRange = null;
  let savedSelectionText = "";
  let lastCopyEventTimestamp = 0;
  let keepAlivePort = null;

  // ツールバー等で頻出するプログラミング言語バッジ一覧
  const CODE_LANGUAGE_BADGES = new Set([
    'plain text', 'plaintext', 'python', 'py', 'javascript', 'js', 'typescript', 'ts',
    'bash', 'sh', 'shell', 'zsh', 'powershell', 'ps1', 'cmd', 'batch', 'bat',
    'html', 'htm', 'xhtml', 'css', 'scss', 'sass', 'less',
    'json', 'jsonc', 'yaml', 'yml', 'xml', 'sql', 'pgsql', 'mysql', 'sqlite',
    'c#', 'csharp', 'cs', 'c++', 'cpp', 'c', 'h', 'hpp',
    'java', 'go', 'golang', 'rust', 'rs', 'ruby', 'rb', 'php',
    'swift', 'kotlin', 'kt', 'dart', 'lua', 'perl', 'pl', 'r',
    'markdown', 'md', 'text', 'txt', 'graphql', 'dockerfile', 'ini', 'toml',
    'diff', 'dos', 'code'
  ]);

  /* ==========================================================================
     2. ドメイン判定 (完全ホワイトリスト方式 & 関連iframe連動)
     ========================================================================== */

  /**
   * ドメインのマッチング判定（サブドメイン対応 & 関連サービスiframe連動）
   */
  function isDomainMatched(hostname, domainPattern) {
    if (!hostname || !domainPattern) return false;
    const h = hostname.toLowerCase().trim();
    let p = domainPattern.toLowerCase().trim();
    p = p.replace(/^[a-zA-Z]+:\/\//, '').split('/')[0].split(':')[0];
    if (!p) return false;

    // 1. 完全一致またはサブドメイン一致
    if (h === p || h.endsWith('.' + p)) return true;

    // 2. 関連サービスファミリーの相互認識
    const relatedDomains = ['copilot.microsoft.com', 'bing.com', 'edgeservices.bing.com', 'cloud.microsoft'];
    const pIsRelated = relatedDomains.some(f => p === f || p.endsWith('.' + f));
    const hIsRelated = relatedDomains.some(f => h === f || h.endsWith('.' + f));
    return pIsRelated && hIsRelated;
  }

  /**
   * 現在のページが有効ドメインに含まれているかを判定（iframe・referrerも考慮）
   */
  function isCurrentDomainEnabled() {
    const hosts = [];
    if (window.location && window.location.hostname) {
      hosts.push(window.location.hostname.toLowerCase());
    }
    try {
      if (window.top && window.top !== window && window.top.location && window.top.location.hostname) {
        hosts.push(window.top.location.hostname.toLowerCase());
      }
    } catch (e) { }
    try {
      if (window.parent && window.parent !== window && window.parent.location && window.parent.location.hostname) {
        hosts.push(window.parent.location.hostname.toLowerCase());
      }
    } catch (e) { }
    try {
      if (document.referrer) {
        const refUrl = new URL(document.referrer);
        if (refUrl.hostname) hosts.push(refUrl.hostname.toLowerCase());
      }
    } catch (e) { }

    if (hosts.length === 0) return false;

    const domains = Array.isArray(config.enabledDomains) ? config.enabledDomains : [];
    // 未登録（空）または指定外のドメインでは一切動作しない（完全ホワイトリスト方式）
    if (domains.length === 0) return false;
    return hosts.some(h => domains.some(d => isDomainMatched(h, d)));
  }

  /**
   * ドメイン有効/無効状態に応じた画面クリーンアップ＆ボタン注入
   */
  function applyDomainStatus() {
    const enabled = isCurrentDomainEnabled();
    if (!enabled) {
      document.querySelectorAll('.ff-clean-copy-injected-btn').forEach(btn => btn.remove());
      hideFloatingButton();
    } else {
      injectCopyButtons();
    }
    return enabled;
  }

  /**
   * 保存済み設定の読み込み (sync と local の両方から安全にロード)
   */
  function loadConfig() {
    const handleLoaded = (saved) => {
      if (saved) {
        config = { ...config, ...saved };
        if (!Array.isArray(config.enabledDomains)) {
          config.enabledDomains = [];
        }
        applyDomainStatus();
      }
    };

    if (extApi.storage && extApi.storage.sync) {
      extApi.storage.sync.get(config, (saved) => {
        if (saved && Array.isArray(saved.enabledDomains) && saved.enabledDomains.length > 0) {
          handleLoaded(saved);
        } else if (extApi.storage.local) {
          extApi.storage.local.get(config, (localSaved) => {
            handleLoaded(localSaved || saved);
          });
        } else {
          handleLoaded(saved);
        }
      });
    } else if (extApi.storage && extApi.storage.local) {
      extApi.storage.local.get(config, handleLoaded);
    }
  }

  loadConfig();

  // 設定変更時のリアルタイム反映リスナー
  if (extApi.storage && extApi.storage.onChanged) {
    extApi.storage.onChanged.addListener((changes, area) => {
      if (area === "sync" || area === "local") {
        let changed = false;
        for (const key in changes) {
          if (key in config) {
            config[key] = changes[key].newValue;
            changed = true;
          }
        }
        if (changed) {
          applyDomainStatus();
        }
      }
    });
  }

  /* ==========================================================================
     3. クリップボード書き込み (安全フォールバック)
     ========================================================================== */

  /**
   * クリップボード書き込みエンジン
   * 1. navigator.clipboard.writeText (ユーザー操作の同期コンテキストで即時成功)
   * 2. document.execCommand("copy") (同期フォールバック)
   * 3. バックグラウンド特権書き込み (非同期走査後や権限制限時のフォールバック)
   */
  async function writeClipboard(text) {
    if (!text) return false;

    // 1. モダン Clipboard API
    if (typeof navigator !== 'undefined' && navigator.clipboard && navigator.clipboard.writeText) {
      try {
        await navigator.clipboard.writeText(text);
        return true;
      } catch (e) { }
    }

    // 2. 同期 execCommand フォールバック
    try {
      const textarea = document.createElement("textarea");
      textarea.value = text;
      textarea.setAttribute("readonly", "");
      textarea.style.position = "fixed";
      textarea.style.top = "0";
      textarea.style.left = "-9999px";
      textarea.style.opacity = "0";
      textarea.style.pointerEvents = "none";
      document.body.appendChild(textarea);

      let handled = false;
      const copyListener = (e) => {
        handled = true;
        e.stopImmediatePropagation();
        e.preventDefault();
        if (e.clipboardData) {
          e.clipboardData.setData("text/plain", text);
        }
      };

      document.addEventListener("copy", copyListener, true);
      textarea.focus();
      textarea.select();
      textarea.setSelectionRange(0, textarea.value.length);
      const ok = document.execCommand("copy");
      document.removeEventListener("copy", copyListener, true);
      document.body.removeChild(textarea);

      if (ok || handled) return true;
    } catch (e) { }

    // 3. バックグラウンド特権書き込みフォールバック (タイムアウト1秒)
    try {
      if (extApi.runtime && extApi.runtime.sendMessage) {
        const resp = await new Promise((resolve) => {
          const timer = setTimeout(() => resolve(null), 1000);
          extApi.runtime.sendMessage({ action: "bg_write_clipboard", text }, (res) => {
            clearTimeout(timer);
            resolve(res);
          });
        });
        if (resp && resp.status === "success") {
          return true;
        }
      }
    } catch (e) { }

    return false;
  }

  /* ==========================================================================
     4. テキスト解析 & クリーニングエンジン
     ========================================================================== */

  /**
   * 要素がコードブロックやエディタ領域の内部にあるかを判定
   */
  function isInsideCodeBlock(el) {
    if (!el) return false;
    const codeSelectors = [
      '.scriptor-component-code-block',
      '[role="textbox"][aria-label*="コード"]',
      '[role="textbox"][aria-label*="code" i]',
      '[data-virtualized-code-find-root]',
      '.scriptor-codeblock-virtualized',
      'pre',
      'code',
      '.monaco-editor',
      '.CodeMirror',
      'table.highlight',
      '.blob-wrapper'
    ];
    return !!el.closest?.(codeSelectors.join(', '));
  }

  /**
   * 選択テキストを「地の文 (Prose)」と「プログラムコード」に自動分類
   */
  function analyzeTextSelection(rawText, containerEl) {
    if (!rawText || !rawText.trim()) {
      return { isProse: true, isCode: false, isVirtualized: false, hasLineNumbers: false };
    }

    const insideCode = isInsideCodeBlock(containerEl);
    const normalized = rawText.replace(/\u00A0/g, ' ');
    const lines = normalized.split(/\r?\n/).map(l => l.trim()).filter(Boolean);

    // 1. 分離された行番号 (例: "import os\n2\nfrom...")
    const loneNumRegex = /^\s*\d+\s*$/;
    const loneNums = lines.filter(l => loneNumRegex.test(l));
    const isInterleavedLineNumbers = (loneNums.length >= 2) || (loneNums.length >= 1 && lines.length <= 4 && insideCode);

    // 2. 行頭の区切り記号付き行番号 (" 1 | code", "1: code", "[1] code")
    const gutterNums = lines.filter(l => /^\d+\s*[\:\|]\s+/.test(l) || /^\[\d+\]\s*/.test(l));
    const hasGutterNums = gutterNums.length >= 2 || (gutterNums.length >= 1 && insideCode);

    // 3. スペース区切りの行番号 ("  1   const a = 10;")
    const spacePrefixed = lines.filter(l => /^\d{1,4}\s{2,}\S/.test(l));
    const hasSpaceNums = spacePrefixed.length >= 2 && insideCode;

    const hasLineNumbers = isInterleavedLineNumbers || hasGutterNums || hasSpaceNums;
    const isCode = insideCode || hasLineNumbers;

    const virtualizedBlock = containerEl?.closest?.(
      '.scriptor-component-code-block, [data-virtualized-code-find-root], .scriptor-codeblock-virtualized'
    );

    return {
      isProse: !isCode,
      isCode,
      isVirtualized: !!virtualizedBlock,
      virtualizedBlock: virtualizedBlock || null,
      hasLineNumbers
    };
  }

  /**
   * 拡張機能ボタン、ツールバー、言語バッジ、折りたたみボタン等のUIゴミをテキストから除去
   */
  function stripExtraneousUIGarbage(rawText) {
    if (!rawText) return "";

    const lines = rawText.split(/\r?\n/);
    const cleanedLines = [];

    for (let i = 0; i < lines.length; i++) {
      const line = lines[i];
      const trimmed = line.trim();

      // 1. 本アドオンのボタンテキスト・通知メッセージ
      if (
        /^(📋\s*)?行番号なし全コピー$/i.test(trimmed) ||
        /^(⏳\s*)?収集中(\.{1,3})?$/i.test(trimmed) ||
        /^(✅\s*)?コピー完了[！!]?$/i.test(trimmed) ||
        /^(📋\s*)?選択範囲をコピー$/i.test(trimmed) ||
        /^(📜\s*)?全体を走査コピー$/i.test(trimmed) ||
        /^(📋\s*)?地の文をコピー$/i.test(trimmed) ||
        /^全コードコピー$/i.test(trimmed)
      ) {
        continue;
      }

      // 2. Web UI のアクションボタン・フッター
      if (
        /^その他の行を表示(する)?$/i.test(trimmed) ||
        /^Show (more|all) lines$/i.test(trimmed) ||
        /^すべて表示$/i.test(trimmed) ||
        /^((コード|プログラム)を?)?コピー(する)?$/i.test(trimmed) ||
        /^Copy( code)?$/i.test(trimmed) ||
        /^コピーしました$/i.test(trimmed) ||
        /^Copied!?$/i.test(trimmed) ||
        /^コードを(展開|折りたたむ)$/i.test(trimmed) ||
        /^(展開|折りたたむ)$/i.test(trimmed) ||
        /^(Expand|Collapse)$/i.test(trimmed) ||
        /^Run code$/i.test(trimmed)
      ) {
        continue;
      }

      // 3. 単独のプログラミング言語名バッジ (例: "Plain Text", "Python")
      if (trimmed.length >= 2 && CODE_LANGUAGE_BADGES.has(trimmed.toLowerCase())) {
        continue;
      }

      cleanedLines.push(line);
    }

    // 前後の余分な空行を除去（コード内部の連続する空行は表示との比較のためそのまま完全に保持）
    let s = 0;
    while (s < cleanedLines.length && cleanedLines[s].trim() === '') s++;
    let e = cleanedLines.length - 1;
    while (e >= s && cleanedLines[e].trim() === '') e--;
    if (s > e) return "";
    return cleanedLines.slice(s, e + 1).join('\n');
  }

  /**
   * メインテキストクリーニング処理
   * 地の文の箇条書き（1. , 2. ）は保持しつつ、コード領域の行番号・プロンプト・UIゴミを除去
   */
  function cleanSelectedText(rawText, options = {}) {
    if (!rawText) return "";

    let normalized = rawText.replace(/\u00A0/g, ' ');
    normalized = stripExtraneousUIGarbage(normalized);

    // 地の文の場合は行番号判定を行わずそのまま返す
    if (options.isProse) {
      return normalized;
    }

    const lines = normalized.split(/\r?\n/);
    if (lines.length === 0) return "";

    const stripNumbers = options.removeLineNumbers ?? config.removeLineNumbers;

    // パターン1: 分離された行番号 (2, 3...)
    let loneNumberLineCount = 0;
    let totalNonEmpty = 0;
    const loneNumRegex = /^\s*\d+\s*$/;

    for (let i = 0; i < lines.length; i++) {
      const trimmed = lines[i].trim();
      if (trimmed.length > 0) {
        totalNonEmpty++;
        if (loneNumRegex.test(trimmed)) loneNumberLineCount++;
      }
    }

    const isInterleavedLineNumbers = (loneNumberLineCount >= 2) || (loneNumberLineCount >= 1 && totalNonEmpty <= 4);
    let processedLines = [];

    if (stripNumbers && isInterleavedLineNumbers) {
      for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        const trimmed = line.trim();
        if (loneNumRegex.test(trimmed)) {
          if (i > 0 && loneNumRegex.test(lines[i - 1].trim())) {
            processedLines.push("");
          }
          continue;
        }
        processedLines.push(line);
      }
    } else {
      processedLines = lines;
    }

    // パターン2: 行頭の行番号 (1 | , 1:, [1])
    // コード本文の内容・記号（$, >, #, %, // など）は改変せずそのまま保持
    const cleanedLines = processedLines.map((line) => {
      let l = line;
      if (stripNumbers) {
        l = l.replace(/^\s*\d+[\s:\|\t]+/, '');
        l = l.replace(/^\s*\[\d+\]\s*/, '');
      }
      return l;
    });

    // 前後の空行を除去して結合
    let start = 0;
    while (start < cleanedLines.length && cleanedLines[start].trim() === '') start++;
    let end = cleanedLines.length - 1;
    while (end >= start && cleanedLines[end].trim() === '') end--;

    if (start > end) return "";
    return cleanedLines.slice(start, end + 1).join('\n');
  }

  /* ==========================================================================
     5. コードブロック抽出 & ボタン自動注入
     ========================================================================== */

  /**
   * コードブロックの最上位ルートコンテナを見つける
   */
  function findCodeBlockRoot(el) {
    if (!el) return null;
    const top = el.closest(
      '.scriptor-component-code-block, .code-block, pre, [data-content="code"], .monaco-editor, .CodeMirror'
    );
    return top || el.closest('.scriptor-codeblock-virtualized, [role="textbox"]') || el;
  }

  /**
   * コードブロック内のスクロール可能コンテナを見つける
   */
  function findScrollableElement(block) {
    if (!block) return null;
    const candidates = [
      block.querySelector('[role="textbox"]'),
      block.querySelector('[data-virtualized-code-find-root]'),
      block.querySelector('.scriptor-codeblock-virtualized'),
      block
    ];

    for (const c of candidates) {
      if (c && c.scrollHeight > c.clientHeight && c.clientHeight > 40) {
        return c;
      }
    }

    const allElements = block.querySelectorAll('*');
    for (const el of allElements) {
      const style = window.getComputedStyle(el);
      if ((style.overflowY === 'auto' || style.overflowY === 'scroll') && el.scrollHeight > el.clientHeight + 15) {
        return el;
      }
    }
    return null;
  }

  /**
   * コードブロック全体のテキストを安全に収集してコピー (即時同期コピー先行 + 走査フォールバック)
   */
  async function copyCodeBlock(targetBlock) {
    if (!targetBlock) return;

    const block = findCodeBlockRoot(targetBlock) || targetBlock;
    const scrollContainer = findScrollableElement(block) || findScrollableElement(targetBlock);
    const lineMap = new Map();

    // 表示中の行要素からテキストを収集
    const harvestVisibleLines = () => {
      const containers = [block, scrollContainer, targetBlock].filter(Boolean);
      for (const c of containers) {
        const lineElements = c.querySelectorAll('[data-line-index]');
        if (lineElements.length > 0) {
          lineElements.forEach(el => {
            const idxStr = el.getAttribute('data-line-index');
            if (idxStr !== null) {
              const idx = parseInt(idxStr, 10);
              if (!isNaN(idx)) {
                lineMap.set(idx, el.textContent.replace(/\u00A0/g, ' '));
              }
            }
          });
        }
      }
    };

    // UIボタン等のノイズを除去してテキストをフォールバック抽出
    const getFallbackCode = () => {
      const codeEl = block.querySelector('code, pre, [role="textbox"], .scriptor-codeblock-virtualized');
      if (codeEl) {
        const clone = codeEl.cloneNode(true);
        clone.querySelectorAll('.ff-clean-copy-injected-btn, [data-ff-clean-copy-ui], .blob-num, .line-numbers-rows').forEach(el => el.remove());
        const t = clone.innerText || clone.textContent;
        if (t && t.trim()) return t;
      }
      const blockClone = block.cloneNode(true);
      blockClone.querySelectorAll('.ff-clean-copy-injected-btn, [data-ff-clean-copy-ui], .blob-num, .line-numbers-rows').forEach(el => el.remove());
      return blockClone.innerText || blockClone.textContent || "";
    };

    harvestVisibleLines();

    // 1. まず同期コンテキスト（User Activation 生存時）で即座に取得できるコードをコピー
    let initialCode = '';
    if (lineMap.size > 0) {
      const sorted = Array.from(lineMap.keys()).sort((a, b) => a - b);
      initialCode = sorted.map(k => lineMap.get(k)).join('\n');
    }
    if (!initialCode || !initialCode.trim()) {
      initialCode = getFallbackCode();
    }
    initialCode = cleanSelectedText(initialCode, { isProse: false });

    const needsScrollScan = scrollContainer && (scrollContainer.scrollHeight > scrollContainer.clientHeight + 80);

    if (initialCode) {
      const ok = await writeClipboard(initialCode);
      if (ok && !needsScrollScan) {
        showToast(`✨ 全コード (${initialCode.split('\n').length}行) をコピーしました！`);
        return;
      }
    }

    if (!needsScrollScan) {
      if (initialCode) {
        showToast(`✨ 全コード (${initialCode.split('\n').length}行) をコピーしました！`);
      } else {
        showToast("⚠️ コピーに失敗しました");
      }
      return;
    }

    // 2. 仮想スクロールコンテナの自動走査 (長文コード用)
    const origScrollTop = scrollContainer.scrollTop;
    const totalScroll = scrollContainer.scrollHeight;
    const viewportH = scrollContainer.clientHeight;

    showToast("🚀 長文コードを走査中... 全行を収集しています");

    // 最上部へ移動してレンダリング待機
    scrollContainer.scrollTop = 0;
    await new Promise(r => setTimeout(r, 40));
    harvestVisibleLines();

    // ビューポートの60%ずつスクロールして行の取りこぼしを防止
    const step = Math.max(120, Math.floor(viewportH * 0.6));
    let currentTop = 0;
    // 数千行〜1万行の長文コードでも確実に最後まで走査できるよう上限を拡大 (最大500ステップ)
    const estimatedSteps = Math.ceil(totalScroll / step) + 30;
    let maxSteps = Math.min(500, Math.max(150, estimatedSteps));
    let lastScrollTop = -1;

    while (currentTop < totalScroll && maxSteps > 0) {
      maxSteps--;
      currentTop += step;
      scrollContainer.scrollTop = Math.min(currentTop, totalScroll);
      // DOMレンダリング待機 (各スクロール位置で行要素が生成されるのを待つ)
      await new Promise(r => setTimeout(r, 25));
      harvestVisibleLines();

      // 最下部到達判定
      if (scrollContainer.scrollTop === lastScrollTop ||
        scrollContainer.scrollTop + viewportH >= totalScroll - 2) {
        break;
      }
      lastScrollTop = scrollContainer.scrollTop;
    }

    // 最後に念のため最下部を確実に収集
    scrollContainer.scrollTop = scrollContainer.scrollHeight;
    await new Promise(r => setTimeout(r, 40));
    harvestVisibleLines();
    scrollContainer.scrollTop = origScrollTop;

    // 行番号キー順に整列して結合
    let finalCode = '';
    if (lineMap.size > 0) {
      const sortedKeys = Array.from(lineMap.keys()).sort((a, b) => a - b);
      const minKey = sortedKeys[0];
      const maxKey = sortedKeys[sortedKeys.length - 1];
      const assembled = [];
      for (let i = minKey; i <= maxKey; i++) {
        assembled.push(lineMap.has(i) ? lineMap.get(i) : "");
      }
      finalCode = assembled.join('\n');
    }

    if (!finalCode.trim()) {
      finalCode = getFallbackCode() || initialCode;
    }
    finalCode = cleanSelectedText(finalCode, { isProse: false });

    if (finalCode) {
      const ok = await writeClipboard(finalCode);
      if (ok) {
        showToast(`✨ 全コード (${finalCode.split('\n').length}行) をコピーしました！`);
      } else {
        showToast("⚠️ コピーに失敗しました");
      }
    }
  }

  /**
   * コードブロックに対する「行番号なし全コピー」ボタンの自動注入 (1ブロック1ボタン厳守)
   */
  function injectCopyButtons() {
    if (!isCurrentDomainEnabled()) {
      document.querySelectorAll('.ff-clean-copy-injected-btn').forEach(btn => btn.remove());
      return;
    }

    const rawCandidates = document.querySelectorAll(
      '.scriptor-component-code-block, [role="textbox"][aria-label*="コード"], [role="textbox"][aria-label*="code" i], .scriptor-codeblock-virtualized, div[data-content="code"], pre, .code-block, [class*="codeblock" i], [class*="code-block" i]'
    );
    if (rawCandidates.length === 0) return;

    // 各要素の最上位ルートコンテナを集約（重複排除）
    const rootSet = new Set();
    rawCandidates.forEach((el) => {
      if (!el.isConnected) return;
      const root = findCodeBlockRoot(el);
      if (root && root.isConnected) {
        rootSet.add(root);
      }
    });

    // 入れ子関係の排除（親ブロックが存在する場合、その内部の子要素は除外）
    const distinctRoots = Array.from(rootSet).filter((root) => {
      for (const other of rootSet) {
        if (other !== root && other.contains(root)) {
          return false;
        }
      }
      return true;
    });

    // 各最上位ルートに対して 1 つだけボタンを保証配置
    distinctRoots.forEach((block) => {
      const existingBtns = block.querySelectorAll('.ff-clean-copy-injected-btn');
      if (existingBtns.length > 0) {
        // 重複した余分なボタンのみ削除
        for (let i = 1; i < existingBtns.length; i++) {
          existingBtns[i].remove();
        }
        return;
      }

      // ツールバー要素の探索
      const toolbar = block.querySelector(
        '.fui-Overflow, [role="toolbar"], .codeblock-toolbar, [class*="toolbar" i], ._803b581d5ee80236, .a3d13fac2210ea8e'
      );

      function setBtnState(btn, icon, text) {
        const iconSpan = document.createElement('span');
        iconSpan.style.fontSize = '12px';
        iconSpan.style.marginRight = '4px';
        iconSpan.textContent = icon;
        const textSpan = document.createElement('span');
        textSpan.textContent = text;
        btn.replaceChildren(iconSpan, textSpan);
      }

      const cleanBtn = document.createElement('button');
      cleanBtn.type = 'button';
      cleanBtn.className = 'ff-clean-copy-injected-btn';
      cleanBtn.setAttribute('data-ff-clean-copy-ui', 'true');
      cleanBtn.setAttribute('aria-hidden', 'true');
      cleanBtn.title = '行番号をすべて除外してコード全体をコピー';
      setBtnState(cleanBtn, '📋', '行番号なし全コピー');

      cleanBtn.addEventListener('mousedown', (e) => {
        e.stopPropagation();
      });

      cleanBtn.addEventListener('click', async (e) => {
        e.preventDefault();
        e.stopPropagation();

        cleanBtn.disabled = true;
        setBtnState(cleanBtn, '⏳', 'コピー中...');

        try {
          await copyCodeBlock(block);
          setBtnState(cleanBtn, '✅', 'コピー完了！');
          setTimeout(() => {
            setBtnState(cleanBtn, '📋', '行番号なし全コピー');
            cleanBtn.disabled = false;
          }, 2000);
        } catch (err) {
          setBtnState(cleanBtn, '📋', '行番号なし全コピー');
          cleanBtn.disabled = false;
        }
      });

      if (toolbar && toolbar !== block && block.contains(toolbar)) {
        toolbar.prepend(cleanBtn);
      } else {
        const compStyle = window.getComputedStyle(block);
        if (compStyle.position === 'static') {
          block.style.position = 'relative';
        }
        cleanBtn.classList.add('ff-clean-copy-absolute');
        block.appendChild(cleanBtn);
      }
    });
  }

  // 動的ロード対応の定期注入監視
  setInterval(injectCopyButtons, 1000);

  /* ==========================================================================
     6. UIコンポーネント (Floating HUD / Toast)
     ========================================================================== */

  function createHudSpan(cls, text) {
    const span = document.createElement("span");
    span.className = cls;
    span.textContent = text;
    return span;
  }

  function createHudBtn(id, title, icon, label, extraClass = "") {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = extraClass ? `ff-hud-btn ${extraClass}` : "ff-hud-btn";
    btn.id = id;
    btn.title = title;
    btn.appendChild(createHudSpan("ff-icon", icon));
    btn.appendChild(createHudSpan("ff-label", label));
    return btn;
  }

  /**
   * UIの初期化
   */
  function initUI() {
    if (!document.body) return;

    // フローティングHUDボタン
    floatingBtn = document.createElement("div");
    floatingBtn.id = "ff-clean-copy-floating-hud";
    floatingBtn.className = "ff-clean-copy-hidden";
    floatingBtn.setAttribute("data-ff-clean-copy-ui", "true");
    floatingBtn.setAttribute("aria-hidden", "true");
    floatingBtn.replaceChildren(
      createHudSpan("ff-icon", "📋"),
      createHudSpan("ff-label", "行番号なしコピー")
    );
    document.body.appendChild(floatingBtn);

    floatingBtn.addEventListener("mousedown", (e) => {
      e.preventDefault();
      e.stopPropagation();
    });

    floatingBtn.addEventListener("click", (e) => {
      e.preventDefault();
      e.stopPropagation();
      executeCleanCopy();
      hideFloatingButton();
    });

    // トースト通知要素
    toastElem = document.createElement("div");
    toastElem.id = "ff-clean-copy-toast";
    toastElem.className = "ff-clean-copy-hidden";
    toastElem.setAttribute("data-ff-clean-copy-ui", "true");
    toastElem.setAttribute("aria-hidden", "true");
    document.body.appendChild(toastElem);
  }

  /**
   * トースト通知の表示
   */
  function showToast(msg) {
    if (!config.showNotificationToast) return;
    if (!toastElem) initUI();
    if (!toastElem) return;

    toastElem.textContent = msg;
    toastElem.className = "ff-clean-copy-toast-visible";

    clearTimeout(toastElem._timer);
    toastElem._timer = setTimeout(() => {
      toastElem.className = "ff-clean-copy-hidden";
    }, 2400);
  }

  /**
   * 選択範囲の位置に応じたフローティングHUDボタンの表示
   */
  function updateFloatingButton() {
    if (!config.showFloatingButton) return;
    const selection = window.getSelection();
    if (!selection || selection.isCollapsed) {
      hideFloatingButton();
      return;
    }

    const text = selection.toString().trim();
    if (text.length < 2) {
      hideFloatingButton();
      return;
    }

    try {
      const range = selection.getRangeAt(0);
      const container = range.commonAncestorContainer.nodeType === 1
        ? range.commonAncestorContainer
        : range.commonAncestorContainer.parentElement;

      const analysis = analyzeTextSelection(text, container);
      const virtualizedBlock = container?.closest?.(
        '.scriptor-component-code-block, [data-virtualized-code-find-root], .scriptor-codeblock-virtualized'
      );

      if (!floatingBtn) initUI();
      if (!floatingBtn) return;

      if (analysis.isProse) {
        const proseBtn = createHudBtn("ff-hud-copy-prose", "地の文をそのままコピー", "📋", "地の文をコピー");
        floatingBtn.replaceChildren(proseBtn);
      } else if (virtualizedBlock) {
        const copySelectionBtn = createHudBtn("ff-hud-copy-selection", "選択した範囲を行番号なしでコピー", "📋", "選択範囲をコピー");
        const divider = document.createElement("span");
        divider.className = "ff-hud-divider";
        const scanAllBtn = createHudBtn("ff-hud-scan-all", "プログラム表示領域内をスクロール走査して全体をコピー", "📜", "全体を走査コピー", "ff-hud-btn-highlight");
        floatingBtn.replaceChildren(copySelectionBtn, divider, scanAllBtn);
      } else {
        const cleanCopyBtn = createHudBtn("ff-hud-copy-clean", "行番号を除去してコピー", "📋", "行番号なしコピー");
        floatingBtn.replaceChildren(cleanCopyBtn);
      }

      // ボタンクリックイベントの登録
      const copyProseBtn = floatingBtn.querySelector('#ff-hud-copy-prose');
      if (copyProseBtn) {
        copyProseBtn.onmousedown = (e) => { e.preventDefault(); e.stopPropagation(); };
        copyProseBtn.onclick = (e) => {
          e.preventDefault(); e.stopPropagation();
          executeCleanCopy({ isProse: true });
          hideFloatingButton();
        };
      }

      const copyCleanBtn = floatingBtn.querySelector('#ff-hud-copy-clean');
      if (copyCleanBtn) {
        copyCleanBtn.onmousedown = (e) => { e.preventDefault(); e.stopPropagation(); };
        copyCleanBtn.onclick = (e) => {
          e.preventDefault(); e.stopPropagation();
          executeCleanCopy({ isProse: false });
          hideFloatingButton();
        };
      }

      const copySelectionBtn = floatingBtn.querySelector('#ff-hud-copy-selection');
      if (copySelectionBtn) {
        copySelectionBtn.onmousedown = (e) => { e.preventDefault(); e.stopPropagation(); };
        copySelectionBtn.onclick = (e) => {
          e.preventDefault(); e.stopPropagation();
          executeCleanCopy({ isProse: false });
          hideFloatingButton();
        };
      }

      const scanAllBtn = floatingBtn.querySelector('#ff-hud-scan-all');
      if (scanAllBtn) {
        scanAllBtn.onmousedown = (e) => { e.preventDefault(); e.stopPropagation(); };
        scanAllBtn.onclick = (e) => {
          e.preventDefault(); e.stopPropagation();
          copyCodeBlock(virtualizedBlock);
          hideFloatingButton();
        };
      }

      const rect = range.getBoundingClientRect();
      if (!rect || (rect.width === 0 && rect.height === 0)) {
        hideFloatingButton();
        return;
      }

      const top = window.scrollY + rect.top - 42;
      const left = window.scrollX + rect.left + (rect.width / 2) - 80;

      floatingBtn.style.top = `${Math.max(10, top)}px`;
      floatingBtn.style.left = `${Math.max(10, left)}px`;
      floatingBtn.className = "ff-clean-copy-floating-visible";
    } catch (e) {
      hideFloatingButton();
    }
  }

  function hideFloatingButton() {
    if (floatingBtn) {
      floatingBtn.className = "ff-clean-copy-hidden";
    }
  }

  /* ==========================================================================
     7. イベントインターセプター (copy / keydown / contextmenu)
     ========================================================================== */

  /**
   * DOMから選択テキストを抽出（不要UI要素やボタンを事前排除）
   */
  function getSelectedTextWithDOMCleanup() {
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0) return "";

    try {
      const range = selection.getRangeAt(0);
      const container = range.commonAncestorContainer.nodeType === 1
        ? range.commonAncestorContainer
        : range.commonAncestorContainer.parentElement;

      // 1. 仮想コードブロックの個別行判定
      const codeBlockEl = container?.closest?.(
        '.scriptor-component-code-block, [role="textbox"][aria-label*="コード"], [role="textbox"][aria-label*="code" i]'
      ) || (container?.matches?.('.scriptor-component-code-block') ? container : null);

      if (codeBlockEl) {
        const lineElements = Array.from(codeBlockEl.querySelectorAll('[data-line-index]'));
        if (lineElements.length > 0) {
          const selectedLineTexts = [];
          for (const lineEl of lineElements) {
            if (selection.containsNode(lineEl, true)) {
              selectedLineTexts.push(lineEl.textContent.replace(/\u00A0/g, ' '));
            }
          }
          if (selectedLineTexts.length > 0) {
            return selectedLineTexts.join('\n');
          }
        }
      }

      // 2. 選択範囲全体のサニタイズ（注入ボタンやUIゴミの除去）
      const fragment = range.cloneContents();
      const unwanted = fragment.querySelectorAll(
        '.ff-clean-copy-injected-btn, #ff-clean-copy-floating-hud, #ff-clean-copy-toast, [data-ff-clean-copy-ui], .scriptor-clean-copy-btn, .blob-num, .line-numbers-rows, .prism-line-numbers, .diff-line-num, ._4272b89f6ce67708'
      );
      unwanted.forEach(el => el.remove());

      const div = document.createElement('div');
      div.appendChild(fragment);
      const cleanedDOMText = div.innerText || div.textContent;
      if (cleanedDOMText && cleanedDOMText.trim()) {
        return cleanedDOMText;
      }
    } catch (e) { }

    return selection.toString();
  }

  /**
   * クリーンコピーの実行 (地の文・コード・チャット全体選択の全対応)
   */
  async function executeCleanCopy(opts = {}) {
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0) return;

    const range = selection.getRangeAt(0);
    const container = range.commonAncestorContainer.nodeType === 1
      ? range.commonAncestorContainer
      : range.commonAncestorContainer.parentElement;

    const rawText = getSelectedTextWithDOMCleanup() || selection.toString();
    if (!rawText || !rawText.trim()) return;

    const analysis = analyzeTextSelection(rawText, container);
    const isProse = opts.isProse !== undefined ? opts.isProse : analysis.isProse;

    if (isProse) {
      const cleanProse = stripExtraneousUIGarbage(rawText.replace(/\u00A0/g, ' '));
      const ok = await writeClipboard(cleanProse);
      if (ok) {
        showToast(`✨ 全体クリーンコピー完了！UIゴミを除去しました (${cleanProse.length}文字)`);
      } else {
        showToast("⚠️ コピーに失敗しました");
      }
      return;
    }

    const cleaned = cleanSelectedText(rawText, { ...opts, isProse: false });
    if (cleaned) {
      const ok = await writeClipboard(cleaned);
      if (ok) {
        showToast(`✨ コピー完了！行番号を削除しました (${cleaned.split('\n').length}行)`);
      } else {
        showToast("⚠️ コピーに失敗しました");
      }
    }
  }

  // 選択範囲変更リスナー
  document.addEventListener("selectionchange", () => {
    if (!isCurrentDomainEnabled()) {
      hideFloatingButton();
      return;
    }
    const sel = window.getSelection();
    if (sel && !sel.isCollapsed && sel.rangeCount > 0 && sel.toString().trim().length > 0) {
      savedSelectionRange = sel.getRangeAt(0).cloneRange();
      savedSelectionText = sel.toString();
    }
    clearTimeout(window._ffSelectionTimer);
    window._ffSelectionTimer = setTimeout(updateFloatingButton, 200);
  });

  // 右クリック時の選択範囲保護
  function handleRightClickGuard(e) {
    if (!isCurrentDomainEnabled()) return;
    if (e.button === 2) {
      const sel = window.getSelection();
      if ((sel && !sel.isCollapsed && sel.toString().trim().length > 0) || savedSelectionRange) {
        e.stopPropagation();
        e.stopImmediatePropagation();
        if (!sel || sel.isCollapsed || sel.toString().trim().length === 0) {
          if (savedSelectionRange) {
            try {
              sel.removeAllRanges();
              sel.addRange(savedSelectionRange);
            } catch (err) { }
          }
        }
      }
    }
  }

  window.addEventListener("pointerdown", handleRightClickGuard, true);
  window.addEventListener("mousedown", (e) => {
    if (!isCurrentDomainEnabled()) return;
    if (e.button === 2) {
      handleRightClickGuard(e);
      return;
    }
    if (floatingBtn && floatingBtn.contains(e.target)) return;
    hideFloatingButton();
  }, true);

  window.addEventListener("contextmenu", (e) => {
    if (!isCurrentDomainEnabled()) return;
    const sel = window.getSelection();
    if (savedSelectionRange && (!sel || sel.isCollapsed)) {
      try {
        sel.removeAllRanges();
        sel.addRange(savedSelectionRange);
      } catch (err) { }
    }
    const currentSel = window.getSelection();
    if (currentSel && !currentSel.isCollapsed && currentSel.toString().trim().length > 0) {
      e.stopImmediatePropagation();
    }
  }, true);

  // ネイティブ copy イベントのキャプチャ相インターセプト (100% 確実な同期待機書き込み)
  document.addEventListener("copy", (e) => {
    if (!isCurrentDomainEnabled()) return;
    const selection = window.getSelection();
    if (!selection || selection.isCollapsed) return;

    const rawText = getSelectedTextWithDOMCleanup() || selection.toString();
    if (!rawText || !rawText.trim()) return;

    lastCopyEventTimestamp = Date.now();

    const container = selection.rangeCount > 0
      ? (selection.getRangeAt(0).commonAncestorContainer.nodeType === 1
        ? selection.getRangeAt(0).commonAncestorContainer
        : selection.getRangeAt(0).commonAncestorContainer.parentElement)
      : null;

    const analysis = analyzeTextSelection(rawText, container);

    let textToCopy = "";
    if (analysis.isProse) {
      textToCopy = stripExtraneousUIGarbage(rawText.replace(/\u00A0/g, ' '));
    } else {
      textToCopy = cleanSelectedText(rawText, { isProse: false });
    }

    if (textToCopy && e.clipboardData) {
      e.preventDefault();
      e.stopImmediatePropagation();
      e.clipboardData.setData('text/plain', textToCopy);
      if (analysis.isProse) {
        showToast(`✨ 全体クリーンコピー完了！UIゴミを除去しました (${textToCopy.length}文字)`);
      } else {
        showToast(`✨ コピー完了！行番号を自動削除しました (${textToCopy.split('\n').length}行)`);
      }
    }
  }, true);

  // Ctrl+C / Cmd+C / Alt+C のキャプチャ相インターセプト
  window.addEventListener("keydown", (e) => {
    if (!isCurrentDomainEnabled()) return;
    const isCopyKey = (e.key === 'c' || e.key === 'C') && (e.ctrlKey || e.metaKey);
    const isAltC = (e.key === 'c' || e.key === 'C') && e.altKey;

    if (isAltC) {
      const selection = window.getSelection();
      if (selection && !selection.isCollapsed && selection.toString().trim().length > 0) {
        e.preventDefault();
        e.stopImmediatePropagation();
        executeCleanCopy();
      }
      return;
    }

    if (isCopyKey) {
      const selection = window.getSelection();
      if (selection && !selection.isCollapsed && selection.toString().trim().length > 0) {
        // 通常のCtrl+Cはネイティブcopyイベントに任せ、サイト側がcopyをブロックした場合のみフォールバック
        setTimeout(() => {
          if (Date.now() - lastCopyEventTimestamp > 80) {
            executeCleanCopy();
          }
        }, 50);
      }
    }
  }, true);

  /* ==========================================================================
     8. 通信リスナー & ライフサイクル初期化
     ========================================================================== */

  // Background および Popup からの通信リスナー
  extApi.runtime.onMessage.addListener((request, sender, sendResponse) => {
    if (request.action === "get_hostname") {
      sendResponse({ hostname: window.location.hostname });
      return true;
    }

    if (request.action === "domain_status_changed") {
      if (Array.isArray(request.enabledDomains)) {
        config.enabledDomains = request.enabledDomains;
      }
      const enabled = applyDomainStatus();
      sendResponse({ status: "success", enabled });
      return true;
    }

    if (!isCurrentDomainEnabled()) {
      sendResponse({ status: "disabled" });
      return true;
    }

    if (request.action === "clean_and_copy") {
      executeCleanCopy();
      sendResponse({ status: "success" });
    }
    return true;
  });

  // Background スクリプトとの Keep-Alive ポート維持
  function initKeepAlive() {
    try {
      if (!extApi.runtime || !extApi.runtime.connect) return;
      keepAlivePort = extApi.runtime.connect({ name: "clean-copy-keepalive" });
      keepAlivePort.onDisconnect.addListener(() => {
        keepAlivePort = null;
        setTimeout(initKeepAlive, 5000);
      });
      keepAlivePort.onMessage.addListener(() => { });
    } catch (e) { }
  }

  setInterval(() => {
    try {
      if (keepAlivePort) {
        keepAlivePort.postMessage({ type: "ping", timestamp: Date.now() });
      } else {
        initKeepAlive();
      }
    } catch (e) {
      initKeepAlive();
    }
  }, 15000);

  initKeepAlive();

  // DOM 構築完了時の初期化
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initUI);
  } else {
    initUI();
  }
})();
