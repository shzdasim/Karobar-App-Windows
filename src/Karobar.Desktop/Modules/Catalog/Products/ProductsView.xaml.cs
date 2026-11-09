using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Karobar.Desktop.Views;

public partial class ProductsView : UserControl
{
    public ProductsView()
    {
        InitializeComponent();
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
