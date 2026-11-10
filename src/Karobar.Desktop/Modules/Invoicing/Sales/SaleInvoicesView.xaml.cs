using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Karobar.Application.Interfaces;
using Karobar.Desktop.Core.Navigation;
using Karobar.Desktop.ViewModels;

namespace Karobar.Desktop.Views;

public partial class SaleInvoicesView : UserControl, IPageKeyboardHandler
{
    private static readonly string[] RetailCols = ["product", "batch", "quantity", "disc"];
    private static readonly string[] WholesaleCols =
    [
        "product", "batch", "pack_quantity", "unit_quantity", "disc",
        "pack_bonus", "unit_bonus", "pack_price", "price"
    ];

    // Drag & 8-way resize state for ProductSearchDialogBorder
    private bool _isDraggingProductModal;
    private Point _productModalDragStartMouse;
    private double _productModalDragStartLeft;
    private double _productModalDragStartTop;

    private bool _isResizingProductModal;
    private string _productModalResizeDir = string.Empty;
    private Point _productModalResizeStartMouse;
    private double _productModalResizeStartLeft;
    private double _productModalResizeStartTop;
    private double _productModalResizeStartWidth;
    private double _productModalResizeStartHeight;
    private Point _lastProductListMousePos;

    public SaleInvoicesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => AttachViewModelCallbacks(DataContext as SaleInvoicesViewModel);
    }

    private SaleInvoicesViewModel? Vm => DataContext as SaleInvoicesViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SaleInvoicesViewModel oldVm)
        {
            DetachViewModelCallbacks(oldVm);
        }
        if (e.NewValue is SaleInvoicesViewModel newVm)
        {
            AttachViewModelCallbacks(newVm);
        }
    }

    private void AttachViewModelCallbacks(SaleInvoicesViewModel? vm)
    {
        if (vm == null) return;

        vm.RequestFocusCustomerSelect = () => FocusElementDeferred(CustomerSelectTriggerButton);
        vm.RequestFocusCustomerModalSearch = () => FocusTextBoxDeferred(CustomerModalSearchTextBox);
        vm.RequestFocusOneTimeCustomerName = () => FocusTextBoxDeferred(OneTimeCustomerNameTextBox);
        vm.RequestFocusOneTimeCustomerPhone = () => FocusTextBoxDeferred(OneTimeCustomerPhoneTextBox);
        vm.RequestFocusDoctorName = () => FocusTextBoxDeferred(DoctorNameTextBox);
        vm.RequestFocusPatientName = () => FocusTextBoxDeferred(PatientNameTextBox);
        vm.RequestFocusRemarks = () => FocusTextBoxDeferred(RemarksTextBox);
        vm.RequestFocusDirectorySearch = () => FocusTextBoxDeferred(DirectorySearchTextBox);
        vm.RequestFocusProductModalSearch = () => FocusTextBoxDeferred(ProductModalSearchTextBox);
        vm.RequestFocusBatchModalSearch = () => FocusTextBoxDeferred(BatchModalSearchTextBox);
        vm.RequestFocusQuickAddProductName = () => FocusTextBoxDeferred(QuickAddProductNameTextBox);
        vm.RequestFocusShowInvoiceSearch = () => FocusTextBoxDeferred(ShowInvoiceSearchTextBox);
        vm.RequestFocusDeletePassword = () => FocusTextBoxDeferred(DeleteInvoicePasswordTextBox);
        vm.RequestApplyProductSearchModalLayout = ApplySavedProductSearchModalLayout;
        vm.RequestFocusLineCell = FocusLineCellDeferred;
        vm.RequestFocusFooterField = FocusFooterFieldDeferred;
        vm.RequestPrintDocument = PrintCurrentDetailDocument;
        vm.RequestPrintDeliveryChallan = PrintCurrentDeliveryChallanDocument;

        if (vm.IsFormMode && vm.IsProductSearchModalOpen)
        {
            ApplySavedProductSearchModalLayout();
            FocusTextBoxDeferred(ProductModalSearchTextBox);
        }
    }

    private static void DetachViewModelCallbacks(SaleInvoicesViewModel vm)
    {
        vm.RequestFocusCustomerSelect = null;
        vm.RequestFocusCustomerModalSearch = null;
        vm.RequestFocusOneTimeCustomerName = null;
        vm.RequestFocusOneTimeCustomerPhone = null;
        vm.RequestFocusDoctorName = null;
        vm.RequestFocusPatientName = null;
        vm.RequestFocusRemarks = null;
        vm.RequestFocusDirectorySearch = null;
        vm.RequestFocusProductModalSearch = null;
        vm.RequestFocusBatchModalSearch = null;
        vm.RequestFocusQuickAddProductName = null;
        vm.RequestFocusShowInvoiceSearch = null;
        vm.RequestFocusDeletePassword = null;
        vm.RequestApplyProductSearchModalLayout = null;
        vm.RequestFocusLineCell = null;
        vm.RequestFocusFooterField = null;
        vm.RequestPrintDocument = null;
        vm.RequestPrintDeliveryChallan = null;
    }

    // =========================================================================
    // 1. IPageKeyboardHandler (All 7 Universal Methods)
    // =========================================================================

    public bool HandleNewShortcut()
    {
        var vm = Vm;
        if (vm == null) return false;

        if (vm.IsFormMode)
        {
            _ = vm.ClearOrNewInvoiceAsync();
            return true;
        }

        if (vm.IsDetailMode)
        {
            vm.CreateNewFromShowMode();
            return true;
        }

        if (string.Equals(vm.LockedDirectorySaleType, "wholesale", StringComparison.OrdinalIgnoreCase))
        {
            vm.NavigateToCreateWholesale();
        }
        else
        {
            vm.NavigateToCreateRetail();
        }
        return true;
    }

    public bool HandleSaveShortcut()
    {
        var vm = Vm;
        if (vm == null || !vm.IsFormMode) return false;
        _ = vm.SubmitInvoiceAsync();
        return true;
    }

    public bool HandleSearchShortcut()
    {
        var vm = Vm;
        if (vm == null) return false;

        if (vm.IsDirectoryMode)
        {
            FocusTextBoxDeferred(DirectorySearchTextBox);
            return true;
        }

        if (vm.IsFormMode)
        {
            var line = vm.Lines.LastOrDefault() ?? vm.AppendEmptyLine();
            _ = vm.OpenProductSearchForLineAsync(line);
            return true;
        }

        if (vm.IsDetailMode)
        {
            _ = vm.OpenShowInvoiceSearchModalAsync();
            return true;
        }

        return false;
    }

    public bool HandleEscapeShortcut()
    {
        var vm = Vm;
        if (vm == null) return false;

        if (vm.IsQuickAddProductModalOpen)
        {
            vm.CloseQuickAddProductModal();
            return true;
        }
        if (vm.IsProductSearchModalOpen)
        {
            vm.CloseProductSearchModal();
            return true;
        }
        if (vm.IsBatchSearchModalOpen)
        {
            vm.CloseBatchSearchModal();
            return true;
        }
        if (vm.IsCustomerSearchModalOpen)
        {
            vm.CloseCustomerSearchModal();
            return true;
        }
        if (vm.IsSubstituteModalOpen)
        {
            vm.CloseSubstituteModal();
            return true;
        }
        if (vm.IsDraftsModalOpen)
        {
            vm.CloseDraftsModal();
            return true;
        }
        if (vm.IsDiscountOverrideModalOpen || vm.IsCreditOverrideModalOpen || vm.IsExpiredOverrideModalOpen)
        {
            vm.CloseOverrideModals();
            return true;
        }
        if (vm.IsPreShiftDrawerModalOpen)
        {
            vm.SkipPreShiftDrawerPrompt();
            return true;
        }
        if (vm.IsShowInvoiceSearchModalOpen)
        {
            vm.CloseShowInvoiceSearchModal();
            return true;
        }
        if (vm.IsRecordPaymentModalOpen)
        {
            vm.CloseRecordPaymentModal();
            return true;
        }
        if (vm.IsHistoryModalOpen)
        {
            vm.CloseHistoryModal();
            return true;
        }
        if (vm.IsReverseModalOpen)
        {
            vm.CloseReverseModal();
            return true;
        }
        if (vm.IsDeleteModalOpen)
        {
            vm.CloseDeleteModal();
            return true;
        }

        return false;
    }

    public bool HandleRefreshShortcut()
    {
        var vm = Vm;
        if (vm == null) return false;

        if (vm.IsDirectoryMode)
        {
            _ = vm.LoadDirectoryPageAsync(vm.CurrentPage);
            return true;
        }
        return false;
    }

    public bool HandleImportShortcut() => false;

    public bool HandleExportShortcut() => false;

    public bool HandlePrintShortcut()
    {
        var vm = Vm;
        if (vm == null || !vm.IsDetailMode) return false;
        PrintCurrentDetailDocument();
        return true;
    }

    // =========================================================================
    // 2. PAGE-LEVEL KEYBOARD SHORTCUTS (Ctrl+<Key> primary, Alt+<Key> fallback)
    // =========================================================================

    private void SaleInvoicesView_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm == null) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;

        if (key == Key.Escape)
        {
            if (HandleEscapeShortcut())
            {
                e.Handled = true;
                return;
            }
        }

        bool hasCtrlOrAlt = ((mods & ModifierKeys.Control) == ModifierKeys.Control ||
                             (mods & ModifierKeys.Alt) == ModifierKeys.Alt) &&
                            (mods & ModifierKeys.Shift) == 0;

        if (hasCtrlOrAlt)
        {
            switch (key)
            {
                case Key.S:
                    if (vm.IsFormMode)
                    {
                        _ = vm.SubmitInvoiceAsync();
                        e.Handled = true;
                    }
                    return;

                case Key.H:
                    if (vm.IsFormMode)
                    {
                        _ = vm.HoldAndNewAsync();
                        e.Handled = true;
                    }
                    else if (vm.IsDetailMode || vm.IsDirectoryMode)
                    {
                        _ = vm.OpenDraftsModalAsync();
                        e.Handled = true;
                    }
                    return;

                case Key.D:
                    if (vm.IsFormMode || vm.IsDirectoryMode)
                    {
                        _ = vm.OpenDraftsModalAsync();
                        e.Handled = true;
                    }
                    else if (vm.IsDetailMode)
                    {
                        vm.OpenDeleteModal(null);
                        e.Handled = true;
                    }
                    return;

                case Key.N:
                    if (HandleNewShortcut())
                    {
                        e.Handled = true;
                    }
                    return;

                case Key.W:
                    if (vm.IsDirectoryMode)
                    {
                        vm.NavigateToCreateWholesale();
                        e.Handled = true;
                    }
                    else if (vm.IsFormMode)
                    {
                        vm.SwitchFormSaleType("wholesale");
                        e.Handled = true;
                    }
                    return;

                case Key.E:
                    if (vm.IsDetailMode)
                    {
                        _ = vm.EditCurrentDetailInvoiceAsync();
                        e.Handled = true;
                    }
                    return;

                case Key.P:
                    if (vm.IsDetailMode)
                    {
                        PrintCurrentDetailDocument();
                        e.Handled = true;
                    }
                    return;

                case Key.B:
                    if (vm.IsFormMode || vm.IsDetailMode)
                    {
                        vm.NavigateBackToList();
                        e.Handled = true;
                    }
                    return;

                case Key.F:
                    if (HandleSearchShortcut())
                    {
                        e.Handled = true;
                    }
                    return;
            }
        }
    }

    private void DirectorySearchTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Vm != null)
        {
            _ = Vm.LoadDirectoryPageAsync(1);
            e.Handled = true;
        }
    }

    // =========================================================================
    // 3. HEADER KEYBOARD NAVIGATION CHAIN
    // =========================================================================

    private void CustomerSelectTriggerButton_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm == null) return;

        if (e.Key == Key.Enter || e.Key == Key.Down || e.Key == Key.Space)
        {
            vm.OpenCustomerSearchModal();
            e.Handled = true;
        }
    }

    private void HeaderField_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm == null || sender is not FrameworkElement fe) return;

        var tag = fe.Tag as string ?? string.Empty;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            AdvanceFromHeaderField(vm, tag);
        }
    }

    private void AdvanceFromHeaderField(SaleInvoicesViewModel vm, string currentTag)
    {
        switch (currentTag)
        {
            case "one_time_name":
                FocusTextBoxDeferred(OneTimeCustomerPhoneTextBox);
                return;

            case "one_time_phone":
                if (vm.IsPharmacy)
                {
                    FocusTextBoxDeferred(DoctorNameTextBox);
                }
                else
                {
                    OpenFirstRowProductSearchOrFocusCell(vm);
                }
                return;

            case "doctor_name":
                FocusTextBoxDeferred(PatientNameTextBox);
                return;

            case "patient_name":
                FocusTextBoxDeferred(RemarksTextBox);
                return;

            case "remarks":
                OpenFirstRowProductSearchOrFocusCell(vm);
                return;
        }
    }

    private void OpenFirstRowProductSearchOrFocusCell(SaleInvoicesViewModel vm)
    {
        if (vm.Lines.Count == 0)
        {
            vm.AppendEmptyLine();
        }

        var firstRow = vm.Lines[0];
        if (!firstRow.HasProduct)
        {
            _ = vm.OpenProductSearchForLineAsync(firstRow);
        }
        else
        {
            FocusLineCellDeferred(0, vm.IsWholesaleFormMode ? "pack_quantity" : "quantity");
        }
    }

    // =========================================================================
    // 4. REPEATER ROWS KEYBOARD CHAIN (Enter, Shift+Tab, ArrowUp, ArrowDown)
    // =========================================================================

    private void NumericCell_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            tb.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(tb.SelectAll));
        }
    }

    private void RepeaterCell_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm == null || sender is not FrameworkElement fe) return;
        if (fe.DataContext is not SaleInvoiceLineRowViewModel rowVm) return;

        int rowIndex = vm.Lines.IndexOf(rowVm);
        if (rowIndex < 0) return;

        string colKey = fe.Tag as string ?? string.Empty;
        var cols = vm.IsWholesaleFormMode ? WholesaleCols : RetailCols;
        int colIdx = Array.IndexOf(cols, colKey);
        if (colIdx < 0) colIdx = 0;

        // Enter: Open modal if on product/batch without selection, else advance to next cell
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (colKey == "product" && !rowVm.HasProduct)
            {
                _ = vm.OpenProductSearchForLineAsync(rowVm);
                return;
            }
            if (colKey == "batch" && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _ = vm.OpenBatchSearchForLineAsync(rowVm);
                return;
            }

            if (colIdx + 1 < cols.Length)
            {
                FocusLineCellDeferred(rowIndex, cols[colIdx + 1]);
            }
            else if (rowIndex + 1 < vm.Lines.Count)
            {
                var nextRow = vm.Lines[rowIndex + 1];
                if (!nextRow.HasProduct)
                {
                    _ = vm.OpenProductSearchForLineAsync(nextRow);
                }
                else
                {
                    FocusLineCellDeferred(rowIndex + 1, cols[0]);
                }
            }
            else
            {
                // Last column of last row -> jump to Cash Given!
                FocusFooterFieldDeferred("cash_given");
            }
            return;
        }

        // Shift+Tab: Move backward across editable columns
        if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            e.Handled = true;
            if (colIdx - 1 >= 0)
            {
                FocusLineCellDeferred(rowIndex, cols[colIdx - 1]);
            }
            else if (rowIndex - 1 >= 0)
            {
                FocusLineCellDeferred(rowIndex - 1, cols[^1]);
            }
            else
            {
                FocusElementDeferred(CustomerSelectTriggerButton);
            }
            return;
        }

        // ArrowUp: Move to same column in previous row
        if (e.Key == Key.Up)
        {
            e.Handled = true;
            if (rowIndex > 0)
            {
                FocusLineCellDeferred(rowIndex - 1, colKey);
            }
            return;
        }

        // ArrowDown: Move to same column in next row, or append new row & open Product Search Modal!
        if (e.Key == Key.Down)
        {
            e.Handled = true;
            if (rowIndex + 1 < vm.Lines.Count)
            {
                FocusLineCellDeferred(rowIndex + 1, colKey);
            }
            else
            {
                _ = vm.AppendRowAndOpenProductModalAsync();
            }
        }
    }

    private void FooterField_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm == null || sender is not FrameworkElement fe) return;

        if (e.Key != Key.Enter) return;
        e.Handled = true;

        var tag = fe.Tag as string ?? string.Empty;
        switch (tag)
        {
            case "discount_percentage":
                FocusFooterFieldDeferred("discount_amount");
                break;
            case "discount_amount":
                FocusFooterFieldDeferred("tax_percentage");
                break;
            case "tax_percentage":
                FocusFooterFieldDeferred("tax_amount");
                break;
            case "tax_amount":
                FocusFooterFieldDeferred(vm.IsCreditInvoice ? "total_receive" : "cash_given");
                break;
            case "total_receive":
                FocusFooterFieldDeferred("cash_given");
                break;
        }
    }

    private void CashGivenTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm == null) return;

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = vm.SubmitInvoiceAsync();
        }
    }

    // =========================================================================
    // 5. FOCUS HELPERS FOR REPEATER CELLS & FOOTER
    // =========================================================================

    private void FocusLineCellDeferred(int rowIndex, string fieldTag)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            var vm = Vm;
            if (vm == null || rowIndex < 0 || rowIndex >= vm.Lines.Count) return;

            var itemsControl = vm.IsWholesaleFormMode ? WholesaleLinesItemsControl : RetailLinesItemsControl;
            if (itemsControl == null) return;

            itemsControl.UpdateLayout();
            var container = itemsControl.ItemContainerGenerator.ContainerFromIndex(rowIndex) as DependencyObject;
            if (container == null) return;

            var target = FindDescendantByTag(container, fieldTag);
            if (target is TextBox tb)
            {
                tb.Focus();
                Keyboard.Focus(tb);
                tb.SelectAll();
            }
            else if (target != null)
            {
                target.Focus();
                Keyboard.Focus(target);
            }
        }));
    }

    private void FocusFooterFieldDeferred(string fieldKey)
    {
        var vm = Vm;
        if (vm == null) return;

        bool isWs = vm.IsWholesaleFormMode;
        TextBox? target = fieldKey switch
        {
            "discount_percentage" => isWs ? WholesaleDiscountPctTextBox : RetailDiscountPctTextBox,
            "discount_amount" => isWs ? WholesaleDiscountAmtTextBox : RetailDiscountAmtTextBox,
            "tax_percentage" => isWs ? WholesaleTaxPctTextBox : RetailTaxPctTextBox,
            "tax_amount" => isWs ? WholesaleTaxAmtTextBox : RetailTaxAmtTextBox,
            "total_receive" => isWs ? WholesaleTotalReceiveTextBox : RetailTotalReceiveTextBox,
            "cash_given" => isWs ? WholesaleCashGivenTextBox : RetailCashGivenTextBox,
            _ => isWs ? WholesaleCashGivenTextBox : RetailCashGivenTextBox
        };

        FocusTextBoxDeferred(target);
    }

    private void FocusTextBoxDeferred(TextBox? textBox)
    {
        if (textBox == null) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            textBox.Focus();
            Keyboard.Focus(textBox);
            textBox.SelectAll();

            if (!textBox.IsKeyboardFocusWithin)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    textBox.Focus();
                    Keyboard.Focus(textBox);
                    textBox.SelectAll();
                }));
            }
        }));
    }

    private void FocusElementDeferred(UIElement? element)
    {
        if (element == null) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            element.Focus();
            Keyboard.Focus(element);
        }));
    }

    private static FrameworkElement? FindDescendantByTag(DependencyObject root, string tag)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && string.Equals(fe.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                return fe;
            }
            var nested = FindDescendantByTag(child, tag);
            if (nested != null) return nested;
        }
        return null;
    }

    // =========================================================================
    // 6. CUSTOMER SEARCH, PRODUCT SEARCH & BATCH SEARCH MODAL HANDLERS
    // =========================================================================

    private void CustomerSearchOverlay_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender))
        {
            Vm?.CloseCustomerSearchModal();
        }
    }

    private void CustomerModalSearchTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm == null) return;

        if (e.Key == Key.Down)
        {
            vm.MoveCustomerModalHighlight(1);
            CustomerModalResultsListBox.ScrollIntoView(vm.HighlightedModalCustomer);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            vm.MoveCustomerModalHighlight(-1);
            CustomerModalResultsListBox.ScrollIntoView(vm.HighlightedModalCustomer);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            vm.CommitCustomerModalSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CloseCustomerSearchModal();
            e.Handled = true;
        }
    }

    private void CustomerModalResultsListBox_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Vm == null) return;
        if (ItemsControl.ContainerFromElement(CustomerModalResultsListBox, e.OriginalSource as DependencyObject) is ListBoxItem item &&
            item.DataContext is SaleCustomerLookupDto customer)
        {
            Vm.CommitCustomerModalSelection(customer);
        }
    }

    private void BatchSearchOverlay_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender))
        {
            Vm?.CloseBatchSearchModal();
        }
    }

    private void BatchModalSearchTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm == null) return;

        if (e.Key == Key.Down)
        {
            vm.MoveBatchModalHighlight(1);
            BatchModalResultsListBox.ScrollIntoView(vm.HighlightedModalBatch);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            vm.MoveBatchModalHighlight(-1);
            BatchModalResultsListBox.ScrollIntoView(vm.HighlightedModalBatch);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            vm.CommitBatchModalSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CloseBatchSearchModal();
            e.Handled = true;
        }
    }

    private void BatchModalResultsListBox_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Vm == null) return;
        if (ItemsControl.ContainerFromElement(BatchModalResultsListBox, e.OriginalSource as DependencyObject) is ListBoxItem item &&
            item.DataContext is SaleProductBatchOptionDto batch)
        {
            Vm.CommitBatchModalSelection(batch);
        }
    }

    // =========================================================================
    // 7. DRAGGABLE & 8-WAY RESIZABLE PRODUCT SEARCH MODAL
    // =========================================================================

    private void ApplySavedProductSearchModalLayout()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            var vm = Vm;
            if (vm == null || ProductSearchDialogBorder == null || ProductSearchOverlayCanvas == null) return;

            double canvasW = ProductSearchOverlayCanvas.ActualWidth > 100 ? ProductSearchOverlayCanvas.ActualWidth : ActualWidth;
            double canvasH = ProductSearchOverlayCanvas.ActualHeight > 100 ? ProductSearchOverlayCanvas.ActualHeight : ActualHeight;

            var layout = vm.ProductSearchModalLayout;
            double w = Math.Clamp(layout.Width > 0 ? layout.Width : 960, 640, Math.Max(640, canvasW - 24));
            double h = Math.Clamp(layout.Height > 0 ? layout.Height : 600, 420, Math.Max(420, canvasH - 24));

            double left = layout.X ?? Math.Max(16, (canvasW - w) / 2.0);
            double top = layout.Y ?? Math.Max(16, (canvasH - h) / 2.0);

            left = Math.Clamp(left, 8, Math.Max(8, canvasW - w - 8));
            top = Math.Clamp(top, 8, Math.Max(8, canvasH - h - 8));

            ProductSearchDialogBorder.Width = w;
            ProductSearchDialogBorder.Height = h;
            Canvas.SetLeft(ProductSearchDialogBorder, left);
            Canvas.SetTop(ProductSearchDialogBorder, top);
        }));
    }

    private void ProductSearchOverlayCanvas_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Vm?.IsProductSearchModalOpen == true)
        {
            ApplySavedProductSearchModalLayout();
        }
    }

    private void ProductSearchOverlay_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, ProductSearchOverlayGrid) ||
            ReferenceEquals(e.OriginalSource, ProductSearchOverlayCanvas))
        {
            Vm?.CloseProductSearchModal();
        }
    }

    private void ProductModalSearchTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm == null) return;

        if (e.Key == Key.Down)
        {
            vm.MoveProductModalHighlight(1);
            ProductModalResultsListBox.ScrollIntoView(vm.HighlightedModalProduct);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            vm.MoveProductModalHighlight(-1);
            ProductModalResultsListBox.ScrollIntoView(vm.HighlightedModalProduct);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            vm.CommitProductModalSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CloseProductSearchModal();
            e.Handled = true;
        }
    }

    private void ProductModalResultsListBox_OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(ProductModalResultsListBox);
        if ((pos - _lastProductListMousePos).Length < 2.0) return;
        _lastProductListMousePos = pos;

        if (ItemsControl.ContainerFromElement(ProductModalResultsListBox, e.OriginalSource as DependencyObject) is ListBoxItem item &&
            item.DataContext is SaleProductSearchItemDto prod &&
            Vm != null)
        {
            Vm.HighlightedModalProduct = prod;
        }
    }

    private void ProductModalResultsListBox_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Vm == null) return;
        if (ItemsControl.ContainerFromElement(ProductModalResultsListBox, e.OriginalSource as DependencyObject) is ListBoxItem item &&
            item.DataContext is SaleProductSearchItemDto prod)
        {
            Vm.CommitProductModalSelection(prod);
        }
    }

    private void ProductSearchHeader_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep &&
            (FindAncestor<TextBox>(dep) != null || FindAncestor<Button>(dep) != null))
        {
            return;
        }

        _isDraggingProductModal = true;
        _productModalDragStartMouse = e.GetPosition(ProductSearchOverlayCanvas);
        _productModalDragStartLeft = Canvas.GetLeft(ProductSearchDialogBorder);
        _productModalDragStartTop = Canvas.GetTop(ProductSearchDialogBorder);
        ProductSearchHeaderBorder.CaptureMouse();
        e.Handled = true;
    }

    private void ProductSearchHeader_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingProductModal) return;

        var cur = e.GetPosition(ProductSearchOverlayCanvas);
        double dx = cur.X - _productModalDragStartMouse.X;
        double dy = cur.Y - _productModalDragStartMouse.Y;

        double canvasW = Math.Max(680, ProductSearchOverlayCanvas.ActualWidth);
        double canvasH = Math.Max(460, ProductSearchOverlayCanvas.ActualHeight);

        double newLeft = Math.Clamp(_productModalDragStartLeft + dx, 8, Math.Max(8, canvasW - ProductSearchDialogBorder.Width - 8));
        double newTop = Math.Clamp(_productModalDragStartTop + dy, 8, Math.Max(8, canvasH - ProductSearchDialogBorder.Height - 8));

        Canvas.SetLeft(ProductSearchDialogBorder, newLeft);
        Canvas.SetTop(ProductSearchDialogBorder, newTop);
    }

    private void ProductSearchHeader_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndProductModalDrag();
    }

    private void ProductSearchHeader_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        EndProductModalDrag();
    }

    private void EndProductModalDrag()
    {
        if (!_isDraggingProductModal) return;
        _isDraggingProductModal = false;
        ProductSearchHeaderBorder.ReleaseMouseCapture();
        PersistProductSearchModalBounds();
    }

    private void ProductSearchResizeHandle_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border handle) return;
        _isResizingProductModal = true;
        _productModalResizeDir = handle.Tag as string ?? "se";
        _productModalResizeStartMouse = e.GetPosition(ProductSearchOverlayCanvas);
        _productModalResizeStartLeft = Canvas.GetLeft(ProductSearchDialogBorder);
        _productModalResizeStartTop = Canvas.GetTop(ProductSearchDialogBorder);
        _productModalResizeStartWidth = ProductSearchDialogBorder.Width;
        _productModalResizeStartHeight = ProductSearchDialogBorder.Height;
        handle.CaptureMouse();
        e.Handled = true;
    }

    private void ProductSearchResizeHandle_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isResizingProductModal) return;

        var cur = e.GetPosition(ProductSearchOverlayCanvas);
        double dx = cur.X - _productModalResizeStartMouse.X;
        double dy = cur.Y - _productModalResizeStartMouse.Y;

        double minW = ProductSearchDialogBorder.MinWidth;
        double minH = ProductSearchDialogBorder.MinHeight;

        double left = _productModalResizeStartLeft;
        double top = _productModalResizeStartTop;
        double width = _productModalResizeStartWidth;
        double height = _productModalResizeStartHeight;

        if (_productModalResizeDir.Contains('e'))
            width = Math.Max(minW, _productModalResizeStartWidth + dx);
        if (_productModalResizeDir.Contains('s'))
            height = Math.Max(minH, _productModalResizeStartHeight + dy);
        if (_productModalResizeDir.Contains('w'))
        {
            double proposedW = _productModalResizeStartWidth - dx;
            if (proposedW >= minW)
            {
                width = proposedW;
                left = _productModalResizeStartLeft + dx;
            }
        }
        if (_productModalResizeDir.Contains('n'))
        {
            double proposedH = _productModalResizeStartHeight - dy;
            if (proposedH >= minH)
            {
                height = proposedH;
                top = _productModalResizeStartTop + dy;
            }
        }

        ProductSearchDialogBorder.Width = width;
        ProductSearchDialogBorder.Height = height;
        Canvas.SetLeft(ProductSearchDialogBorder, left);
        Canvas.SetTop(ProductSearchDialogBorder, top);
    }

    private void ProductSearchResizeHandle_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border handle)
            handle.ReleaseMouseCapture();
        EndProductModalResize();
    }

    private void ProductSearchResizeHandle_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        EndProductModalResize();
    }

    private void EndProductModalResize()
    {
        if (!_isResizingProductModal) return;
        _isResizingProductModal = false;
        PersistProductSearchModalBounds();
    }

    private void PersistProductSearchModalBounds()
    {
        if (Vm == null) return;
        double left = Canvas.GetLeft(ProductSearchDialogBorder);
        double top = Canvas.GetTop(ProductSearchDialogBorder);
        double width = ProductSearchDialogBorder.Width;
        double height = ProductSearchDialogBorder.Height;
        _ = Vm.SaveProductSearchModalLayoutAsync(left, top, width, height);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    // =========================================================================
    // 8. NATIVE WPF PRINT DOCUMENT (THERMAL 80MM & A4 FULL SHEET)
    // =========================================================================

    private void PrintCurrentDetailDocument()
    {
        var vm = Vm;
        var detail = vm?.CurrentDetail;
        if (vm == null || detail == null) return;

        try
        {
            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true) return;

            bool isThermal = vm.IsPrintThermalMode;
            var doc = new FlowDocument
            {
                PageWidth = isThermal ? 280 : 760,
                PagePadding = isThermal ? new Thickness(10, 12, 10, 12) : new Thickness(32, 28, 32, 28),
                ColumnWidth = double.PositiveInfinity,
                FontFamily = new FontFamily("Segoe UI")
            };

            // Store Header
            var storeTitle = new Paragraph(new Run(vm.StoreSettings.StoreName))
            {
                FontSize = isThermal ? 14 : 18,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2)
            };
            doc.Blocks.Add(storeTitle);

            var metaPara = new Paragraph(new Run(
                $"{vm.StoreSettings.StoreAddress}\nTel: {vm.StoreSettings.StorePhone}\nInvoice: {detail.PostedNumber} • Date: {detail.FormattedDate}\nCustomer: {detail.CustomerName}"))
            {
                FontSize = isThermal ? 9.5 : 11,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            doc.Blocks.Add(metaPara);

            // Items Table
            var table = new Table { CellSpacing = 0 };
            table.Columns.Add(new TableColumn { Width = new GridLength(2.6, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(0.9, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(1.1, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(1.3, GridUnitType.Star) });

            var rg = new TableRowGroup();
            var headerRow = new TableRow { Background = Brushes.WhiteSmoke };
            headerRow.Cells.Add(CreateCell("Item", true, TextAlignment.Left, isThermal));
            headerRow.Cells.Add(CreateCell("Qty", true, TextAlignment.Center, isThermal));
            headerRow.Cells.Add(CreateCell("Price", true, TextAlignment.Right, isThermal));
            headerRow.Cells.Add(CreateCell("Total", true, TextAlignment.Right, isThermal));
            rg.Rows.Add(headerRow);

            foreach (var item in detail.Items)
            {
                var r = new TableRow();
                r.Cells.Add(CreateCell(item.ProductName, false, TextAlignment.Left, isThermal));
                r.Cells.Add(CreateCell(item.TotalUnitsDisplay, false, TextAlignment.Center, isThermal));
                r.Cells.Add(CreateCell($"{item.Price:N2}", false, TextAlignment.Right, isThermal));
                r.Cells.Add(CreateCell($"{item.SubTotal:N2}", true, TextAlignment.Right, isThermal));
                rg.Rows.Add(r);
            }

            table.RowGroups.Add(rg);
            doc.Blocks.Add(table);

            // Totals Summary
            var totalsPara = new Paragraph(new Run(
                $"Gross: Rs. {detail.GrossAmount:N2}\n" +
                $"Discount: Rs. {(detail.ItemDiscount + detail.DiscountAmount):N2}\n" +
                $"NET TOTAL: Rs. {detail.Total:N0}\n" +
                $"Received: Rs. {detail.TotalReceive:N0} • Balance Due: Rs. {detail.RemainThisBill:N0}"))
            {
                FontSize = isThermal ? 10 : 12,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(0, 10, 0, 8)
            };
            doc.Blocks.Add(totalsPara);

            if (!string.IsNullOrWhiteSpace(vm.StoreSettings.SaleNote))
            {
                doc.Blocks.Add(new Paragraph(new Run(vm.StoreSettings.SaleNote))
                {
                    FontSize = isThermal ? 8.5 : 10,
                    TextAlignment = TextAlignment.Center,
                    Foreground = Brushes.DimGray
                });
            }

            IDocumentPaginatorSource idp = doc;
            dlg.PrintDocument(idp.DocumentPaginator, $"Sale Invoice {detail.PostedNumber}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to print invoice: {ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PrintCurrentDeliveryChallanDocument()
    {
        var vm = Vm;
        var detail = vm?.CurrentDetail;
        if (vm == null || detail == null) return;

        try
        {
            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true) return;

            var doc = new FlowDocument
            {
                PageWidth = 760,
                PagePadding = new Thickness(32, 28, 32, 28),
                ColumnWidth = double.PositiveInfinity,
                FontFamily = new FontFamily("Segoe UI")
            };

            doc.Blocks.Add(new Paragraph(new Run(vm.StoreSettings.StoreName))
            {
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2)
            });

            doc.Blocks.Add(new Paragraph(new Run(
                $"DELIVERY CHALLAN / GATE PASS\n" +
                $"{vm.StoreSettings.StoreAddress} • Tel: {vm.StoreSettings.StorePhone}\n" +
                $"Challan / Invoice #: {detail.PostedNumber} • Date: {detail.FormattedDate}\n" +
                $"Customer: {detail.CustomerName}"))
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var table = new Table { CellSpacing = 0 };
            table.Columns.Add(new TableColumn { Width = new GridLength(0.5, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(2.6, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(0.9, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(1.1, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(1.0, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(1.1, GridUnitType.Star) });

            var rg = new TableRowGroup();
            var headerRow = new TableRow { Background = Brushes.WhiteSmoke };
            headerRow.Cells.Add(CreateCell("#", true, TextAlignment.Center, false));
            headerRow.Cells.Add(CreateCell("Product Description", true, TextAlignment.Left, false));
            headerRow.Cells.Add(CreateCell("Pack Size", true, TextAlignment.Center, false));
            headerRow.Cells.Add(CreateCell("Batch", true, TextAlignment.Left, false));
            headerRow.Cells.Add(CreateCell("Expiry", true, TextAlignment.Center, false));
            headerRow.Cells.Add(CreateCell("Total Units", true, TextAlignment.Right, false));
            rg.Rows.Add(headerRow);

            foreach (var item in detail.Items)
            {
                var r = new TableRow();
                r.Cells.Add(CreateCell(item.RowNumber.ToString(), false, TextAlignment.Center, false));
                r.Cells.Add(CreateCell(item.ProductName, false, TextAlignment.Left, false));
                r.Cells.Add(CreateCell($"1x{item.PackSize}", false, TextAlignment.Center, false));
                r.Cells.Add(CreateCell(item.BatchDisplay, false, TextAlignment.Left, false));
                r.Cells.Add(CreateCell(item.FormattedExpiry, false, TextAlignment.Center, false));
                r.Cells.Add(CreateCell(item.TotalUnitsDisplay, true, TextAlignment.Right, false));
                rg.Rows.Add(r);
            }

            table.RowGroups.Add(rg);
            doc.Blocks.Add(table);

            doc.Blocks.Add(new Paragraph(new Run(
                $"\nTotal Items: {detail.TotalItemsCount}   •   Total Billed Units: {detail.TotalBilledUnits:N0}   •   Bonus Units: {detail.TotalBonusUnits:N0}\n\n" +
                "Prepared / Dispatched By: _______________________          Received By: _______________________"))
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 16, 0, 8)
            });

            IDocumentPaginatorSource idp = doc;
            dlg.PrintDocument(idp.DocumentPaginator, $"Delivery Challan {detail.PostedNumber}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to print Delivery Challan: {ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // =========================================================================
    // 9. SHOW PAGE SEARCH MODAL & DELETE PASSWORD HANDLERS
    // =========================================================================

    private void ShowInvoiceSearchOverlay_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender))
        {
            Vm?.CloseShowInvoiceSearchModal();
        }
    }

    private void ShowInvoiceSearchTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm == null) return;

        if (e.Key == Key.Down)
        {
            vm.MoveShowInvoiceSearchHighlight(1);
            ShowInvoiceSearchResultsListBox.ScrollIntoView(vm.HighlightedShowSearchInvoice);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            vm.MoveShowInvoiceSearchHighlight(-1);
            ShowInvoiceSearchResultsListBox.ScrollIntoView(vm.HighlightedShowSearchInvoice);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            _ = vm.CommitShowInvoiceSearchSelectionAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CloseShowInvoiceSearchModal();
            e.Handled = true;
        }
    }

    private void ShowInvoiceSearchResultsListBox_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Vm == null) return;
        if (ItemsControl.ContainerFromElement(ShowInvoiceSearchResultsListBox, e.OriginalSource as DependencyObject) is ListBoxItem item &&
            item.DataContext is SaleInvoiceDirectoryRowDto row)
        {
            _ = Vm.CommitShowInvoiceSearchSelectionAsync(row);
        }
    }

    private void DeleteInvoicePasswordTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm == null) return;

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = vm.ConfirmDeleteInvoiceAsync();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            vm.CloseDeleteModal();
        }
    }

    private static TableCell CreateCell(string text, bool bold, TextAlignment align, bool isThermal)
    {
        return new TableCell(new Paragraph(new Run(text))
        {
            Margin = new Thickness(2, 2, 2, 2),
            TextAlignment = align,
            FontSize = isThermal ? 9 : 10.5,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal
        });
    }
}
