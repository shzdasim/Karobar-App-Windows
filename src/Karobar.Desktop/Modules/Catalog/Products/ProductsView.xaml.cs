using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Karobar.Desktop.Core.Navigation;
using Karobar.Desktop.ViewModels;

namespace Karobar.Desktop.Views;

public partial class ProductsView : UserControl, IPageKeyboardHandler
{
    public ProductsView()
    {
        InitializeComponent();
    }

    public void FocusSearchInput()
    {
        Dispatcher.BeginInvoke(() =>
        {
            SearchProductsBox.Focus();
            Keyboard.Focus(SearchProductsBox);
            SearchProductsBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    public bool HandleNewShortcut()
    {
        if (DataContext is not ProductsViewModel vm || !vm.IsDirectoryMode)
            return false;

        if (vm.OpenCreateProductCommand.CanExecute(null))
        {
            vm.OpenCreateProductCommand.Execute(null);
            Dispatcher.BeginInvoke(() =>
            {
                FormNameBox.Focus();
                Keyboard.Focus(FormNameBox);
                FormNameBox.SelectAll();
            }, DispatcherPriority.Background);
            return true;
        }

        return false;
    }

    public bool HandleSaveShortcut()
    {
        if (DataContext is not ProductsViewModel vm)
            return false;

        if (vm.IsDeleteModalOpen)
        {
            if (vm.ConfirmDeleteWithPasswordCommand.CanExecute(null))
                vm.ConfirmDeleteWithPasswordCommand.Execute(null);
            return true;
        }

        if (vm.IsBulkEditModalOpen)
        {
            if (vm.ConfirmBulkEditCommand.CanExecute(null))
                vm.ConfirmBulkEditCommand.Execute(null);
            return true;
        }

        if (vm.IsMoveBatchModalOpen)
        {
            if (vm.ConfirmMoveBatchCommand.CanExecute(null))
                vm.ConfirmMoveBatchCommand.Execute(null);
            return true;
        }

        if (vm.IsManageLocationsModalOpen)
        {
            if (vm.SaveStorageLocationCommand.CanExecute(null))
                vm.SaveStorageLocationCommand.Execute(null);
            return true;
        }

        if (vm.IsFormMode && vm.SaveProductCommand.CanExecute(null))
        {
            vm.SaveProductCommand.Execute(null);
            return true;
        }

        return false;
    }

    public bool HandleSearchShortcut()
    {
        if (DataContext is not ProductsViewModel vm || !vm.IsDirectoryMode)
            return false;

        FocusSearchInput();
        return true;
    }

    public bool HandleEscapeShortcut()
    {
        if (DataContext is not ProductsViewModel vm)
            return false;

        if (vm.IsDeleteModalOpen)
        {
            vm.CloseDeleteModalCommand.Execute(null);
            return true;
        }

        if (vm.IsBulkEditModalOpen)
        {
            vm.CloseBulkEditModalCommand.Execute(null);
            return true;
        }

        if (vm.IsCsvImportModalOpen)
        {
            vm.CloseCsvImportModalCommand.Execute(null);
            return true;
        }

        if (vm.IsSubstituteModalOpen)
        {
            vm.CloseSubstituteModalCommand.Execute(null);
            return true;
        }

        if (vm.IsMoveBatchModalOpen)
        {
            vm.CloseMoveBatchModalCommand.Execute(null);
            return true;
        }

        if (vm.IsManageLocationsModalOpen)
        {
            vm.CloseManageLocationsModalCommand.Execute(null);
            return true;
        }

        if (vm.IsFormMode)
        {
            if (vm.BackToDirectoryCommand.CanExecute(null))
                vm.BackToDirectoryCommand.Execute(null);
            return true;
        }

        if (vm.IsDirectoryMode && !string.IsNullOrEmpty(vm.SearchText))
        {
            vm.ClearSearchCommand.Execute(null);
            return true;
        }

        return false;
    }

    public bool HandleRefreshShortcut()
    {
        if (DataContext is not ProductsViewModel vm)
            return false;

        if (vm.IsDirectoryMode)
        {
            _ = vm.LoadProductsPageAsync(resetPage: false);
            return true;
        }

        return false;
    }

    public bool HandleImportShortcut()
    {
        if (DataContext is not ProductsViewModel vm || !vm.IsDirectoryMode)
            return false;

        if (vm.OpenCsvImportModalCommand.CanExecute(null))
        {
            vm.OpenCsvImportModalCommand.Execute(null);
            return true;
        }

        return false;
    }

    public bool HandleExportShortcut()
    {
        if (DataContext is not ProductsViewModel vm || !vm.IsDirectoryMode)
            return false;

        if (vm.ExportCsvCommand.CanExecute(null))
        {
            vm.ExportCsvCommand.Execute(null);
            return true;
        }

        return false;
    }

    private FrameworkElement[] GetProductFormSequence() =>
    [
        FormBarcodeBox,
        FormLocationSelect,
        FormRackBox,
        FormNarcoticSelect,
        FormNameBox,
        FormFormulationSelect,
        FormPackSizeBox,
        FormCategorySelect,
        FormBrandSelect,
        FormSupplierSelect,
        FormDescriptionBox,
        FormQuantityBox,
        FormPackPurchasePriceBox,
        FormPackSalePriceBox,
        FormUnitPurchasePriceBox,
        FormUnitSalePriceBox,
        FormMarginBox,
        FormWholeSalePackPriceBox,
        FormWholeSaleUnitPriceBox,
        FormWholeSaleMarginBox,
        FormAvgPriceBox,
        FormMaxDiscountBox,
        FormExpiryWarningDaysBox,
        SaveProductBtn
    ];

    private FrameworkElement[] GetBatchFormSequence() =>
    [
        BatchNumberBox,
        BatchExpiryPicker,
        BatchQtyBox,
        BatchLocationSelect,
        BatchRackBox,
        BatchMrpBox,
        BatchCostBox,
        SaveBatchBtn
    ];

    private FrameworkElement[] GetMultiSupplierFormSequence() =>
    [
        MultiSupplierSelect,
        MultiSupplierPrioritySelect,
        MultiSupplierRateBox,
        MultiSupplierLeadTimeBox,
        MultiSupplierSkuBox,
        SaveMultiSupplierBtn
    ];

    private void ProductFormTextBox_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox tb && !tb.IsReadOnly)
        {
            tb.Dispatcher.BeginInvoke(tb.SelectAll);
        }
    }

    private void ProductFormField_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            return;

        if (sender is FrameworkElement current)
        {
            e.Handled = true;
            AdvanceFocusToNextField(current, GetProductFormSequence());
        }
    }

