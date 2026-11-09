using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Karobar.Desktop.Views;

public sealed class SearchableSelectOptionItem
{
    public string Label { get; init; } = string.Empty;
    public object? RawItem { get; init; }
    public bool IsCurrentSelection { get; init; }
    public bool IsCreateOption { get; init; }
    public string CreateValue { get; init; } = string.Empty;
}

public partial class SearchableSelect : UserControl
{
    private const int MaxDisplayedOptions = 150;

    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> PropertyCache = new();

    private static readonly SolidColorBrush BrushTextActive = CreateFrozenBrush(15, 23, 42);     // #0F172A
    private static readonly SolidColorBrush BrushTextOpenMuted = CreateFrozenBrush(100, 116, 139); // #64748B
    private static readonly SolidColorBrush BrushPlaceholder = CreateFrozenBrush(148, 163, 184);  // #94A3B8
    private static readonly SolidColorBrush BrushBorderFocused = CreateFrozenBrush(59, 130, 246); // #3B82F6
    private static readonly SolidColorBrush BrushBorderNormal = CreateFrozenBrush(203, 213, 225); // #CBD5E1

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private INotifyCollectionChanged? _subscribedCollection;
    private bool _suppressSearchTextChanged;
    private bool _pendingCollectionRefresh;
    private DateTime _lastPopupClosedUtc = DateTime.MinValue;
    private List<SearchableSelectOptionItem> _filteredItems = [];

