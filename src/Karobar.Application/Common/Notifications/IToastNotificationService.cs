namespace Karobar.Application.Interfaces;

public enum ToastType
{
    Success,
    Error,
    Warning,
    Info
}

public interface IToastNotificationService
{
    void Show(string message, ToastType type = ToastType.Info, string? title = null, int durationMs = 3600);
    void ShowSuccess(string message, string? title = null, int durationMs = 3400);
    void ShowError(string message, string? title = null, int durationMs = 4600);
    void ShowWarning(string message, string? title = null, int durationMs = 4000);
    void ShowInfo(string message, string? title = null, int durationMs = 3400);
    void Dismiss(Guid toastId);
    void ClearAll();
}
