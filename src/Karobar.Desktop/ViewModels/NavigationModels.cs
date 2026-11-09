using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Karobar.Desktop.ViewModels;

public partial class SidebarItemViewModel : ObservableObject
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string? QuickCreatePath { get; init; }
    public string? SubGroup { get; init; }
    public string IconGlyph { get; init; } = "•";
    public string AccentHex { get; init; } = "#3B82F6";
    public string? BadgeText { get; init; }

    public bool HasQuickCreate => !string.IsNullOrWhiteSpace(QuickCreatePath);
    public bool HasBadge => !string.IsNullOrWhiteSpace(BadgeText);

    [ObservableProperty]
    private bool _showSubGroupHeader;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isVisible = true;

    public string FavoriteGlyph => IsFavorite ? "★" : "☆";

    partial void OnIsFavoriteChanged(bool value)
    {
        OnPropertyChanged(nameof(FavoriteGlyph));
    }
}

public partial class SidebarSectionViewModel : ObservableObject
{
    public string Key { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string IconGlyph { get; init; } = "◈";
    public string AccentHex { get; init; } = "#3B82F6";
    public string AccentBgHex { get; init; } = "#EFF6FF";

    public ObservableCollection<SidebarItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private int _visibleCount;

    public string ChevronGlyph => IsExpanded ? "▾" : "▸";

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ChevronGlyph));
    }
}