    public event EventHandler? EnterCommitted;

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(SearchableSelect),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(
            nameof(SelectedItem),
            typeof(object),
            typeof(SearchableSelect),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedItemChanged));

    public static readonly DependencyProperty DisplayMemberPathProperty =
        DependencyProperty.Register(
            nameof(DisplayMemberPath),
            typeof(string),
            typeof(SearchableSelect),
            new PropertyMetadata(string.Empty, OnDisplayConfigChanged));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(
            nameof(Placeholder),
            typeof(string),
            typeof(SearchableSelect),
            new PropertyMetadata("Select...", OnDisplayConfigChanged));

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(SearchableSelect),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnTextPropertyChanged));

    public static readonly DependencyProperty IsCreatableProperty =
        DependencyProperty.Register(
            nameof(IsCreatable),
            typeof(bool),
            typeof(SearchableSelect),
            new PropertyMetadata(false, OnDisplayConfigChanged));

    public static readonly DependencyProperty IsClearableProperty =
        DependencyProperty.Register(
            nameof(IsClearable),
            typeof(bool),
            typeof(SearchableSelect),
            new PropertyMetadata(false, OnDisplayConfigChanged));

    public static readonly DependencyProperty MoveFocusOnEnterProperty =
        DependencyProperty.Register(
            nameof(MoveFocusOnEnter),
            typeof(bool),
            typeof(SearchableSelect),
            new PropertyMetadata(true));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public string DisplayMemberPath
    {
        get => (string)GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsCreatable
    {
        get => (bool)GetValue(IsCreatableProperty);
        set => SetValue(IsCreatableProperty, value);
    }

    public bool IsClearable
    {
        get => (bool)GetValue(IsClearableProperty);
        set => SetValue(IsClearableProperty, value);
    }

    public bool MoveFocusOnEnter
    {
        get => (bool)GetValue(MoveFocusOnEnterProperty);
        set => SetValue(MoveFocusOnEnterProperty, value);
    }

    public SearchableSelect()
    {
        InitializeComponent();
        FontSize = 12.5;
        GotFocus += OnControlGotFocus;
        UpdateVisualDisplay();
    }

    public void FocusSearchInput()
    {
        PART_SearchTextBox.Focus();
        Keyboard.Focus(PART_SearchTextBox);
    }

    private void OnControlGotFocus(object sender, RoutedEventArgs e)
    {
        if (!PART_SearchTextBox.IsKeyboardFocusWithin)
        {
            PART_SearchTextBox.Focus();
        }
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SearchableSelect control)
            return;

        if (control._subscribedCollection != null)
        {
            control._subscribedCollection.CollectionChanged -= control.OnSourceCollectionChanged;
            control._subscribedCollection = null;
        }

        if (e.NewValue is INotifyCollectionChanged notifyCollection)
        {
            control._subscribedCollection = notifyCollection;
            notifyCollection.CollectionChanged += control.OnSourceCollectionChanged;
        }

        if (control.PART_Popup.IsOpen)
        {
            control.RebuildFilteredItems(control.PART_SearchTextBox.Text);
        }
        control.UpdateVisualDisplay();
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_pendingCollectionRefresh)
            return;

        _pendingCollectionRefresh = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _pendingCollectionRefresh = false;
            if (PART_Popup.IsOpen)
            {
                RebuildFilteredItems(PART_SearchTextBox.Text);
            }
            UpdateVisualDisplay();
        }));
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SearchableSelect control)
            return;

        if (control.IsCreatable && e.NewValue != null)
        {
            var label = control.GetItemLabel(e.NewValue);
            if (!string.Equals(control.Text, label, StringComparison.Ordinal))
            {
                control.Text = label;
            }
        }

        control.UpdateVisualDisplay();
        if (control.PART_Popup.IsOpen)
        {
            control.RebuildFilteredItems(control.PART_SearchTextBox.Text);
        }
    }

    private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SearchableSelect control)
            return;

        control.UpdateVisualDisplay();
    }

    private static void OnDisplayConfigChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SearchableSelect control)
            return;

        if (control.PART_Popup.IsOpen)
        {
            control.RebuildFilteredItems(control.PART_SearchTextBox.Text);
        }
        control.UpdateVisualDisplay();
    }

    private string GetItemLabel(object? item)
    {
        if (item == null)
            return string.Empty;

        var path = DisplayMemberPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            var type = item.GetType();
            var prop = PropertyCache.GetOrAdd((type, path), static key => key.Item1.GetProperty(key.Item2));
            if (prop != null)
            {
                return prop.GetValue(item)?.ToString() ?? string.Empty;
            }
        }

        return item.ToString() ?? string.Empty;
    }

    private string GetCurrentCommittedLabel()
    {
        if (SelectedItem != null)
        {
            var label = GetItemLabel(SelectedItem);
            if (!string.IsNullOrWhiteSpace(label))
                return label;
        }

        if (IsCreatable && !string.IsNullOrWhiteSpace(Text))
        {
            return Text.Trim();
        }

        return string.Empty;
    }

    private void UpdateVisualDisplay()
    {
        var searchTyped = PART_SearchTextBox.Text;
        var committedLabel = GetCurrentCommittedLabel();
        var hasCommittedValue = !string.IsNullOrWhiteSpace(committedLabel);

        if (!string.IsNullOrEmpty(searchTyped))
        {
            PART_SelectedTextBlock.Visibility = Visibility.Collapsed;
        }
        else
        {
            PART_SelectedTextBlock.Visibility = Visibility.Visible;
            if (hasCommittedValue)
            {
                PART_SelectedTextBlock.Text = committedLabel;
                PART_SelectedTextBlock.Foreground = PART_Popup.IsOpen
                    ? BrushTextOpenMuted
                    : BrushTextActive;
            }
            else
            {
                PART_SelectedTextBlock.Text = string.IsNullOrWhiteSpace(Placeholder) ? "Select..." : Placeholder;
                PART_SelectedTextBlock.Foreground = BrushPlaceholder;
            }
        }

        PART_ClearButton.Visibility = IsClearable && hasCommittedValue
            ? Visibility.Visible
            : Visibility.Collapsed;

        bool isActive = PART_SearchTextBox.IsKeyboardFocused || PART_Popup.IsOpen;
        RootBorder.BorderBrush = isActive ? BrushBorderFocused : BrushBorderNormal;
    }

    private void RebuildFilteredItems(string? query)
    {
        var cleanQuery = (query ?? string.Empty).Trim();
        var hasQuery = !string.IsNullOrEmpty(cleanQuery);
        var currentCommitted = GetCurrentCommittedLabel();

        var matchingOptions = new List<SearchableSelectOptionItem>(64);
        SearchableSelectOptionItem? selectedOptionIfCapped = null;
        var exactMatchFound = false;

        if (ItemsSource != null)
        {
            foreach (var raw in ItemsSource)
            {
                if (raw == null) continue;
                var label = GetItemLabel(raw);
                if (string.IsNullOrWhiteSpace(label)) continue;

                if (hasQuery && !label.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (hasQuery && string.Equals(label.Trim(), cleanQuery, StringComparison.OrdinalIgnoreCase))
                    exactMatchFound = true;

                bool isSelected = ReferenceEquals(raw, SelectedItem) ||
                                  (SelectedItem != null && Equals(raw, SelectedItem)) ||
                                  (IsCreatable && !string.IsNullOrWhiteSpace(currentCommitted) &&
                                   string.Equals(label, currentCommitted, StringComparison.OrdinalIgnoreCase));

                var optionItem = new SearchableSelectOptionItem
                {
                    Label = label,
                    RawItem = raw,
                    IsCurrentSelection = isSelected,
                    IsCreateOption = false
                };

                if (matchingOptions.Count < MaxDisplayedOptions)
                {
                    matchingOptions.Add(optionItem);
                }
                else if (isSelected && selectedOptionIfCapped == null)
                {
                    selectedOptionIfCapped = optionItem;
                }
            }
        }

        if (selectedOptionIfCapped != null)
        {
            matchingOptions.Insert(0, selectedOptionIfCapped);
        }

        var nextList = new List<SearchableSelectOptionItem>(matchingOptions.Count + 1);

        if (IsCreatable && hasQuery && !exactMatchFound)
        {
            nextList.Add(new SearchableSelectOptionItem
            {
                Label = $"+ Add \"{cleanQuery}\"",
                RawItem = null,
                IsCurrentSelection = false,
                IsCreateOption = true,
                CreateValue = cleanQuery
            });
        }

        nextList.AddRange(matchingOptions);
        _filteredItems = nextList;
        PART_ListBox.ItemsSource = _filteredItems;

        if (_filteredItems.Count > 0)
        {
            PART_EmptyState.Visibility = Visibility.Collapsed;
            PART_ListBox.Visibility = Visibility.Visible;

            if (hasQuery)
            {
                PART_ListBox.SelectedIndex = 0;
            }
            else
            {
                var currentIdx = 0;
                for (var i = 0; i < _filteredItems.Count; i++)
                {
                    if (_filteredItems[i].IsCurrentSelection)
                    {
                        currentIdx = i;
                        break;
                    }
                }
                PART_ListBox.SelectedIndex = currentIdx;
            }

            if (PART_Popup.IsOpen && PART_ListBox.SelectedItem != null)
            {
                PART_ListBox.ScrollIntoView(PART_ListBox.SelectedItem);
            }
        }
        else
        {
            PART_ListBox.Visibility = Visibility.Collapsed;
            PART_EmptyState.Visibility = Visibility.Visible;
            PART_EmptyStateText.Text = IsCreatable
                ? "Type to add a new option..."
                : "No matching options";
        }
    }

    private void OpenDropDown()
    {
        PART_Popup.IsOpen = true;
        PART_ChevronGlyph.Text = "▴";
        RebuildFilteredItems(PART_SearchTextBox.Text);
        UpdateVisualDisplay();
    }

    private void CloseDropDown(bool commitTypedCreatable)
    {
        if (commitTypedCreatable && IsCreatable && !string.IsNullOrWhiteSpace(PART_SearchTextBox.Text))
        {
            CommitCreatableValue(PART_SearchTextBox.Text.Trim());
        }

        PART_Popup.IsOpen = false;
        PART_ChevronGlyph.Text = "▾";
        ClearSearchBoxQuietly();
        UpdateVisualDisplay();
    }

    private void ClearSearchBoxQuietly()
    {
        _suppressSearchTextChanged = true;
        PART_SearchTextBox.Text = string.Empty;
        _suppressSearchTextChanged = false;
    }

    private void CommitOption(SearchableSelectOptionItem option)
    {
        if (option.IsCreateOption)
        {
            CommitCreatableValue(option.CreateValue);
        }
        else
        {
            SelectedItem = option.RawItem;
            if (IsCreatable)
            {
                Text = option.Label;
            }
        }

        CloseDropDown(commitTypedCreatable: false);
    }

    private void CommitCreatableValue(string rawValue)
    {
        var clean = rawValue.Trim();
        if (string.IsNullOrWhiteSpace(clean))
            return;

        if (ItemsSource != null)
        {
            foreach (var raw in ItemsSource)
            {
                if (raw == null) continue;
                var label = GetItemLabel(raw);
                if (string.Equals(label.Trim(), clean, StringComparison.OrdinalIgnoreCase))
                {
                    SelectedItem = raw;
                    Text = label;
                    return;
                }
            }
        }

        SelectedItem = null;
        Text = clean;
    }

    private void RootBorder_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (PART_ClearButton.IsMouseOver || PART_ChevronArea.IsMouseOver)
            return;

        PART_SearchTextBox.Focus();

        if (!PART_Popup.IsOpen && (DateTime.UtcNow - _lastPopupClosedUtc).TotalMilliseconds > 150)
        {
            OpenDropDown();
        }
    }

    private void PART_ChevronArea_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        PART_SearchTextBox.Focus();

        if (PART_Popup.IsOpen)
        {
            CloseDropDown(commitTypedCreatable: true);
        }
        else if ((DateTime.UtcNow - _lastPopupClosedUtc).TotalMilliseconds > 150)
        {
            OpenDropDown();
        }
    }

    private void PART_ClearButton_OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        SelectedItem = null;
        if (IsCreatable)
        {
            Text = string.Empty;
        }
        ClearSearchBoxQuietly();
        if (PART_Popup.IsOpen)
        {
            RebuildFilteredItems(string.Empty);
        }
        UpdateVisualDisplay();
        PART_SearchTextBox.Focus();
    }

    private void PART_SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSearchTextChanged)
            return;

        var currentText = PART_SearchTextBox.Text;
        if (!PART_Popup.IsOpen && PART_SearchTextBox.IsKeyboardFocused && !string.IsNullOrEmpty(currentText))
        {
            PART_Popup.IsOpen = true;
            PART_ChevronGlyph.Text = "▴";
        }

        if (PART_Popup.IsOpen)
        {
            RebuildFilteredItems(currentText);
        }
        UpdateVisualDisplay();
    }

    private void PART_SearchTextBox_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        UpdateVisualDisplay();
    }

    private void PART_SearchTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!IsKeyboardFocusWithin && !PART_Popup.IsKeyboardFocusWithin)
        {
            CloseDropDown(commitTypedCreatable: true);
        }
        else
        {
            UpdateVisualDisplay();
        }
    }

    private void PART_Popup_OnClosed(object? sender, EventArgs e)
    {
        _lastPopupClosedUtc = DateTime.UtcNow;
        PART_ChevronGlyph.Text = "▾";
        ClearSearchBoxQuietly();
        UpdateVisualDisplay();
    }

    private void PART_SearchTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            e.Handled = true;
            if (!PART_Popup.IsOpen)
            {
                OpenDropDown();
            }
            else if (_filteredItems.Count > 0)
            {
                var next = Math.Min(_filteredItems.Count - 1, PART_ListBox.SelectedIndex + 1);
                PART_ListBox.SelectedIndex = next;
                PART_ListBox.ScrollIntoView(PART_ListBox.SelectedItem);
            }
            return;
        }

        if (e.Key == Key.Up)
        {
            e.Handled = true;
            if (!PART_Popup.IsOpen)
            {
                OpenDropDown();
            }
            else if (_filteredItems.Count > 0)
            {
                var prev = Math.Max(0, PART_ListBox.SelectedIndex - 1);
                PART_ListBox.SelectedIndex = prev;
                PART_ListBox.ScrollIntoView(PART_ListBox.SelectedItem);
            }
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (PART_Popup.IsOpen || !string.IsNullOrEmpty(PART_SearchTextBox.Text))
            {
                e.Handled = true;
                CloseDropDown(commitTypedCreatable: false);
            }
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;

            if (PART_Popup.IsOpen)
            {
                if (PART_ListBox.SelectedItem is SearchableSelectOptionItem highlighted)
                {
                    CommitOption(highlighted);
                }
                else if (IsCreatable && !string.IsNullOrWhiteSpace(PART_SearchTextBox.Text))
                {
                    CommitCreatableValue(PART_SearchTextBox.Text);
                    CloseDropDown(commitTypedCreatable: false);
                }
                else
                {
                    CloseDropDown(commitTypedCreatable: false);
                }
            }
            else if (IsCreatable && !string.IsNullOrWhiteSpace(PART_SearchTextBox.Text))
            {
                CommitCreatableValue(PART_SearchTextBox.Text);
                ClearSearchBoxQuietly();
                UpdateVisualDisplay();
            }

            if (EnterCommitted != null)
            {
                EnterCommitted.Invoke(this, EventArgs.Empty);
            }
            else if (MoveFocusOnEnter)
            {
                PART_SearchTextBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
            return;
        }

        if (e.Key == Key.Tab && PART_Popup.IsOpen)
        {
            if (!string.IsNullOrWhiteSpace(PART_SearchTextBox.Text) &&
                PART_ListBox.SelectedItem is SearchableSelectOptionItem highlighted)
            {
                CommitOption(highlighted);
            }
            else
            {
                CloseDropDown(commitTypedCreatable: true);
            }
        }
    }

    private void OptionBorder_OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SearchableSelectOptionItem item })
        {
            PART_ListBox.SelectedItem = item;
        }
    }

    private void OptionBorder_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SearchableSelectOptionItem item })
        {
            e.Handled = true;
            CommitOption(item);
            PART_SearchTextBox.Focus();
        }
    }
}
