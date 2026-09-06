using FontSelector.Models;

namespace FontSelector.Services;

/// <summary>
/// Service interface for retrieving installed font information.
/// </summary>
public interface IFontService
{
    /// <summary>
    /// Retrieves all installed font families with their typeface variations synchronously.
    /// </summary>
    IReadOnlyList<FontFamilyInfo> GetAllFonts(IProgress<(int current, int total, string fontName, int phase)>? progress = null);

    /// <summary>
    /// Retrieves all installed font families with their typeface variations asynchronously on a background thread.
    /// </summary>
    Task<IReadOnlyList<FontFamilyInfo>> GetAllFontsAsync(
        IProgress<(int current, int total, string fontName, int phase)>? progress = null,
        CancellationToken cancellationToken = default);
}
