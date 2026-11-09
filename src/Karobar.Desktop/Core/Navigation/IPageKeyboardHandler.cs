namespace Karobar.Desktop.Core.Navigation;

/// <summary>
/// Universal contract implemented by every module View in Karobar.Desktop
/// to handle page-level keyboard shortcuts routed from MainWindow.PreviewKeyDown.
/// </summary>
public interface IPageKeyboardHandler
{
    /// <summary>
    /// Handles Ctrl+N / Alt+N (Open Create Form or focus Add input on Index/Directory pages).
    /// </summary>
    bool HandleNewShortcut();

    /// <summary>
    /// Handles Ctrl+S / Alt+S / Ctrl+Enter (Save active form or confirm active modal).
    /// </summary>
    bool HandleSaveShortcut();

    /// <summary>
    /// Handles Ctrl+F (Focus and select live search box on Index/Directory pages).
    /// </summary>
    bool HandleSearchShortcut();

    /// <summary>
    /// Handles Esc / Alt+C (Dismiss topmost modal -> return to Directory -> clear search).
    /// </summary>
    bool HandleEscapeShortcut();

    /// <summary>
    /// Handles F5 / Ctrl+R (Refresh current page data).
    /// </summary>
    bool HandleRefreshShortcut();

    /// <summary>
    /// Handles Ctrl+I (Open CSV Import modal on Index/Directory pages).
    /// </summary>
    bool HandleImportShortcut();

    /// <summary>
    /// Handles Ctrl+E (Trigger CSV Export on Index/Directory pages).
    /// </summary>
    bool HandleExportShortcut();
}
