using System.Text.Json;

namespace FontSelector.Services;

/// <summary>
/// Generates HTML, CSS, and update scripts for the WebView2 font preview.
/// </summary>
public static class PreviewHtmlBuilder
{
    public static string BuildInitialHtml()
    {
        return """
<!DOCTYPE html>
<html lang="ja">
<head>
<meta charset="utf-8">
<style>
  :root {
    --font-family: sans-serif;
    --font-size: 24pt;
    --font-weight: 400;
    --font-style: normal;
    --scale-x: 1;
    --text-color: #f1f5f9;
    --bg-color: #1a1a2e;
    --font-variation-settings: normal;
  }
  * {
    box-sizing: border-box;
    margin: 0;
    padding: 0;
  }
  html, body {
    width: 100%;
    height: 100%;
    background-color: var(--bg-color);
    color: var(--text-color);
    overflow-x: hidden;
    overflow-y: auto;
    user-select: text;
    -webkit-user-select: text;
  }
  ::-webkit-scrollbar {
    width: 8px;
    height: 8px;
  }
  ::-webkit-scrollbar-track {
    background: transparent;
  }
  ::-webkit-scrollbar-thumb {
    background: rgba(255, 255, 255, 0.2);
    border-radius: 4px;
  }
  ::-webkit-scrollbar-thumb:hover {
    background: rgba(255, 255, 255, 0.35);
  }
  #preview-container {
    padding: 12px;
    min-height: 100%;
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  .preview-line {
    width: 100%;
    min-height: 1.45em;
    line-height: 1.45;
  }
  .line-content {
    display: block;
    width: calc(100% / max(0.1, var(--scale-x)));
    transform-origin: 0 0;
    transform: scaleX(var(--scale-x));
    font-family: var(--font-family);
    font-size: var(--font-size);
    font-weight: var(--font-weight);
    font-style: var(--font-style);
    font-variation-settings: var(--font-variation-settings);
    word-break: break-word;
    white-space: pre-wrap;
  }
</style>
</head>
<body>
<div id="preview-container"></div>
<script>
  function updateStyles(config) {
    const root = document.documentElement.style;
    if (config.fontFamily !== undefined) root.setProperty('--font-family', config.fontFamily);
    if (config.fontSize !== undefined) root.setProperty('--font-size', config.fontSize + 'pt');
    if (config.fontWeight !== undefined) root.setProperty('--font-weight', config.fontWeight);
    if (config.fontStyle !== undefined) root.setProperty('--font-style', config.fontStyle);
    if (config.scaleX !== undefined) root.setProperty('--scale-x', config.scaleX);
    if (config.textColor !== undefined) root.setProperty('--text-color', config.textColor);
    if (config.bgColor !== undefined) root.setProperty('--bg-color', config.bgColor);
    if (config.fontVariationSettings !== undefined) root.setProperty('--font-variation-settings', config.fontVariationSettings);
  }

  function updateText(lines) {
    const container = document.getElementById('preview-container');
    container.innerHTML = '';
    for (let i = 0; i < lines.length; i++) {
      const lineDiv = document.createElement('div');
      lineDiv.className = 'preview-line';
      const contentDiv = document.createElement('div');
      contentDiv.className = 'line-content';
      const line = lines[i];
      if (!line) {
        contentDiv.innerHTML = '&nbsp;';
      } else {
        contentDiv.textContent = line;
      }
      lineDiv.appendChild(contentDiv);
      container.appendChild(lineDiv);
    }
  }
</script>
</body>
</html>
""";
    }

    public static string BuildUpdateStylesScript(
        string fontFamily,
        double fontSize,
        double fontWeight,
        string fontStyle,
        double scaleX,
        string textColor,
        string bgColor,
        string fontVariationSettings = "normal")
    {
        var data = new
        {
            fontFamily,
            fontSize,
            fontWeight,
            fontStyle,
            scaleX,
            textColor,
            bgColor,
            fontVariationSettings
        };

        var json = JsonSerializer.Serialize(data);
        return $"updateStyles({json});";
    }

    public static string BuildUpdateTextScript(IReadOnlyList<string> lines)
    {
        var json = JsonSerializer.Serialize(lines);
        return $"updateText({json});";
    }
}
