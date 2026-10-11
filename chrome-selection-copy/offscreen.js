/**
 * Offscreen Document for privileged clipboard write operations (Manifest V3)
 * Runs in extension context with 'clipboardWrite' permission, bypassing User Gesture restrictions.
 */
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message && message.action === 'offscreen_write_clipboard') {
    const text = message.text || '';

    // 1. Try modern navigator.clipboard.writeText
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(text)
        .then(() => {
          sendResponse({ status: 'success' });
        })
        .catch(() => {
          fallbackExecCommand(text, sendResponse);
        });
      return true;
    }

    // 2. Fallback
    fallbackExecCommand(text, sendResponse);
    return true;
  }
});

function fallbackExecCommand(text, sendResponse) {
  try {
    const textarea = document.getElementById('clipboard-textarea') || document.createElement('textarea');
    textarea.value = text;
    document.body.appendChild(textarea);
    textarea.focus();
    textarea.select();
    const ok = document.execCommand('copy');
    sendResponse({ status: ok ? 'success' : 'error' });
  } catch (err) {
    sendResponse({ status: 'error', error: String(err) });
  }
}
