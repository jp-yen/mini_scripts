/**
 * Firefox Selection Clean Copy - Content Script
 * 
 * Features:
 * 1. Bypasses sites that block Ctrl+C by using capture-phase event interception
 * 2. Provides an unobtrusive floating HUD copy button on selection
 * 3. Intelligently strips line numbers (1 |, 1:, 1  , [1], 01.) and prompt markers ($ > #)
 * 4. Preserves code indentation and formatting
 * 5. Visual toast feedback on successful copy
 */

(function() {
  // Prevent double injection
  if (window.__ffSelectionCleanCopyInjected) return;
  window.__ffSelectionCleanCopyInjected = true;

  const extApi = typeof browser !== 'undefined' ? browser : chrome;

  // Settings
  let config = {
    removeLineNumbers: true,
    removePromptPrefixes: true,
    preserveIndentation: true,
    showFloatingButton: true,
    interceptCtrlC: true,
    showNotificationToast: true
  };

  // Load saved settings
  if (extApi.storage && extApi.storage.sync) {
    extApi.storage.sync.get(config, (saved) => {
      if (saved) config = { ...config, ...saved };
    });
  }

  // Floating HUD button element
  let floatingBtn = null;
  let toastElem = null;

  function initUI() {
    if (!document.body) return;

    // Create floating copy button
    floatingBtn = document.createElement("div");
    floatingBtn.id = "ff-clean-copy-floating-hud";
    floatingBtn.className = "ff-clean-copy-hidden";
    floatingBtn.setAttribute("data-ff-clean-copy-ui", "true");
    floatingBtn.setAttribute("aria-hidden", "true");
    floatingBtn.innerHTML = `
      <span class="ff-icon">📋</span>
      <span class="ff-label">行番号なしコピー</span>
    `;
    document.body.appendChild(floatingBtn);

    // Floating button click event
    floatingBtn.addEventListener("mousedown", (e) => {
      e.preventDefault();
      e.stopPropagation();
    });

    floatingBtn.addEventListener("click", (e) => {
      e.preventDefault();
      e.stopPropagation();
      executeCleanCopy({ stripPrompts: config.removePromptPrefixes });
      hideFloatingButton();
    });

    // Create toast notification element
    toastElem = document.createElement("div");
    toastElem.id = "ff-clean-copy-toast";
    toastElem.className = "ff-clean-copy-hidden";
    toastElem.setAttribute("data-ff-clean-copy-ui", "true");
    toastElem.setAttribute("aria-hidden", "true");
    document.body.appendChild(toastElem);
  }

  // Core Text Analysis & Cleaning Engine
  
  // Check if an element is inside a code block, editor, or preformatted area
  function isInsideCodeBlock(el) {
    if (!el) return false;
    const codeBlockSelectors = [
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
    return !!el.closest?.(codeBlockSelectors.join(', '));
  }

  // Find the scrollable container inside a virtualized code block
  function findScrollableElement(block) {
    if (!block) return null;

    // Candidates in Copilot & Fluent UI virtualized blocks
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

    // Check children with overflow-y: auto / scroll
    const allElements = block.querySelectorAll('*');
    for (const el of allElements) {
      const style = window.getComputedStyle(el);
      if ((style.overflowY === 'auto' || style.overflowY === 'scroll') && el.scrollHeight > el.clientHeight + 15) {
        return el;
      }
    }

    return null;
  }

  // Classify selected text into Prose (地の文) vs Code with line numbers
  function analyzeTextSelection(rawText, containerEl) {
    if (!rawText || !rawText.trim()) {
      return { isProse: true, isCode: false, isCopilotVirtualized: false, hasLineNumbers: false };
    }

    const insideCode = isInsideCodeBlock(containerEl);
    const normalized = rawText.replace(/\u00A0/g, ' ');
    const lines = normalized.split(/\r?\n/).map(l => l.trim()).filter(Boolean);

    // 1. Check for Copilot interleaved lone number lines (e.g. "import os\n2\nfrom playwright...\n3")
    const loneNumRegex = /^\s*\d+\s*$/;
    const loneNums = lines.filter(l => loneNumRegex.test(l));
    const isCopilotInterleaved = (loneNums.length >= 2) || (loneNums.length >= 1 && lines.length <= 4 && insideCode);

    // 2. Check for gutter line numbers (" 1 | code", "1: code", "[1] code")
    const gutterNums = lines.filter(l => /^\d+\s*[\:\|]\s+/.test(l) || /^\[\d+\]\s*/.test(l));
    const hasGutterNums = gutterNums.length >= 2 || (gutterNums.length >= 1 && insideCode);

    // 3. Check for space-prefixed line numbers ("  1   const a = 10;")
    const spacePrefixed = lines.filter(l => /^\d{1,4}\s{2,}\S/.test(l));
    const hasSpaceNums = spacePrefixed.length >= 2 && insideCode;

    const hasLineNumbers = isCopilotInterleaved || hasGutterNums || hasSpaceNums;
    const isCode = insideCode || hasLineNumbers;

    const virtualizedBlock = containerEl?.closest?.(
      '.scriptor-component-code-block, [data-virtualized-code-find-root], .scriptor-codeblock-virtualized'
    );

    return {
      isProse: !isCode,
      isCode,
      isCopilotVirtualized: !!virtualizedBlock,
      virtualizedBlock: virtualizedBlock || null,
      hasLineNumbers
    };
  }

  // Comprehensive UI Artifact & Button Cleaner
  // Automatically removes injected extension buttons, Copilot UI toolbars, language badges, and "その他の行を表示する"
  function stripExtraneousUIGarbage(rawText) {
    if (!rawText) return "";

    const lines = rawText.split(/\r?\n/);
    const cleanedLines = [];

    // Common programming language badges in Copilot toolbars (case-insensitive)
    const codeLanguageBadges = new Set([
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

    for (let i = 0; i < lines.length; i++) {
      const line = lines[i];
      const trimmed = line.trim();

      // 1. Injected extension buttons and HUD text
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

      // 2. Copilot / Web UI action buttons & footer links
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

      // 3. Isolated code block language badges (e.g. "Plain Text", "Python")
      if (trimmed.length >= 2 && codeLanguageBadges.has(trimmed.toLowerCase())) {
        continue;
      }

      cleanedLines.push(line);
    }

    // Collapse multiple consecutive blank lines down to at most 1 blank line
    const collapsed = [];
    let consecutiveBlanks = 0;
    for (const l of cleanedLines) {
      if (l.trim() === '') {
        consecutiveBlanks++;
        if (consecutiveBlanks <= 1) {
          collapsed.push('');
        }
      } else {
        consecutiveBlanks = 0;
        collapsed.push(l);
      }
    }

    // Trim outer blank lines
    let s = 0;
    while (s < collapsed.length && collapsed[s].trim() === '') s++;
    let e = collapsed.length - 1;
    while (e >= s && collapsed[e].trim() === '') e--;
    if (s > e) return "";
    return collapsed.slice(s, e + 1).join('\n');
  }

  /**
   * Main text cleaning engine
   * Handles:
   * 1. UI Garbage Stripping (buttons, language badges, "その他の行を表示する")
   * 2. Plain text (地の文): preserves numbered lists (1. , 2. ) and Japanese/English formatting untouched!
   * 3. Copilot interleaved line numbers: lone number lines (2, 3...)
   * 4. Traditional prefixed line numbers: (1 |, 1:, 1  , [1])
   * 5. Terminal prompts ($ > #)
   */
  function cleanSelectedText(rawText, options = {}) {
    if (!rawText) return "";

    // Normalize non-breaking spaces
    let normalized = rawText.replace(/\u00A0/g, ' ');

    // Always strip extraneous UI garbage first!
    normalized = stripExtraneousUIGarbage(normalized);

    // If explicitly marked as prose (地の文), return cleaned text without stripping numbers!
    if (options.isProse) {
      return normalized;
    }

    const lines = normalized.split(/\r?\n/);
    if (lines.length === 0) return "";

    const stripNumbers = options.removeLineNumbers ?? config.removeLineNumbers;
    const stripPrompts = options.stripPrompts ?? config.removePromptPrefixes;

    // Pattern 1: Interleaved lone digit lines (Copilot virtualized codeblock pattern)
    let loneNumberLineCount = 0;
    let totalNonEmpty = 0;
    const loneNumRegex = /^\s*\d+\s*$/;

    for (let i = 0; i < lines.length; i++) {
      const trimmed = lines[i].trim();
      if (trimmed.length > 0) {
        totalNonEmpty++;
        if (loneNumRegex.test(trimmed)) {
          loneNumberLineCount++;
        }
      }
    }

    const isCopilotInterleaved = (loneNumberLineCount >= 2) || (loneNumberLineCount >= 1 && totalNonEmpty <= 4);
    let processedLines = [];

    if (stripNumbers && isCopilotInterleaved) {
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

    // Pattern 2: Prefixed line numbers and code-specific prefixes
    const cleanedLines = processedLines.map((line) => {
      let l = line;

      if (stripNumbers) {
        l = l.replace(/^\s*\d+[\s:\|\t]+/, '');
        l = l.replace(/^\s*\[\d+\]\s*/, '');
        // Only strip \d+\. if accompanied by code or explicitly cleaning code
        if (!options.isProse) {
          l = l.replace(/^\s*\d+\.\s+/, '');
        }
      }

      if (stripPrompts && !options.isProse) {
        l = l.replace(/^\s*[$%>#]\s+/, '');
      }

      return l;
    });

    // Trim outer blank lines while preserving indentation & internal blank lines
    let start = 0;
    while (start < cleanedLines.length && cleanedLines[start].trim() === '') {
      start++;
    }
    let end = cleanedLines.length - 1;
    while (end >= start && cleanedLines[end].trim() === '') {
      end--;
    }

    if (start > end) return "";
    return cleanedLines.slice(start, end + 1).join('\n');
  }

  // Safe Clipboard Writer with Bulletproof Fallbacks
  async function writeClipboard(text) {
    if (!text) return false;

    // 1. Try modern navigator.clipboard.writeText
    if (typeof navigator !== 'undefined' && navigator.clipboard && navigator.clipboard.writeText) {
      try {
        await navigator.clipboard.writeText(text);
        return true;
      } catch (e) {
        console.warn("[CleanCopy] navigator.clipboard.writeText failed, attempting execCommand fallback:", e);
      }
    }

    // 2. Synchronous execCommand copy with dedicated one-time copy event handler
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
    } catch (e) {
      console.error("[CleanCopy] document.execCommand failed:", e);
    }

    // 3. Fallback to background script privileged clipboard writer if content script is restricted
    try {
      if (extApi.runtime && extApi.runtime.sendMessage) {
        const resp = await new Promise((resolve) => {
          extApi.runtime.sendMessage({ action: "bg_write_clipboard", text }, (res) => {
            resolve(res);
          });
        });
        if (resp && resp.status === "success") {
          return true;
        }
      }
    } catch (e) {
      console.warn("[CleanCopy] background clipboard fallback failed:", e);
    }

    return false;
  }

  // Automated Virtualized Scroll Scanner
  // Seamlessly traverses scrollable Copilot virtualized containers from top to bottom,
  // collects all [data-line-index] rows without missing any, restores original scroll, and copies!
  async function scanAndCopyVirtualizedCode(targetBlock) {
    if (!targetBlock) return;

    // Search upwards or downwards to find the surrounding code-block root
    const block = targetBlock.closest?.(
      '.scriptor-component-code-block, [data-virtualized-code-find-root], .scriptor-codeblock-virtualized, [role="textbox"][aria-label*="コード"], [role="textbox"][aria-label*="code" i], .code-block, pre'
    ) || targetBlock;

    const scrollContainer = findScrollableElement(block) || findScrollableElement(targetBlock);
    const lineMap = new Map();

    const harvestVisibleLines = () => {
      // Look within block, scrollContainer, and targetBlock
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

    // Helper: extract fallback text from block
    const getFallbackCode = () => {
      const codeEl = block.querySelector('code, pre, [role="textbox"]') || block;
      return cleanSelectedText(codeEl.innerText || codeEl.textContent, { isProse: false });
    };

    // If block is short or doesn't need vertical scrolling
    if (!scrollContainer || scrollContainer.scrollHeight <= scrollContainer.clientHeight + 15) {
      harvestVisibleLines();
      let fullCode = '';
      if (lineMap.size > 0) {
        const sorted = Array.from(lineMap.keys()).sort((a, b) => a - b);
        fullCode = sorted.map(k => lineMap.get(k)).join('\n');
      } else {
        fullCode = getFallbackCode();
      }

      if (fullCode) {
        const ok = await writeClipboard(fullCode);
        if (ok) {
          showToast(`✨ 全コード (${fullCode.split('\n').length}行) をコピーしました！`);
        } else {
          showToast("⚠️ コピーに失敗しました");
        }
      }
      return;
    }

    // Long virtualized code block: perform automated scroll scan!
    const origScrollTop = scrollContainer.scrollTop;
    const totalScroll = scrollContainer.scrollHeight;
    const viewportH = scrollContainer.clientHeight;

    showToast("🚀 仮想スクロールを自動走査中... 全コードを収集中");

    // 1. Collect currently visible lines
    harvestVisibleLines();

    // 2. Scroll to top
    scrollContainer.scrollTop = 0;
    await new Promise(r => setTimeout(r, 45));
    harvestVisibleLines();

    // 3. Step through the container downwards
    // 60-70% of viewport height ensures generous overlap so no rows are skipped
    const step = Math.max(80, Math.floor(viewportH * 0.65));
    let currentTop = 0;
    let lastToastCount = 0;

    while (currentTop < totalScroll) {
      currentTop += step;
      scrollContainer.scrollTop = Math.min(currentTop, totalScroll);
      
      // Delay to allow React / Fluent UI to mount the newly visible virtual DOM rows
      await new Promise(r => setTimeout(r, 40));
      harvestVisibleLines();

      if (lineMap.size - lastToastCount >= 15) {
        lastToastCount = lineMap.size;
        showToast(`🚀 スクロール走査中... 現在 ${lineMap.size} 行を取得`);
      }

      if (scrollContainer.scrollTop + viewportH >= totalScroll - 4) {
        break;
      }
    }

    // 4. Capture bottom-most lines
    scrollContainer.scrollTop = totalScroll;
    await new Promise(r => setTimeout(r, 45));
    harvestVisibleLines();

    // 5. Restore user's original scroll position
    scrollContainer.scrollTop = origScrollTop;

    // 6. Assemble lines in sequential order
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
      finalCode = getFallbackCode();
    }

    if (finalCode) {
      const ok = await writeClipboard(finalCode);
      if (ok) {
        showToast(`✨ スクロール走査完了！全 ${finalCode.split('\n').length} 行を行番号なしでコピーしました`);
      } else {
        showToast("⚠️ コピーに失敗しました");
      }
    }
  }

  // DOM Selection Helper: handles Copilot Fluent UI & standard DOM structures directly
  function getSelectedTextWithDOMCleanup() {
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0) return "";

    try {
      const range = selection.getRangeAt(0);
      const container = range.commonAncestorContainer.nodeType === 1
        ? range.commonAncestorContainer
        : range.commonAncestorContainer.parentElement;

      // 1. Single Copilot virtualized code block check
      const copilotBlock = container?.closest?.('.scriptor-component-code-block, [role="textbox"][aria-label*="コード"], [role="textbox"][aria-label*="code" i]') || (container?.matches?.('.scriptor-component-code-block') ? container : null);

      if (copilotBlock) {
        const lineElements = Array.from(copilotBlock.querySelectorAll('[data-line-index]'));
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

      // 2. DOM Sanitization for multi-block, mixed, or whole-chat selections:
      // Clone range and purge all injected buttons, HUDs, Copilot action toolbars, and line-number columns before converting to text!
      const fragment = range.cloneContents();
      const unwanted = fragment.querySelectorAll(
        '.ff-copilot-clean-copy-btn, #ff-clean-copy-floating-hud, #ff-clean-copy-toast, [data-ff-clean-copy-ui], .scriptor-clean-copy-btn, .blob-num, .line-numbers-rows, .prism-line-numbers, .diff-line-num, ._4272b89f6ce67708'
      );
      unwanted.forEach(el => el.remove());

      const div = document.createElement('div');
      div.appendChild(fragment);
      const cleanedDOMText = div.innerText || div.textContent;
      if (cleanedDOMText && cleanedDOMText.trim()) {
        return cleanedDOMText;
      }
    } catch (e) {
      console.warn("[CleanCopy] DOM inspection fallback", e);
    }

    return selection.toString();
  }

  // Execute Clean Copy & write to clipboard (handles prose, code, and whole-chat mixed selections)
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
      // Plain text / prose (地の文) or Whole Chat selection:
      // Strip UI garbage (extension buttons, Plain Text language badges, その他の行を表示する, etc.)
      // while keeping list numbering (1. 2.) and genuine prose 100% intact!
      const cleanProse = stripExtraneousUIGarbage(rawText.replace(/\u00A0/g, ' '));
      const ok = await writeClipboard(cleanProse);
      if (ok) {
        showToast(`✨ 全体クリーンコピー完了！UIゴミを除去しました (${cleanProse.length}文字)`);
      } else {
        showToast("⚠️ コピーに失敗しました");
      }
      return;
    }

    // Code: clean line numbers, prompts, and UI garbage
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

  // Show Toast notification
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

  // Show floating HUD button near cursor or selection
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

      // Render contextual buttons inside HUD
      if (analysis.isProse) {
        // Selection is prose (地の文)
        floatingBtn.innerHTML = `
          <button type="button" class="ff-hud-btn" id="ff-hud-copy-prose" title="地の文をそのままコピー">
            <span class="ff-icon">📋</span>
            <span class="ff-label">地の文をコピー</span>
          </button>
        `;
      } else if (virtualizedBlock) {
        // Selection is inside Copilot virtualized code block
        floatingBtn.innerHTML = `
          <button type="button" class="ff-hud-btn" id="ff-hud-copy-selection" title="選択した範囲を行番号なしでコピー">
            <span class="ff-icon">📋</span>
            <span class="ff-label">選択範囲をコピー</span>
          </button>
          <span class="ff-hud-divider"></span>
          <button type="button" class="ff-hud-btn ff-hud-btn-highlight" id="ff-hud-scan-all" title="プログラム表示領域内をスクロール走査して全体をコピー">
            <span class="ff-icon">📜</span>
            <span class="ff-label">全体を走査コピー</span>
          </button>
        `;
      } else {
        // Selection is inside standard code block
        floatingBtn.innerHTML = `
          <button type="button" class="ff-hud-btn" id="ff-hud-copy-clean" title="行番号を除去してコピー">
            <span class="ff-icon">📋</span>
            <span class="ff-label">行番号なしコピー</span>
          </button>
        `;
      }

      // Attach button event listeners with mousedown protection to preserve selection
      const copyProseBtn = floatingBtn.querySelector('#ff-hud-copy-prose');
      if (copyProseBtn) {
        copyProseBtn.onmousedown = (e) => { e.preventDefault(); e.stopPropagation(); };
        copyProseBtn.onclick = (e) => {
          e.preventDefault();
          e.stopPropagation();
          executeCleanCopy({ isProse: true });
          hideFloatingButton();
        };
      }

      const copyCleanBtn = floatingBtn.querySelector('#ff-hud-copy-clean');
      if (copyCleanBtn) {
        copyCleanBtn.onmousedown = (e) => { e.preventDefault(); e.stopPropagation(); };
        copyCleanBtn.onclick = (e) => {
          e.preventDefault();
          e.stopPropagation();
          executeCleanCopy({ isProse: false, stripPrompts: config.removePromptPrefixes });
          hideFloatingButton();
        };
      }

      const copySelectionBtn = floatingBtn.querySelector('#ff-hud-copy-selection');
      if (copySelectionBtn) {
        copySelectionBtn.onmousedown = (e) => { e.preventDefault(); e.stopPropagation(); };
        copySelectionBtn.onclick = (e) => {
          e.preventDefault();
          e.stopPropagation();
          executeCleanCopy({ isProse: false, stripPrompts: config.removePromptPrefixes });
          hideFloatingButton();
        };
      }

      const scanAllBtn = floatingBtn.querySelector('#ff-hud-scan-all');
      if (scanAllBtn) {
        scanAllBtn.onmousedown = (e) => { e.preventDefault(); e.stopPropagation(); };
        scanAllBtn.onclick = (e) => {
          e.preventDefault();
          e.stopPropagation();
          scanAndCopyVirtualizedCode(virtualizedBlock);
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

  // Event Listeners
  document.addEventListener("selectionchange", () => {
    const sel = window.getSelection();
    if (sel && !sel.isCollapsed && sel.rangeCount > 0 && sel.toString().trim().length > 0) {
      savedSelectionRange = sel.getRangeAt(0).cloneRange();
      savedSelectionText = sel.toString();
    }
    clearTimeout(window._ffSelectionTimer);
    window._ffSelectionTimer = setTimeout(updateFloatingButton, 200);
  });

  // Keep track of last valid non-empty selection for restoration
  let savedSelectionRange = null;
  let savedSelectionText = "";

  // Guard right-click:
  // When right-clicking on selected text, prevent sites (e.g. Copilot) from deselecting text on pointerdown/mousedown/contextmenu!
  function handleRightClickGuard(e) {
    if (e.button === 2) {
      const sel = window.getSelection();
      if ((sel && !sel.isCollapsed && sel.toString().trim().length > 0) || savedSelectionRange) {
        // Stop the website's custom script from handling the right-click and deselecting
        e.stopPropagation();
        e.stopImmediatePropagation();
        
        // If selection was already collapsed, restore it immediately
        if (!sel || sel.isCollapsed || sel.toString().trim().length === 0) {
          if (savedSelectionRange) {
            try {
              sel.removeAllRanges();
              sel.addRange(savedSelectionRange);
            } catch (err) {}
          }
        }
      }
    }
  }

  // Intercept mouse/pointer down on capture phase before site scripts run
  window.addEventListener("pointerdown", handleRightClickGuard, true);
  window.addEventListener("mousedown", (e) => {
    if (e.button === 2) {
      handleRightClickGuard(e);
      return;
    }
    if (floatingBtn && floatingBtn.contains(e.target)) return;
    hideFloatingButton();
  }, true);

  // Unblock and preserve selection on contextmenu at CAPTURE PHASE!
  window.addEventListener("contextmenu", (e) => {
    const sel = window.getSelection();
    if (savedSelectionRange && (!sel || sel.isCollapsed)) {
      try {
        sel.removeAllRanges();
        sel.addRange(savedSelectionRange);
      } catch (err) {}
    }

    const currentSel = window.getSelection();
    if (currentSel && !currentSel.isCollapsed && currentSel.toString().trim().length > 0) {
      // Allow browser native contextmenu while preventing site's clearSelection scripts
      e.stopImmediatePropagation();
    }
  }, true);

  // Global Copy Event Interceptor (Capture Phase)
  // Ensures copying ALWAYS succeeds whether the page blocks it or not:
  // - Prose (地の文) & Whole-chat: guaranteed copy with 100% untouched formatting and numbered lists, removing UI trash
  // - Code (プログラム表示領域): guaranteed copy with line numbers and prompt prefixes removed
  document.addEventListener("copy", (e) => {
    const selection = window.getSelection();
    if (!selection || selection.isCollapsed) return;

    const rawText = getSelectedTextWithDOMCleanup() || selection.toString();
    if (!rawText || !rawText.trim()) return;

    const container = selection.rangeCount > 0
      ? (selection.getRangeAt(0).commonAncestorContainer.nodeType === 1
          ? selection.getRangeAt(0).commonAncestorContainer
          : selection.getRangeAt(0).commonAncestorContainer.parentElement)
      : null;

    const analysis = analyzeTextSelection(rawText, container);

    let textToCopy = "";
    if (analysis.isProse) {
      // 地の文 or チャット全体選択: 箇条書き番号(1. 2.)は保持しつつ、UIゴミ(ボタン・言語名・その他の行を表示する)を自動除去
      textToCopy = stripExtraneousUIGarbage(rawText.replace(/\u00A0/g, ' '));
    } else {
      // プログラム表示領域: 行番号・プロンプト・UIゴミを除去
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

  // Intercept Ctrl+C / Cmd+C at CAPTURE PHASE!
  // Solves environments (like Copilot Web or code editors) where Ctrl+C is disabled or canceled
  window.addEventListener("keydown", (e) => {
    const isCopyKey = (e.key === 'c' || e.key === 'C') && (e.ctrlKey || e.metaKey);
    const isAltC = (e.key === 'c' || e.key === 'C') && e.altKey;

    if (isCopyKey || isAltC) {
      const selection = window.getSelection();
      if (selection && !selection.isCollapsed && selection.toString().trim().length > 0) {
        const raw = getSelectedTextWithDOMCleanup() || selection.toString();
        const container = selection.rangeCount > 0
          ? (selection.getRangeAt(0).commonAncestorContainer.nodeType === 1
              ? selection.getRangeAt(0).commonAncestorContainer
              : selection.getRangeAt(0).commonAncestorContainer.parentElement)
          : null;

        const analysis = analyzeTextSelection(raw, container);

        // Always intercept at capture phase when interceptCtrlC is enabled or Alt+C is used,
        // defeating any website's preventDefault() / copy block!
        if (isAltC || config.interceptCtrlC) {
          e.preventDefault();
          e.stopImmediatePropagation();
          executeCleanCopy({ isProse: analysis.isProse });
        }
      }
    }
  }, true);

  // Copilot & Code Editor: Auto-inject Direct "Clean Copy" Button onto Code Blocks
  // Traverses virtual scroll to ensure entire long programs are captured 100%!
  function injectCopilotButtons() {
    const codeBlocks = document.querySelectorAll(
      '.scriptor-component-code-block, [role="textbox"][aria-label*="コード"], [role="textbox"][aria-label*="code" i], .scriptor-codeblock-virtualized, div[data-content="code"], pre:has(code), .code-block, [class*="codeblock" i], [class*="code-block" i]'
    );
    
    codeBlocks.forEach((block) => {
      if (block.dataset.ffCleanCopyInjected) return;
      block.dataset.ffCleanCopyInjected = "true";

      // Find toolbar or header
      const toolbar = block.querySelector('.fui-Overflow, ._803b581d5ee80236, .a3d13fac2210ea8e') || block;
      
      const cleanBtn = document.createElement('button');
      cleanBtn.type = 'button';
      cleanBtn.className = 'ff-copilot-clean-copy-btn';
      cleanBtn.setAttribute('data-ff-clean-copy-ui', 'true');
      cleanBtn.setAttribute('aria-hidden', 'true');
      cleanBtn.style.userSelect = 'none';
      cleanBtn.style.webkitUserSelect = 'none';
      cleanBtn.style.MozUserSelect = 'none';
      cleanBtn.title = 'プログラム表示領域内をスクロール走査して、画面外の行も含めた全コードを行番号なしで一発コピーします';
      cleanBtn.innerHTML = `
        <span style="font-size:13px;margin-right:4px;">📋</span>
        <span>行番号なし全コピー</span>
      `;

      cleanBtn.addEventListener('mousedown', (e) => {
        e.stopPropagation();
      });

      cleanBtn.addEventListener('click', async (e) => {
        e.preventDefault();
        e.stopPropagation();

        const originalText = cleanBtn.innerHTML;
        cleanBtn.disabled = true;
        cleanBtn.innerHTML = `
          <span style="font-size:13px;margin-right:4px;">⏳</span>
          <span>収集中...</span>
        `;

        try {
          // Perform automated virtual scroll scan and copy
          await scanAndCopyVirtualizedCode(block);
          cleanBtn.innerHTML = `
            <span style="font-size:13px;margin-right:4px;">✅</span>
            <span>コピー完了！</span>
          `;
          setTimeout(() => {
            cleanBtn.innerHTML = originalText;
            cleanBtn.disabled = false;
          }, 2000);
        } catch (err) {
          console.error("[CleanCopy] scanAndCopyVirtualizedCode error:", err);
          cleanBtn.innerHTML = originalText;
          cleanBtn.disabled = false;
        }
      });

      if (toolbar !== block) {
        toolbar.prepend(cleanBtn);
      } else {
        block.style.position = 'relative';
        cleanBtn.style.position = 'absolute';
        cleanBtn.style.top = '8px';
        cleanBtn.style.right = '8px';
        cleanBtn.style.zIndex = '100';
        block.appendChild(cleanBtn);
      }
    });
  }

  // Periodic check for dynamically loaded Copilot code blocks
  setInterval(injectCopilotButtons, 1000);

  // Listen for messages from background script
  extApi.runtime.onMessage.addListener((request, sender, sendResponse) => {
    if (request.action === "clean_and_copy") {
      executeCleanCopy({ stripPrompts: request.stripPrompts });
      sendResponse({ status: "success" });
    }
    return true;
  });

  // Maintain Keep-Alive Port to keep background script active and responsive
  let keepAlivePort = null;
  function initKeepAlive() {
    try {
      if (!extApi.runtime || !extApi.runtime.connect) return;
      keepAlivePort = extApi.runtime.connect({ name: "clean-copy-keepalive" });
      keepAlivePort.onDisconnect.addListener(() => {
        keepAlivePort = null;
        setTimeout(initKeepAlive, 5000);
      });
      keepAlivePort.onMessage.addListener(() => {});
    } catch (e) {}
  }

  // Periodic ping every 15 seconds to prevent background script from going into idle/stopped
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

  // Initialize on load
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initUI);
  } else {
    initUI();
  }
})();
