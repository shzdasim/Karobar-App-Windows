using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Karobar.Application.Interfaces;
using Karobar.Desktop.Core.Navigation;
using Karobar.Desktop.ViewModels;

namespace Karobar.Desktop.Views;

public partial class ProductBatchesView : UserControl, IPageKeyboardHandler
{
    public ProductBatchesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ProductBatchesViewModel oldVm)
        {
            oldVm.RequestFocusProductSearch = null;
            oldVm.RequestFocusBatchNumber = null;
            oldVm.RequestFocusQuantity = null;
            oldVm.RequestFocusRack = null;
        }

        if (e.NewValue is ProductBatchesViewModel newVm)
        {
            newVm.RequestFocusProductSearch = FocusProductSearchBox;
            newVm.RequestFocusBatchNumber = FocusBatchNumberBox;
            newVm.RequestFocusQuantity = () => FocusAndSelect(QuantityTextBox);
            newVm.RequestFocusRack = () => FocusAndSelect(RackTextBox);
        }
    }

    public void FocusProductSearchBox()
    {
        Dispatcher.BeginInvoke(() =>
        {
            MainWorkbenchScrollViewer?.ScrollToTop();
            ProductSearchTextBox.Focus();
            Keyboard.Focus(ProductSearchTextBox);
            ProductSearchTextBox.SelectAll();
            if (DataContext is ProductBatchesViewModel vm)
            {
                vm.IsProductDropdownOpen = true;
            }
        }, DispatcherPriority.Input);
    }

    public void FocusBatchNumberBox()
    {
        Dispatcher.BeginInvoke(() =>
        {
            MainWorkbenchScrollViewer?.ScrollToTop();
            BatchNumberTextBox.Focus();
            Keyboard.Focus(BatchNumberTextBox);
            BatchNumberTextBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void FocusAndSelect(TextBox target)
    {
        Dispatcher.BeginInvoke(() =>
        {
            target.Focus();
            Keyboard.Focus(target);
            target.SelectAll();
        }, DispatcherPriority.Input);
    }

    private async void ProductSearchTextBox_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is not ProductBatchesViewModel vm)
            return;

        if (vm.ProductSearchResults.Count == 0)
        {
            await vm.RunProductSearchAsync(string.Empty, openDropdown: true);
        }
        else
        {
            vm.IsProductDropdownOpen = true;
        }
    }

    private async void ProductSearchTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not ProductBatchesViewModel vm)
            return;

        if (e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            vm.MoveProductHighlight(+1);
            if (vm.HighlightedProductResult != null)
            {
                ProductSearchResultsListBox.ScrollIntoView(vm.HighlightedProductResult);
            }
        }
        else if (e.Key == Key.Up && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            vm.MoveProductHighlight(-1);
            if (vm.HighlightedProductResult != null)
            {
                ProductSearchResultsListBox.ScrollIntoView(vm.HighlightedProductResult);
            }
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            await vm.CommitHighlightedProductAsync();
        }
        else if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (vm.IsProductDropdownOpen)
            {
                e.Handled = true;
                vm.IsProductDropdownOpen = false;
            }
        }
    }

    private async void ProductSearchResultsListBox_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ProductBatchesViewModel vm)
            return;

        if ((e.OriginalSource as DependencyObject) is { } source)
        {
            var container = ItemsControl.ContainerFromElement(ProductSearchResultsListBox, source) as ListBoxItem;
            if (container?.DataContext is BatchWorkbenchProductSearchItemDto item)
            {
                await vm.SelectProductFromSearchAsync(item);
            }
        }
    }

    private void BatchFormField_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None)
            return;

        if (sender is not TextBox current)
            return;

        e.Handled = true;
        current.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

        if (ReferenceEquals(current, BatchNumberTextBox))
        {
            FocusAndSelect(ExpiryDateTextBox);
        }
        else if (ReferenceEquals(current, ExpiryDateTextBox))
        {
            FocusAndSelect(QuantityTextBox);
        }
        else if (ReferenceEquals(current, QuantityTextBox))
        {
            FocusAndSelect(UnitCostTextBox);
        }
        else if (ReferenceEquals(current, UnitCostTextBox))
        {
            FocusAndSelect(UnitMrpTextBox);
        }
        else if (ReferenceEquals(current, UnitMrpTextBox))
        {
            Dispatcher.BeginInvoke(() =>
            {
                BatchLocationSelect.Focus();
                Keyboard.Focus(BatchLocationSelect);
            }, DispatcherPriority.Input);
        }
    }

    private void BatchLocationSelect_OnEnterCommitted(object? sender, RoutedEventArgs e)
    {
        FocusAndSelect(RackTextBox);
    }

    private void RackTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None)
            return;

        e.Handled = true;
        RackTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

        if (DataContext is ProductBatchesViewModel vm && vm.SaveBatchCommand.CanExecute(null))
        {
            vm.SaveBatchCommand.Execute(null);
        }
    }

    private void ProductBatchesView_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || DataContext is not ProductBatchesViewModel vm)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        // F2 or Alt+F -> Focus Step 1 Product Search
        if ((modifiers == ModifierKeys.None && key == Key.F2) ||
            (modifiers == ModifierKeys.Alt && key == Key.F))
        {
            FocusProductSearchBox();
            e.Handled = true;
            return;
        }

        // Alt+B -> Focus Step 2 Batch Number
        if (modifiers == ModifierKeys.Alt && key == Key.B)
        {
            if (vm.HasActiveProduct)
            {
                FocusBatchNumberBox();
            }
            else
            {
                FocusProductSearchBox();
            }
            e.Handled = true;
            return;
        }

        // Alt+R -> Refresh Workbench
        if (modifiers == ModifierKeys.Alt && key == Key.R)
        {
            if (vm.RefreshWorkbenchCommand.CanExecute(null))
            {
                vm.RefreshWorkbenchCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    // =========================================================================
    // IPageKeyboardHandler Implementation
    // =========================================================================

    public bool HandleNewShortcut()
    {
        if (DataContext is not ProductBatchesViewModel vm)
            return false;

        if (vm.IsLocationsModalOpen)
        {
            vm.ResetStorageLocationForm();
            return true;
        }

        if (vm.HasActiveProduct)
        {
            vm.ResetBatchForm();
        }
        else
        {
            FocusProductSearchBox();
        }
        return true;
    }

    public bool HandleSaveShortcut()
    {
        if (DataContext is not ProductBatchesViewModel vm)
            return false;

        if (vm.IsDeleteBatchModalOpen)
        {
            if (vm.ConfirmDeleteBatchCommand.CanExecute(null))
                vm.ConfirmDeleteBatchCommand.Execute(null);
            return true;
        }

        if (vm.IsMoveBatchModalOpen)
        {
            if (vm.ConfirmMoveBatchLocationCommand.CanExecute(null))
                vm.ConfirmMoveBatchLocationCommand.Execute(null);
            return true;
        }

        if (vm.IsLocationsModalOpen)
        {
            if (vm.SaveStorageLocationCommand.CanExecute(null))
                vm.SaveStorageLocationCommand.Execute(null);
            return true;
        }

        if (vm.SaveBatchCommand.CanExecute(null))
        {
            vm.SaveBatchCommand.Execute(null);
            return true;
        }

        return false;
    }

    public bool HandleSearchShortcut()
    {
        FocusProductSearchBox();
        return true;
    }

    public bool HandleEscapeShortcut()
    {
        if (DataContext is not ProductBatchesViewModel vm)
            return false;

        if (vm.IsDeleteBatchModalOpen)
        {
            vm.CloseDeleteBatchModal();
            return true;
        }

        if (vm.IsMoveBatchModalOpen)
        {
            vm.CloseMoveBatchModal();
            return true;
        }

        if (vm.IsLocationsModalOpen)
        {
            vm.CloseLocationsModal();
            return true;
        }

        if (vm.IsProductDropdownOpen)
        {
            vm.IsProductDropdownOpen = false;
            return true;
        }

        if (vm.IsEditingBatch)
        {
            vm.ResetBatchForm();
            return true;
        }

        return false;
    }

    public bool HandleRefreshShortcut()
    {
        if (DataContext is not ProductBatchesViewModel vm)
            return false;

        if (vm.RefreshWorkbenchCommand.CanExecute(null))
        {
            vm.RefreshWorkbenchCommand.Execute(null);
            return true;
        }

        return false;
    }
}