    private void ProductFormSelect_OnEnterCommitted(object? sender, EventArgs e)
    {
        if (sender is FrameworkElement current)
        {
            AdvanceFocusToNextField(current, GetProductFormSequence());
        }
    }

    private void BatchFormField_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            return;

        if (sender is FrameworkElement current)
        {
            e.Handled = true;
            AdvanceFocusToNextField(current, GetBatchFormSequence());
        }
    }

    private void BatchFormSelect_OnEnterCommitted(object? sender, EventArgs e)
    {
        if (sender is FrameworkElement current)
        {
            AdvanceFocusToNextField(current, GetBatchFormSequence());
        }
    }

    private void MultiSupplierFormField_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            return;

        if (sender is FrameworkElement current)
        {
            e.Handled = true;
            AdvanceFocusToNextField(current, GetMultiSupplierFormSequence());
        }
    }

    private void MultiSupplierFormSelect_OnEnterCommitted(object? sender, EventArgs e)
    {
        if (sender is FrameworkElement current)
        {
            AdvanceFocusToNextField(current, GetMultiSupplierFormSequence());
        }
    }

    private static void AdvanceFocusToNextField(FrameworkElement current, IReadOnlyList<FrameworkElement> sequence)
    {
        var idx = -1;
        for (var i = 0; i < sequence.Count; i++)
        {
            if (ReferenceEquals(sequence[i], current))
            {
                idx = i;
                break;
            }
        }

        if (idx < 0)
            return;

        for (var nextIdx = idx + 1; nextIdx < sequence.Count; nextIdx++)
        {
            var candidate = sequence[nextIdx];
            if (!candidate.IsVisible || !candidate.IsEnabled)
                continue;

            if (candidate is TextBox { IsReadOnly: true })
                continue;

            candidate.BringIntoView();

            if (candidate is SearchableSelect select)
            {
                select.FocusSearchInput();
            }
            else if (candidate is TextBox tb)
            {
                tb.Focus();
                Keyboard.Focus(tb);
                tb.SelectAll();
            }
            else
            {
                candidate.Focus();
                Keyboard.Focus(candidate);
            }
            return;
        }
    }
}
