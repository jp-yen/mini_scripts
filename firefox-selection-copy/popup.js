/**
 * Firefox Selection Clean Copy - Popup Logic
 */

const extApi = typeof browser !== 'undefined' ? browser : chrome;

const defaultSettings = {
  removeLineNumbers: true,
  removePromptPrefixes: true,
  showFloatingButton: true,
  interceptCtrlC: true
};

document.addEventListener("DOMContentLoaded", () => {
  const lineNumCheckbox = document.getElementById("removeLineNumbers");
  const promptsCheckbox = document.getElementById("removePromptPrefixes");
  const floatBtnCheckbox = document.getElementById("showFloatingButton");
  const ctrlCCheckbox = document.getElementById("interceptCtrlC");
  const testInput = document.getElementById("testInput");
  const testCleanBtn = document.getElementById("testCleanBtn");
  const testStatus = document.getElementById("testStatus");

  // Load existing settings
  if (extApi.storage && extApi.storage.sync) {
    extApi.storage.sync.get(defaultSettings, (saved) => {
      lineNumCheckbox.checked = saved.removeLineNumbers;
      promptsCheckbox.checked = saved.removePromptPrefixes;
      floatBtnCheckbox.checked = saved.showFloatingButton;
      ctrlCCheckbox.checked = saved.interceptCtrlC;
    });
  }

  // Save changes
  function saveSettings() {
    const current = {
      removeLineNumbers: lineNumCheckbox.checked,
      removePromptPrefixes: promptsCheckbox.checked,
      showFloatingButton: floatBtnCheckbox.checked,
      interceptCtrlC: ctrlCCheckbox.checked
    };

    if (extApi.storage && extApi.storage.sync) {
      extApi.storage.sync.set(current);
    }
  }

  lineNumCheckbox.addEventListener("change", saveSettings);
  promptsCheckbox.addEventListener("change", saveSettings);
  floatBtnCheckbox.addEventListener("change", saveSettings);
  ctrlCCheckbox.addEventListener("change", saveSettings);

  // Quick Test in Popup
  testCleanBtn.addEventListener("click", async () => {
    const raw = testInput.value;
    if (!raw.trim()) {
      testStatus.textContent = "テキストを入力してください";
      testStatus.style.color = "#ef4444";
      return;
    }

    const lines = raw.split(/\r?\n/);
    const cleaned = lines.map(line => {
      let l = line;
      if (lineNumCheckbox.checked) {
        l = l.replace(/^\s*\d+[\s:\|\t]+/, '');
        l = l.replace(/^\s*\[\d+\]\s*/, '');
      }
      if (promptsCheckbox.checked) {
        l = l.replace(/^\s*[$%>#]\s+/, '');
      }
      return l;
    }).join('\n').trim();

    try {
      await navigator.clipboard.writeText(cleaned);
      testStatus.textContent = "✔ クリップボードにコピーしました！";
      testStatus.style.color = "#10b981";
      setTimeout(() => {
        testStatus.textContent = "";
      }, 2500);
    } catch (err) {
      testStatus.textContent = "コピーに失敗しました";
      testStatus.style.color = "#ef4444";
    }
  });
});
