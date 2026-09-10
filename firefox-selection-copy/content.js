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
    document.body.appendChild(toastElem);
  }

  // Core Line Number & Prompt Stripping Algorithm
  /**
   * Main text cleaning engine
   * Handles:
   * 1. Copilot interleaved line numbers (lone number lines like "2", "3", "4" on their own lines)
   * 2. Traditional prefixed line numbers ("1 | ", "12: ", "01 ", "[1] ")
   * 3. Terminal prompts ("$ ", "> ", "# ")
   * 4. Empty lines and &nbsp; entities
   */
  function cleanSelectedText(rawText, options = {}) {
    if (!rawText) return "";

    // Replace non-breaking spaces with standard space
    let normalized = rawText.replace(/\u00A0/g, ' ');

    const lines = normalized.split(/\r?\n/);
    if (lines.length === 0) return "";

    const stripNumbers = options.removeLineNumbers ?? config.removeLineNumbers;
    const stripPrompts = options.stripPrompts ?? config.removePromptPrefixes;

    // Pattern 1: Interleaved lone digit lines (Copilot virtualized codeblock pattern)
    // In Copilot Web, line numbers appear as isolated lines containing only numbers:
    // e.g. "import os\n2\nfrom playwright...\n3\n4\nSWITCH_URL = ..."
    let loneNumberLineCount = 0;
    let totalNonEmpty = 0;
    const loneNumRegex = /^\s*\d+\s*$/;
    const prefixedNumRegex = /^\s*(\d+|\[\d+\])[\s:\|\.\t]+/;

    for (let i = 0; i < lines.length; i++) {
      const trimmed = lines[i].trim();
      if (trimmed.length > 0) {
        totalNonEmpty++;
        if (loneNumRegex.test(trimmed)) {
          loneNumberLineCount++;
        }
      }
    }

    // Determine if this text is contaminated with Copilot lone line numbers:
    // Either at least 2 lone numeric lines, or >15% of non-empty lines are lone numbers
    const isCopilotInterleaved = (loneNumberLineCount >= 2) || (loneNumberLineCount >= 1 && totalNonEmpty <= 4);

    let processedLines = [];

    if (stripNumbers && isCopilotInterleaved) {
      // Filter out lone number lines, but preserve empty lines that represent code blank lines
      for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        const trimmed = line.trim();

        if (loneNumRegex.test(trimmed)) {
          // This is an isolated line number (like "2", "3", "4") -> omit it!
          // Note: If two number lines were adjacent (e.g. "3\n4"), it represents an empty code line between them!
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

    // Pattern 2: Prefixed line numbers (e.g. "1 | code", "1: code", "1  code", "[1] code")
    const cleanedLines = processedLines.map((line) => {
      let l = line;

      if (stripNumbers) {
        l = l.replace(/^\s*\d+[\s:\|\t]+/, '');
        l = l.replace(/^\s*\[\d+\]\s*/, '');
        l = l.replace(/^\s*\d+\.\s+/, '');
      }

      if (stripPrompts) {
        l = l.replace(/^\s*[$%>#]\s+/, '');
      }

      return l;
    });

    // Trim leading/trailing blank lines while preserving indentation & internal blank lines
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

  // DOM Selection Helper: handles Copilot Fluent UI & standard DOM structures directly
  function getSelectedTextWithDOMCleanup() {
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0) return "";

    try {
      const range = selection.getRangeAt(0);
      const container = range.commonAncestorContainer.nodeType === 1
        ? range.commonAncestorContainer
        : range.commonAncestorContainer.parentElement;

      // Check if selection is within Copilot's code editor
      const copilotBlock = container.closest?.('.scriptor-component-code-block, [role="textbox"][aria-label*="コード"], [role="textbox"][aria-label*="code" i]') || container.querySelector?.('[data-line-index]');

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
    } catch (e) {
      console.warn("[CleanCopy] DOM inspection fallback", e);
    }

    return selection.toString();
  }

  // Execute Clean Copy & write to clipboard
  async function executeCleanCopy(opts = {}) {
    const rawText = getSelectedTextWithDOMCleanup();
    if (!rawText || !rawText.trim()) return;

    const cleaned = cleanSelectedText(rawText, opts);

    try {
      if (navigator.clipboard && navigator.clipboard.writeText) {
        await navigator.clipboard.writeText(cleaned);
      } else {
        // Fallback for restricted clipboard contexts
        const textarea = document.createElement("textarea");
        textarea.value = cleaned;
        textarea.style.position = "fixed";
        textarea.style.opacity = "0";
        document.body.appendChild(textarea);
        textarea.select();
        document.execCommand("copy");
        document.body.removeChild(textarea);
      }

      showToast(`✨ コピー完了！行番号を削除しました (${cleaned.split('\n').length}行)`);
    } catch (err) {
      console.error("[CleanCopy] Clipboard write error:", err);
      showToast("⚠️ コピーに失敗しました");
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
    }, 2200);
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
    if (text.length < 3) {
      hideFloatingButton();
      return;
    }

    // Only show if selection contains newlines or starts with a digit/prompt
    const hasMultipleLines = text.includes('\n');
    const looksLikeCodeOrPrompt = /^\s*(\d+|[$#>])/.test(text);

    if (!hasMultipleLines && !looksLikeCodeOrPrompt) {
      hideFloatingButton();
      return;
    }

    try {
      const range = selection.getRangeAt(0);
      const rect = range.getBoundingClientRect();
      if (!rect || (rect.width === 0 && rect.height === 0)) {
        hideFloatingButton();
        return;
      }

      if (!floatingBtn) initUI();
      if (!floatingBtn) return;

      const top = window.scrollY + rect.top - 38;
      const left = window.scrollX + rect.left + (rect.width / 2) - 60;

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
    // Debounce to allow user to finish selecting
    clearTimeout(window._ffSelectionTimer);
    window._ffSelectionTimer = setTimeout(updateFloatingButton, 200);
  });

  document.addEventListener("mousedown", (e) => {
    if (floatingBtn && floatingBtn.contains(e.target)) return;
    hideFloatingButton();
  });

  // Intercept and unblock Right-Click (contextmenu) at CAPTURE PHASE!
  // Websites block right-click by calling e.preventDefault() in their own listener.
  // By capturing the event at window level with useCapture: true, we stop propagation
  // before the page's script can block the context menu when text is selected!
  window.addEventListener("contextmenu", (e) => {
    const selection = window.getSelection();
    if (selection && !selection.isCollapsed && selection.toString().trim().length > 0) {
      // Prevents the webpage from intercepting/canceling the right-click menu
      e.stopImmediatePropagation();
    }
  }, true);

  // Global Copy Event Interceptor (Capture Phase)
  // When user right-clicks and chooses standard "Copy", or uses any copy action:
  // We sanitize the clipboard content so lone line numbers (Copilot) and line numbers are NEVER copied!
  document.addEventListener("copy", (e) => {
    const rawText = getSelectedTextWithDOMCleanup();
    if (!rawText || !rawText.trim()) return;

    const lines = rawText.split(/\r?\n/);
    const loneNumsCount = lines.filter(l => /^\s*\d+\s*$/.test(l.trim())).length;
    const hasPrefixedNums = /^\s*\d+[\s:\|\t]+/m.test(rawText) || /^\s*\[\d+\]/m.test(rawText);

    // If Copilot interleaved lone numbers (e.g. 2, 3...) or prefixed numbers exist
    if (loneNumsCount >= 2 || (loneNumsCount >= 1 && lines.length <= 6) || hasPrefixedNums) {
      const cleaned = cleanSelectedText(rawText);
      if (cleaned && cleaned !== rawText && e.clipboardData) {
        e.preventDefault();
        e.stopImmediatePropagation();
        e.clipboardData.setData('text/plain', cleaned);
        showToast(`✨ コピー完了！行番号を自動削除しました (${cleaned.split('\n').length}行)`);
      }
    }
  }, true);

  // Intercept Ctrl+C / Cmd+C at CAPTURE PHASE!
  // This solves environments (like Copilot Web or code editors) where Ctrl+C is disabled or canceled
  window.addEventListener("keydown", (e) => {
    const isCopyKey = (e.key === 'c' || e.key === 'C') && (e.ctrlKey || e.metaKey);
    const isAltC = (e.key === 'c' || e.key === 'C') && e.altKey;

    if (isCopyKey || isAltC) {
      const selection = window.getSelection();
      if (selection && !selection.isCollapsed && selection.toString().trim().length > 0) {
        const raw = selection.toString();
        // Check if selection has line numbers, lone numbers, or prompts
        const lines = raw.split(/\r?\n/);
        const hasLoneNums = lines.filter(l => /^\s*\d+\s*$/.test(l.trim())).length >= 1;
        const hasLineNums = /^\s*\d+[\s:\|\t]+/m.test(raw) || /^\s*\[\d+\]/m.test(raw) || hasLoneNums;

        // If Alt+C was pressed, or if Ctrl+C was pressed on text with line numbers
        if (isAltC || (config.interceptCtrlC && hasLineNums)) {
          // Prevent the site from blocking the copy!
          e.preventDefault();
          e.stopImmediatePropagation();
          executeCleanCopy();
        }
      }
    }
  }, true); // Use capture phase = true

  // Copilot & Code Editor: Auto-inject Direct "Clean Copy" Button onto Code Blocks
  // This gives the user a 1-click button directly on Copilot without needing to drag/select or right click!
  function injectCopilotButtons() {
    // Look for Copilot virtualized code blocks or standard code blocks
    const codeBlocks = document.querySelectorAll('.scriptor-component-code-block, [role="textbox"][aria-label*="コード"], [role="textbox"][aria-label*="code" i]');
    
    codeBlocks.forEach((block) => {
      if (block.dataset.ffCleanCopyInjected) return;
      block.dataset.ffCleanCopyInjected = "true";

      // Find toolbar or header
      const toolbar = block.querySelector('.fui-Overflow, ._803b581d5ee80236, .a3d13fac2210ea8e') || block;
      
      const cleanBtn = document.createElement('button');
      cleanBtn.type = 'button';
      cleanBtn.className = 'ff-copilot-clean-copy-btn';
      cleanBtn.title = '行番号をすべて除外してコード全体をコピー';
      cleanBtn.innerHTML = `
        <span style="font-size:13px;margin-right:4px;">📋</span>
        <span>行番号なしコピー</span>
      `;

      cleanBtn.addEventListener('click', async (e) => {
        e.preventDefault();
        e.stopPropagation();

        // Extract lines with data-line-index
        const lineElements = Array.from(block.querySelectorAll('[data-line-index]'));
        let fullCode = '';

        if (lineElements.length > 0) {
          fullCode = lineElements.map(el => el.textContent.replace(/\u00A0/g, ' ')).join('\n');
        } else {
          // Fallback to text cleaning on block content
          fullCode = cleanSelectedText(block.innerText || block.textContent);
        }

        if (fullCode) {
          try {
            await navigator.clipboard.writeText(fullCode);
            showToast(`✨ 全コードを行番号なしでコピーしました！ (${fullCode.split('\n').length}行)`);
          } catch (err) {
            executeCleanCopy();
          }
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

  // Initialize on load
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initUI);
  } else {
    initUI();
  }
})();
