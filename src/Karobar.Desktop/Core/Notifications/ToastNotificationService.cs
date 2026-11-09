using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Karobar.Application.Interfaces;

namespace Karobar.Desktop.ViewModels;

public partial class ToastNotificationItem : ObservableObject
{
    public const double MaxProgressBarWidth = 332.0;

    public Guid Id { get; init; } = Guid.NewGuid();
    public ToastType Type { get; init; } = ToastType.Info;
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public int DurationMs { get; init; } = 3500;

    [ObservableProperty]
    private double _opacity = 1.0;

    [ObservableProperty]
    private double _progressWidth = MaxProgressBarWidth;

    public string IconGlyph => Type switch
    {
        ToastType.Success => "✓",
        ToastType.Error => "✕",
        ToastType.Warning => "!",
        _ => "i"
    };

    public string AccentColorHex => Type switch
    {
        ToastType.Success => "#10B981",
        ToastType.Error => "#EF4444",
        ToastType.Warning => "#F59E0B",
        _ => "#3B82F6"
    };

    public string CardBorderHex => Type switch
    {
        ToastType.Success => "#6EE7B7",
        ToastType.Error => "#FCA5A5",
        ToastType.Warning => "#FCD34D",
        _ => "#93C5FD"
    };

    public string IconBadgeBgHex => Type switch
    {
        ToastType.Success => "#ECFDF5",
        ToastType.Error => "#FEF2F2",
        ToastType.Warning => "#FFFBEB",
        _ => "#EFF6FF"
    };

    public string IconBadgeBorderHex => Type switch
    {
        ToastType.Success => "#A7F3D0",
        ToastType.Error => "#FECACA",
        ToastType.Warning => "#FDE68A",
        _ => "#BFDBFE"
    };

    public string IconFgHex => Type switch
    {
        ToastType.Success => "#059669",
        ToastType.Error => "#DC2626",
        ToastType.Warning => "#D97706",
        _ => "#2563EB"
    };

    public string TitleFgHex => Type switch
    {
        ToastType.Success => "#065F46",
        ToastType.Error => "#991B1B",
        ToastType.Warning => "#92400E",
        _ => "#1E40AF"
    };
}

public partial class ToastNotificationService : ObservableObject, IToastNotificationService
{
    private const int MaxVisibleToasts = 5;
    private const double FadeOutWindowMs = 450.0;
    private DispatcherTimer? _timer;

    public ObservableCollection<ToastNotificationItem> ActiveToasts { get; } = [];

    public bool HasActiveToasts => ActiveToasts.Count > 0;

    public void ShowSuccess(string message, string? title = null, int durationMs = 3400)
        => Show(message, ToastType.Success, title ?? "Success", durationMs);

    public void ShowError(string message, string? title = null, int durationMs = 4600)
        => Show(message, ToastType.Error, title ?? "Error", durationMs);

    public void ShowWarning(string message, string? title = null, int durationMs = 4000)
        => Show(message, ToastType.Warning, title ?? "Warning", durationMs);

    public void ShowInfo(string message, string? title = null, int durationMs = 3400)
        => Show(message, ToastType.Info, title ?? "Notice", durationMs);

    public void Show(string message, ToastType type = ToastType.Info, string? title = null, int durationMs = 3600)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        string resolvedTitle = string.IsNullOrWhiteSpace(title)
            ? type switch
            {
                ToastType.Success => "Success",
                ToastType.Error => "Error",
                ToastType.Warning => "Warning",
                _ => "Notice"
            }
            : title.Trim();

        var item = new ToastNotificationItem
        {
            Id = Guid.NewGuid(),
            Type = type,
            Title = resolvedTitle,
            Message = message.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            DurationMs = Math.Clamp(durationMs, 1200, 15000),
            Opacity = 1.0,
            ProgressWidth = ToastNotificationItem.MaxProgressBarWidth
        };

        RunOnUiThread(() =>
        {
            while (ActiveToasts.Count >= MaxVisibleToasts)
            {
                ActiveToasts.RemoveAt(0);
            }

            ActiveToasts.Add(item);
            OnPropertyChanged(nameof(HasActiveToasts));
            EnsureTimerRunning();
        });
    }

    [RelayCommand]
    public void DismissToast(ToastNotificationItem? item)
    {
        if (item == null)
            return;

        Dismiss(item.Id);
    }

    public void Dismiss(Guid toastId)
    {
        RunOnUiThread(() =>
        {
            var target = ActiveToasts.FirstOrDefault(t => t.Id == toastId);
            if (target != null)
            {
                ActiveToasts.Remove(target);
                OnPropertyChanged(nameof(HasActiveToasts));
            }

            if (ActiveToasts.Count == 0)
            {
                _timer?.Stop();
            }
        });
    }

    public void ClearAll()
    {
        RunOnUiThread(() =>
        {
            ActiveToasts.Clear();
            OnPropertyChanged(nameof(HasActiveToasts));
            _timer?.Stop();
        });
    }

    private void EnsureTimerRunning()
    {
        if (System.Windows.Application.Current?.Dispatcher == null)
            return;

        if (_timer == null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Render, System.Windows.Application.Current.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(30)
            };
            _timer.Tick += OnTimerTick;
        }

        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (ActiveToasts.Count == 0)
        {
            _timer?.Stop();
            return;
        }

        var now = DateTime.UtcNow;
        var expired = new List<ToastNotificationItem>();

        foreach (var toast in ActiveToasts)
        {
            double elapsedMs = (now - toast.CreatedAtUtc).TotalMilliseconds;
            double remainingMs = toast.DurationMs - elapsedMs;

            if (remainingMs <= 0)
            {
                expired.Add(toast);
                continue;
            }

            double progressRatio = Math.Clamp(remainingMs / toast.DurationMs, 0.0, 1.0);
            toast.ProgressWidth = Math.Round(ToastNotificationItem.MaxProgressBarWidth * progressRatio, 1);

            if (remainingMs <= FadeOutWindowMs)
            {
                toast.Opacity = Math.Clamp(remainingMs / FadeOutWindowMs, 0.0, 1.0);
            }
            else
            {
                toast.Opacity = 1.0;
            }
        }

        if (expired.Count > 0)
        {
            foreach (var item in expired)
            {
                ActiveToasts.Remove(item);
            }

            OnPropertyChanged(nameof(HasActiveToasts));
            if (ActiveToasts.Count == 0)
            {
                _timer?.Stop();
            }
        }
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }
}
