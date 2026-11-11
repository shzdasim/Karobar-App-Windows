using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Karobar.Application.Interfaces;
using Karobar.Desktop.Core.Navigation;
using Karobar.Desktop.ViewModels;

namespace Karobar.Desktop.Views;

public partial class PurchaseReturnsView : UserControl, IPageKeyboardHandler
{
    private PurchaseReturnsViewModel? _wiredVm;

    private sealed record ModalResizeState(
        string Direction,
        double StartX,
        double StartY,
        double StartWidth,
        double StartHeight,
        double StartLeft,
        double StartTop);

    private bool _isDraggingProductSearch;
    private Point _productSearchDragOffset;
    private ModalResizeState? _productSearchResizeState;
    private Point _lastProductModalMousePos;

    public PurchaseReturnsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private PurchaseReturnsViewModel? Vm => DataContext as PurchaseReturnsViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_wiredVm != null)
        {
            _wiredVm.RequestFocusField -= OnRequestFocusField;
            _wiredVm.RequestPrintDocument -= OnRequestPrintDocument;
            _wiredVm.RequestApplyProductSearchModalLayout = null;
            _wiredVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _wiredVm = e.NewValue as PurchaseReturnsViewModel;

        if (_wiredVm != null)
        {
            _wiredVm.RequestFocusField += OnRequestFocusField;
            _wiredVm.RequestPrintDocument += OnRequestPrintDocument;
            _wiredVm.RequestApplyProductSearchModalLayout = () =>
                Dispatcher.BeginInvoke(ApplyProductSearchModalLayout, DispatcherPriority.Loaded);
            _wiredVm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_wiredVm == null) return;

        if (e.PropertyName == nameof(PurchaseReturnsViewModel.IsSupplierSearchModalOpen) && _wiredVm.IsSupplierSearchModalOpen)
        {
            FocusAndPlaceCaretAtEnd(SupplierModalSearchTextBox);
        }
        else if (e.PropertyName == nameof(PurchaseReturnsViewModel.IsInvoiceSearchModalOpen) && _wiredVm.IsInvoiceSearchModalOpen)
        {
            FocusAndPlaceCaretAtEnd(InvoiceModalSearchTextBox);
        }
        else if (e.PropertyName == nameof(PurchaseReturnsViewModel.IsProductSearchModalOpen) && _wiredVm.IsProductSearchModalOpen)
        {
            Dispatcher.BeginInvoke(() =>
            {
                ApplyProductSearchModalLayout();
                FocusAndPlaceCaretAtEnd(ProductModalSearchTextBox);
            }, DispatcherPriority.Input);
        }
        else if (e.PropertyName == nameof(PurchaseReturnsViewModel.IsDeleteModalOpen) && _wiredVm.IsDeleteModalOpen)
        {
            Dispatcher.BeginInvoke(() =>
            {
                DeletePasswordBox.Clear();
                DeletePasswordBox.Focus();
                Keyboard.Focus(DeletePasswordBox);
            }, DispatcherPriority.Input);
        }
    }

    // =========================================================================
    // 1. DATE PICKER dd-MM-yyyy WATERMARK & NUMERIC FOCUS HELPERS
    // =========================================================================

    private void AnyDatePicker_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is DatePicker dp)
        {
            dp.ApplyTemplate();
            if (dp.Template?.FindName("PART_TextBox", dp) is DatePickerTextBox dpTextBox)
            {
                dpTextBox.ApplyTemplate();
                if (dpTextBox.Template?.FindName("PART_Watermark", dpTextBox) is ContentControl watermark)
                {
                    watermark.Content = "dd-MM-yyyy";
                }
            }
        }
    }

    private void NumericTextBox_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            var idx = GetRowIndexFromElement(tb);
            if (idx >= 0 && Vm != null)
                Vm.ActiveRowIndex = idx;
            tb.SelectAll();
        }
    }

    private void NumericTextBox_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox tb && !tb.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            tb.Focus();
            tb.SelectAll();
        }
    }

    private void LineCellTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PurchaseReturnLineEditorViewModel row })
        {
            row.NormalizeDisplayTexts();
        }
    }

    private void DeletePasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (Vm != null && sender is PasswordBox pb)
        {
            Vm.DeletePassword = pb.Password;
        }
    }

    private async void DeletePasswordBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None &&
            Vm is { IsDeleteModalOpen: true })
        {
            e.Handled = true;
            await Vm.ConfirmDeleteReturnAsync();
        }
    }

    // =========================================================================
    // 2. IPageKeyboardHandler IMPLEMENTATION
    // =========================================================================

    public bool HandleNewShortcut()
    {
        if (Vm == null) return false;
        if (Vm.IsDirectoryMode)
        {
            _ = Vm.OpenCreateFormAsync();
            return true;
        }

        if (Vm.IsFormMode)
        {
            Vm.AddRow();
            return true;
        }

        return false;
    }

    public bool HandleSaveShortcut()
    {
        if (Vm == null) return false;
        if (Vm.IsDeleteModalOpen)
        {
            _ = Vm.ConfirmDeleteReturnAsync();
            return true;
        }

        if (Vm.IsFormMode)
        {
            _ = Vm.SaveReturnAsync();
            return true;
        }

        return false;
    }

    public bool HandleSearchShortcut()
    {
        if (Vm == null) return false;
        if (Vm.IsDirectoryMode)
        {
            FocusAndSelectTextBox(DirectorySearchPostedBox);
            return true;
        }

        if (Vm.IsFormMode)
        {
            _ = Vm.OpenProductSearchForRowAsync(Vm.ActiveRowIndex);
            return true;
        }

        return false;
    }

    public bool HandleEscapeShortcut()
    {
        if (Vm == null) return false;

        if (Vm.IsPreShiftModalOpen)
        {
            Vm.ClosePreShiftModal();
            return true;
        }
        if (Vm.IsDeleteModalOpen)
        {
            Vm.CloseDeleteModal();
            return true;
        }
        if (Vm.IsHistoryModalOpen)
        {
            Vm.CloseHistoryModal();
            return true;
        }
        if (Vm.IsDetailModalOpen)
        {
            Vm.CloseDetailModal();
            return true;
        }
        if (Vm.IsBatchSearchModalOpen)
        {
            Vm.CloseBatchSearchModal();
            return true;
        }
        if (Vm.IsProductSearchModalOpen)
        {
            Vm.CloseProductSearchModal();
            return true;
        }
        if (Vm.IsInvoiceSearchModalOpen)
        {
            Vm.CloseInvoiceSearchModal();
            return true;
        }
        if (Vm.IsSupplierSearchModalOpen)
        {
            Vm.CloseSupplierSearchModal();
            return true;
        }
        if (Vm.IsFormMode)
        {
            _ = Vm.CancelFormAsync();
            return true;
        }
        if (Vm.IsDirectoryMode && (!string.IsNullOrEmpty(Vm.FilterPostedNumber) || !string.IsNullOrEmpty(Vm.FilterSupplierSearch)))
        {
            _ = Vm.ResetDirectoryFiltersAsync();
            return true;
        }

        return false;
    }

    public bool HandleRefreshShortcut()
    {
        if (Vm == null) return false;
        if (Vm.IsDirectoryMode)
        {
            _ = Vm.RefreshDirectoryAsync();
            return true;
        }

        return false;
    }

    public bool HandleImportShortcut() => false;

    public bool HandleExportShortcut() => false;

    // =========================================================================
    // 3. HEADER & PAGE-LEVEL KEYBOARD NAVIGATION
    // =========================================================================

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var isAlt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
        var isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        if (Vm.IsFormMode)
        {
            if (isAlt && key == Key.S)
            {
                e.Handled = true;
                _ = Vm.SaveReturnAsync();
                return;
            }

            if (isAlt && key == Key.P)
            {
                e.Handled = true;
                _ = Vm.SaveAndPrintReturnAsync();
                return;
            }

            if (isAlt && key == Key.A)
            {
                e.Handled = true;
                Vm.AddRow();
                return;
            }

            if (isAlt && key == Key.U)
            {
                e.Handled = true;
                Vm.OpenSupplierSearchModal();
                return;
            }

            if (isAlt && key == Key.I)
            {
                e.Handled = true;
                _ = Vm.OpenInvoiceSearchModalAsync();
                return;
            }

            if (key == Key.F2)
            {
                e.Handled = true;
                _ = Vm.OpenProductSearchForRowAsync(Vm.ActiveRowIndex);
                return;
            }

            if (key == Key.F3)
            {
                e.Handled = true;
                OnRequestFocusField("batch", Vm.ActiveRowIndex);
                return;
            }
        }
        else if (Vm.IsDirectoryMode && isCtrl && key == Key.N)
        {
            e.Handled = true;
            _ = Vm.OpenCreateFormAsync();
        }
    }

    private void SupplierTriggerButton_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null) return;

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None && (e.Key == Key.Enter || e.Key == Key.Space || e.Key == Key.Down))
        {
            e.Handled = true;
            Vm.OpenSupplierSearchModal();
            return;
        }

        var seed = TryMapKeyToSeedChar(e.Key, mods);
        if (seed != null)
        {
            e.Handled = true;
            Vm.OpenSupplierSearchModal(seed);
        }
    }

    private async void InvoiceTriggerButton_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null) return;

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None && (e.Key == Key.Enter || e.Key == Key.Space || e.Key == Key.Down))
        {
            e.Handled = true;
            await Vm.OpenInvoiceSearchModalAsync();
            return;
        }

        // Pressing Tab on InvoiceTriggerButton moves to Remarks or Row 0 Product
        var seed = TryMapKeyToSeedChar(e.Key, mods);
        if (seed != null)
        {
            e.Handled = true;
            await Vm.OpenInvoiceSearchModalAsync();
            Vm.InvoiceSearchQuery = seed;
        }
    }

    private async void RemarksTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null) return;

        if (Keyboard.Modifiers == ModifierKeys.None && (e.Key == Key.Enter || e.Key == Key.Tab))
        {
            e.Handled = true;
            if (Vm.Lines.Count == 0)
                Vm.AddRow();

            if (!Vm.Lines[0].HasProduct)
                await Vm.OpenProductSearchForRowAsync(0);
            else
                OnRequestFocusField("product", 0);
        }
    }

    // =========================================================================
    // 4. LINE-ITEM REPEATER KEYBOARD CHAIN (Enter & ArrowUp / ArrowDown)
    // =========================================================================

    private int GetRowIndexFromElement(FrameworkElement? el)
    {
        if (Vm == null || el?.DataContext is not PurchaseReturnLineEditorViewModel lineVm)
            return -1;
        return Vm.Lines.IndexOf(lineVm);
    }

    private async void ProductCellButton_Click(object sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var rowIndex = GetRowIndexFromElement(sender as FrameworkElement);
        if (rowIndex >= 0)
        {
            await Vm.OpenProductSearchForRowAsync(rowIndex);
        }
    }

    private async void ProductCellButton_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null) return;
        var rowIndex = GetRowIndexFromElement(sender as FrameworkElement);
        if (rowIndex < 0) return;
        Vm.ActiveRowIndex = rowIndex;
        var row = Vm.Lines[rowIndex];
        var mods = Keyboard.Modifiers;

        if (mods == ModifierKeys.None && e.Key == Key.Enter)
        {
            e.Handled = true;
            if (!row.HasProduct)
            {
                await Vm.OpenProductSearchForRowAsync(rowIndex);
            }
            else
            {
                var nextCol = row.RowBatches.Count > 1 ? "batch" : "pack_quantity";
                OnRequestFocusField(nextCol, rowIndex);
            }
            return;
        }

        // Pressing Tab on an empty trailing row jumps cleanly to Footer Discount %
        if (mods == ModifierKeys.None && e.Key == Key.Tab && !row.HasProduct && Vm.ActiveProductLinesCount > 0)
        {
            e.Handled = true;
            FocusAndSelectTextBox(FooterDiscountPctTextBox);
            return;
        }

        if (mods == ModifierKeys.None && e.Key == Key.Up && rowIndex > 0)
        {
            e.Handled = true;
            OnRequestFocusField("product", rowIndex - 1);
            return;
        }

        if (mods == ModifierKeys.None && e.Key == Key.Down)
        {
            e.Handled = true;
            if (rowIndex + 1 < Vm.Lines.Count)
            {
                var nextRow = Vm.Lines[rowIndex + 1];
                if (!nextRow.HasProduct)
                    await Vm.OpenProductSearchForRowAsync(rowIndex + 1);
                else
                    OnRequestFocusField("product", rowIndex + 1);
            }
            else
            {
                Vm.AddRow();
                await Vm.OpenProductSearchForRowAsync(Vm.Lines.Count - 1);
            }
            return;
        }

        // Typing any alphanumeric character on the Product trigger opens the Product Search Modal seeded with that character!
        var seed = TryMapKeyToSeedChar(e.Key, mods);
        if (seed != null)
        {
            e.Handled = true;
            await Vm.OpenProductSearchForRowAsync(rowIndex, seed);
        }
    }

    private void LineBatchSelect_OnEnterCommitted(object? sender, EventArgs e)
    {
        if (Vm == null || sender is not FrameworkElement fe)
            return;

        var rowIndex = GetRowIndexFromElement(fe);
        if (rowIndex < 0)
            return;

        OnRequestFocusField("pack_quantity", rowIndex);
    }

    private async void LineTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null || sender is not TextBox tb) return;
        var rowIndex = GetRowIndexFromElement(tb);
        if (rowIndex < 0) return;
        var row = Vm.Lines[rowIndex];
        var mods = Keyboard.Modifiers;
        var fieldTag = tb.Tag as string ?? string.Empty;

        // Sequential Enter Chain matching PurchaseReturnForm.jsx lines 1251-1264:
        // pack_quantity -> pack_purchase_price -> item_discount -> next row product
        if (mods == ModifierKeys.None && e.Key == Key.Enter)
        {
            e.Handled = true;
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            row.NormalizeDisplayTexts();

            if (fieldTag == "pack_quantity" || fieldTag == "unit_quantity")
            {
                OnRequestFocusField("pack_purchase_price", rowIndex);
            }
            else if (fieldTag == "pack_purchase_price" || fieldTag == "unit_purchase_price")
            {
                OnRequestFocusField("item_discount", rowIndex);
            }
            else if (fieldTag == "item_discount")
            {
                if (rowIndex + 1 < Vm.Lines.Count)
                {
                    var nextRow = Vm.Lines[rowIndex + 1];
                    if (!nextRow.HasProduct)
                        await Vm.OpenProductSearchForRowAsync(rowIndex + 1);
                    else
                        OnRequestFocusField("product", rowIndex + 1);
                }
                else
                {
                    Vm.AddRow();
                    await Vm.OpenProductSearchForRowAsync(Vm.Lines.Count - 1);
                }
            }
            return;
        }

        if (mods == ModifierKeys.None && e.Key == Key.Down)
        {
            e.Handled = true;
            if (rowIndex + 1 < Vm.Lines.Count)
            {
                var nextRow = Vm.Lines[rowIndex + 1];
                if (!nextRow.HasProduct)
                    await Vm.OpenProductSearchForRowAsync(rowIndex + 1);
                else
                    OnRequestFocusField(fieldTag, rowIndex + 1);
            }
            else
            {
                Vm.AddRow();
                await Vm.OpenProductSearchForRowAsync(Vm.Lines.Count - 1);
            }
            return;
        }

        if (mods == ModifierKeys.None && e.Key == Key.Up && rowIndex > 0)
        {
            e.Handled = true;
            var targetTag = Vm.Lines[rowIndex - 1].HasProduct ? fieldTag : "product";
            OnRequestFocusField(targetTag, rowIndex - 1);
        }
    }

    private void FooterDiscountPctTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            FocusAndSelectTextBox(FooterTaxPctTextBox);
        }
    }

    private void FooterTaxPctTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            SaveReturnFooterButton?.Focus();
        }
    }

    private void OnRequestFocusField(string fieldTag, int rowIndex)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (Vm == null) return;

            if (string.Equals(fieldTag, "supplier_trigger", StringComparison.OrdinalIgnoreCase))
            {
                SupplierTriggerButton?.Focus();
                if (SupplierTriggerButton != null)
                    Keyboard.Focus(SupplierTriggerButton);
                return;
            }

            if (string.Equals(fieldTag, "invoice_trigger", StringComparison.OrdinalIgnoreCase))
            {
                InvoiceTriggerButton?.Focus();
                if (InvoiceTriggerButton != null)
                    Keyboard.Focus(InvoiceTriggerButton);
                return;
            }

            if (LinesItemsControl == null || rowIndex < 0 || rowIndex >= LinesItemsControl.Items.Count)
                return;

            LinesItemsControl.UpdateLayout();
            var container = LinesItemsControl.ItemContainerGenerator.ContainerFromIndex(rowIndex) as DependencyObject;
            if (container == null)
                return;

            var target = FindChildByTag(container, fieldTag);
            if (target == null)
                return;

            target.BringIntoView();

            if (target is SearchableSelect ss)
            {
                ss.FocusSearchInput();
            }
            else if (target is TextBox tb)
            {
                tb.Focus();
                Keyboard.Focus(tb);
                tb.SelectAll();
            }
            else if (target is UIElement ui)
            {
                ui.Focus();
                Keyboard.Focus(ui);
            }
        }, DispatcherPriority.Input);
    }

    private static FrameworkElement? FindChildByTag(DependencyObject parent, string tag)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement fe && string.Equals(fe.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
                return fe;

            var nested = FindChildByTag(child, tag);
            if (nested != null)
                return nested;
        }
        return null;
    }

    // =========================================================================
    // 5. SUPPLIER & INVOICE SEARCH MODAL HANDLERS
    // =========================================================================

    private void SupplierModalSearchTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null) return;

        if (e.Key == Key.Down)
        {
            e.Handled = true;
            Vm.MoveSupplierModalHighlight(1);
            if (Vm.SelectedSupplierInModal != null)
                SupplierModalResultsListBox.ScrollIntoView(Vm.SelectedSupplierInModal);
        }
        else if (e.Key == Key.Up)
        {
            e.Handled = true;
            Vm.MoveSupplierModalHighlight(-1);
            if (Vm.SelectedSupplierInModal != null)
                SupplierModalResultsListBox.ScrollIntoView(Vm.SelectedSupplierInModal);
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Vm.SelectSupplierFromModal(Vm.SelectedSupplierInModal);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Vm.CloseSupplierSearchModal();
            Dispatcher.BeginInvoke(() => SupplierTriggerButton?.Focus(), DispatcherPriority.Input);
        }
    }

    private void SupplierModalResultsListBox_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Vm == null) return;

        if (ItemsControl.ContainerFromElement(SupplierModalResultsListBox, e.OriginalSource as DependencyObject) is ListBoxItem lbi &&
            lbi.DataContext is PurchaseSupplierLookupDto sup)
        {
            Vm.SelectSupplierFromModal(sup);
        }
    }

    private async void InvoiceModalSearchTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null) return;

        if (e.Key == Key.Down)
        {
            e.Handled = true;
            Vm.MoveInvoiceModalHighlight(1);
            if (Vm.SelectedInvoiceInModal != null)
                InvoiceModalResultsListBox.ScrollIntoView(Vm.SelectedInvoiceInModal);
        }
        else if (e.Key == Key.Up)
        {
            e.Handled = true;
            Vm.MoveInvoiceModalHighlight(-1);
            if (Vm.SelectedInvoiceInModal != null)
                InvoiceModalResultsListBox.ScrollIntoView(Vm.SelectedInvoiceInModal);
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await Vm.SelectInvoiceFromModalAsync(Vm.SelectedInvoiceInModal);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Vm.CloseInvoiceSearchModal();
            Dispatcher.BeginInvoke(() => InvoiceTriggerButton?.Focus(), DispatcherPriority.Input);
        }
    }

    private async void InvoiceModalResultsListBox_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Vm == null) return;

        if (ItemsControl.ContainerFromElement(InvoiceModalResultsListBox, e.OriginalSource as DependencyObject) is ListBoxItem lbi &&
            lbi.DataContext is PurchaseReturnInvoiceLookupDto inv)
        {
            await Vm.SelectInvoiceFromModalAsync(inv);
        }
    }

    // =========================================================================
    // 6. DRAGGABLE & 8-WAY RESIZABLE PRODUCT SEARCH MODAL HANDLERS
    //    (Exact ProductSearchInput.jsx parity)
    // =========================================================================

    private async void ProductModalSearchTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null) return;

        if (e.Key == Key.Down)
        {
            e.Handled = true;
            Vm.MoveProductSearchHighlight(1);
            if (Vm.SelectedProductInModal != null)
                ProductModalResultsListBox.ScrollIntoView(Vm.SelectedProductInModal);
        }
        else if (e.Key == Key.Up)
        {
            e.Handled = true;
            Vm.MoveProductSearchHighlight(-1);
            if (Vm.SelectedProductInModal != null)
                ProductModalResultsListBox.ScrollIntoView(Vm.SelectedProductInModal);
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await Vm.SelectProductFromModalAsync(Vm.SelectedProductInModal);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            var targetRowIdx = Vm.ProductSearchTargetRowIndex;
            var wasTrailingEmpty = targetRowIdx >= 0 &&
                                   targetRowIdx < Vm.Lines.Count &&
                                   !Vm.Lines[targetRowIdx].HasProduct &&
                                   Vm.ActiveProductLinesCount > 0;
            Vm.CloseProductSearchModal();
            if (wasTrailingEmpty)
            {
                FocusAndSelectTextBox(FooterDiscountPctTextBox);
            }
            else
            {
                OnRequestFocusField("product", targetRowIdx);
            }
        }
    }

    private void ProductModalResultsListBox_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (Vm == null) return;

        var pos = e.GetPosition(ProductModalResultsListBox);
        if (Math.Abs(pos.X - _lastProductModalMousePos.X) < 1.0 &&
            Math.Abs(pos.Y - _lastProductModalMousePos.Y) < 1.0)
        {
            return;
        }
        _lastProductModalMousePos = pos;

        if (ItemsControl.ContainerFromElement(ProductModalResultsListBox, e.OriginalSource as DependencyObject) is ListBoxItem lbi &&
            lbi.DataContext is PurchaseReturnProductSearchDto hovered &&
            !ReferenceEquals(Vm.SelectedProductInModal, hovered))
        {
            Vm.SelectedProductInModal = hovered;
        }
    }

    private async void ProductModalResultsListBox_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Vm == null) return;

        if (ItemsControl.ContainerFromElement(ProductModalResultsListBox, e.OriginalSource as DependencyObject) is ListBoxItem lbi &&
            lbi.DataContext is PurchaseReturnProductSearchDto item)
        {
            await Vm.SelectProductFromModalAsync(item);
        }
    }

    private void ProductSearchOverlay_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm == null) return;

        if (ReferenceEquals(e.OriginalSource, ProductSearchOverlayGrid) ||
            ReferenceEquals(e.OriginalSource, ProductSearchOverlayCanvas))
        {
            e.Handled = true;
            Vm.CloseProductSearchModal();
        }
    }

    private void ProductSearchOverlayCanvas_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Vm is { IsProductSearchModalOpen: true })
        {
            ApplyProductSearchModalLayout();
        }
    }

    private void ApplyProductSearchModalLayout()
    {
        if (Vm == null || ProductSearchDialogBorder == null || ProductSearchOverlayCanvas == null)
            return;

        const double minW = 640d;
        const double minH = 420d;

        double canvasW = ProductSearchOverlayCanvas.ActualWidth > 100
            ? ProductSearchOverlayCanvas.ActualWidth
            : (ActualWidth > 100 ? ActualWidth : 1280d);
        double canvasH = ProductSearchOverlayCanvas.ActualHeight > 100
            ? ProductSearchOverlayCanvas.ActualHeight
            : (ActualHeight > 100 ? ActualHeight : 800d);

        var layout = Vm.ProductSearchModalLayout;
        double maxW = Math.Max(minW, canvasW - 16d);
        double maxH = Math.Max(minH, canvasH - 24d);

        double width = Math.Clamp(layout.Width > 0 ? layout.Width : 960d, minW, maxW);
        double height = Math.Clamp(layout.Height > 0 ? layout.Height : 600d, minH, maxH);

        double left;
        double top;
        if (layout.HasSavedPosition && double.IsFinite(layout.X!.Value) && double.IsFinite(layout.Y!.Value))
        {
            left = Math.Clamp(layout.X.Value, 8d, Math.Max(8d, canvasW - width - 8d));
            top = Math.Clamp(layout.Y.Value, 8d, Math.Max(8d, canvasH - height - 8d));
        }
        else
        {
            left = Math.Max(8d, (canvasW - width) / 2d);
            top = Math.Max(8d, (canvasH - height) / 2d);
        }

        ProductSearchDialogBorder.Width = width;
        ProductSearchDialogBorder.Height = height;
        Canvas.SetLeft(ProductSearchDialogBorder, left);
        Canvas.SetTop(ProductSearchDialogBorder, top);
    }

    private void ProductSearchHeader_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ProductSearchDialogBorder == null || ProductSearchOverlayCanvas == null)
            return;

        if (IsInteractiveHeaderChild(e.OriginalSource as DependencyObject, ProductSearchHeaderBorder))
            return;

        var pos = e.GetPosition(ProductSearchOverlayCanvas);
        var curLeft = Canvas.GetLeft(ProductSearchDialogBorder);
        var curTop = Canvas.GetTop(ProductSearchDialogBorder);
        if (double.IsNaN(curLeft)) curLeft = 120d;
        if (double.IsNaN(curTop)) curTop = 60d;

        _productSearchDragOffset = new Point(pos.X - curLeft, pos.Y - curTop);
        _isDraggingProductSearch = true;
        (sender as UIElement)?.CaptureMouse();
        e.Handled = true;
    }

    private void ProductSearchHeader_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingProductSearch || ProductSearchDialogBorder == null || ProductSearchOverlayCanvas == null)
            return;

        var pos = e.GetPosition(ProductSearchOverlayCanvas);
        double canvasW = Math.Max(ProductSearchDialogBorder.Width + 16d, ProductSearchOverlayCanvas.ActualWidth);
        double canvasH = Math.Max(ProductSearchDialogBorder.Height + 16d, ProductSearchOverlayCanvas.ActualHeight);

        double nextLeft = Math.Clamp(
            pos.X - _productSearchDragOffset.X,
            0d,
            Math.Max(0d, canvasW - ProductSearchDialogBorder.Width));
        double nextTop = Math.Clamp(
            pos.Y - _productSearchDragOffset.Y,
            0d,
            Math.Max(0d, canvasH - ProductSearchDialogBorder.Height));

        Canvas.SetLeft(ProductSearchDialogBorder, nextLeft);
        Canvas.SetTop(ProductSearchDialogBorder, nextTop);
    }

    private void ProductSearchHeader_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndProductSearchDrag(sender as UIElement);
        e.Handled = true;
    }

    private void ProductSearchHeader_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        EndProductSearchDrag(sender as UIElement);
    }

    private void EndProductSearchDrag(UIElement? senderElement)
    {
        if (!_isDraggingProductSearch)
            return;

        _isDraggingProductSearch = false;
        if (senderElement?.IsMouseCaptured == true)
            senderElement.ReleaseMouseCapture();

        PersistCurrentProductSearchLayout();
        FocusAndPlaceCaretAtEnd(ProductModalSearchTextBox);
    }

    private void ProductSearchResizeHandle_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ProductSearchDialogBorder == null || ProductSearchOverlayCanvas == null || sender is not FrameworkElement handle)
            return;

        var dir = (handle.Tag as string ?? "se").ToLowerInvariant();
        var pos = e.GetPosition(ProductSearchOverlayCanvas);
        var curLeft = Canvas.GetLeft(ProductSearchDialogBorder);
        var curTop = Canvas.GetTop(ProductSearchDialogBorder);
        if (double.IsNaN(curLeft)) curLeft = 120d;
        if (double.IsNaN(curTop)) curTop = 60d;

        _productSearchResizeState = new ModalResizeState(
            dir,
            pos.X,
            pos.Y,
            ProductSearchDialogBorder.Width,
            ProductSearchDialogBorder.Height,
            curLeft,
            curTop);

        handle.CaptureMouse();
        e.Handled = true;
    }

    private void ProductSearchResizeHandle_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_productSearchResizeState == null || ProductSearchDialogBorder == null || ProductSearchOverlayCanvas == null)
            return;

        var r = _productSearchResizeState;
        var pos = e.GetPosition(ProductSearchOverlayCanvas);
        ApplyEightWayResize(
            r,
            pos.X - r.StartX,
            pos.Y - r.StartY,
            minW: 640d,
            minH: 420d,
            ProductSearchOverlayCanvas,
            ProductSearchDialogBorder);
    }

    private void ProductSearchResizeHandle_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndProductSearchResize(sender as UIElement);
        e.Handled = true;
    }

    private void ProductSearchResizeHandle_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        EndProductSearchResize(sender as UIElement);
    }

    private void EndProductSearchResize(UIElement? handleElement)
    {
        if (_productSearchResizeState == null)
            return;

        _productSearchResizeState = null;
        if (handleElement?.IsMouseCaptured == true)
            handleElement.ReleaseMouseCapture();

        PersistCurrentProductSearchLayout();
        FocusAndPlaceCaretAtEnd(ProductModalSearchTextBox);
    }

    private void PersistCurrentProductSearchLayout()
    {
        if (Vm == null || ProductSearchDialogBorder == null)
            return;

        var left = Canvas.GetLeft(ProductSearchDialogBorder);
        var top = Canvas.GetTop(ProductSearchDialogBorder);
        var width = ProductSearchDialogBorder.Width;
        var height = ProductSearchDialogBorder.Height;

        if (double.IsFinite(left) && double.IsFinite(top) && double.IsFinite(width) && double.IsFinite(height))
        {
            _ = Vm.SaveProductSearchModalLayoutAsync(left, top, width, height);
        }
    }

    private static void ApplyEightWayResize(
        ModalResizeState r,
        double dx,
        double dy,
        double minW,
        double minH,
        Canvas overlayCanvas,
        Border dialogBorder)
    {
        double width = r.StartWidth;
        double height = r.StartHeight;
        double left = r.StartLeft;
        double top = r.StartTop;

        double maxCanvasW = overlayCanvas.ActualWidth > minW ? overlayCanvas.ActualWidth : 1920d;
        double maxCanvasH = overlayCanvas.ActualHeight > minH ? overlayCanvas.ActualHeight : 1080d;

        if (r.Direction.Contains('e'))
        {
            width = Math.Clamp(r.StartWidth + dx, minW, Math.Max(minW, maxCanvasW - left));
        }
        if (r.Direction.Contains('s'))
        {
            height = Math.Clamp(r.StartHeight + dy, minH, Math.Max(minH, maxCanvasH - top));
        }
        if (r.Direction.Contains('w'))
        {
            double targetW = Math.Max(minW, r.StartWidth - dx);
            double nextLeft = r.StartLeft + (r.StartWidth - targetW);
            if (nextLeft < 0d)
            {
                nextLeft = 0d;
                targetW = r.StartLeft + r.StartWidth;
            }
            width = Math.Max(minW, targetW);
            left = nextLeft;
        }
        if (r.Direction.Contains('n'))
        {
            double targetH = Math.Max(minH, r.StartHeight - dy);
            double nextTop = r.StartTop + (r.StartHeight - targetH);
            if (nextTop < 0d)
            {
                nextTop = 0d;
                targetH = r.StartTop + r.StartHeight;
            }
            height = Math.Max(minH, targetH);
            top = nextTop;
        }

        dialogBorder.Width = width;
        dialogBorder.Height = height;
        Canvas.SetLeft(dialogBorder, left);
        Canvas.SetTop(dialogBorder, top);
    }

    private static bool IsInteractiveHeaderChild(DependencyObject? source, DependencyObject? stopAt)
    {
        var cur = source;
        while (cur != null && !ReferenceEquals(cur, stopAt))
        {
            if (cur is TextBoxBase or ButtonBase)
                return true;
            cur = cur is Visual || cur is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(cur)
                : LogicalTreeHelper.GetParent(cur);
        }
        return false;
    }

    private void FocusAndSelectTextBox(TextBox? textBox)
    {
        if (textBox == null) return;
        Dispatcher.BeginInvoke(() =>
        {
            textBox.Focus();
            Keyboard.Focus(textBox);
            textBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void FocusAndPlaceCaretAtEnd(TextBox? textBox)
    {
        if (textBox == null) return;
        Dispatcher.BeginInvoke(() =>
        {
            textBox.Focus();
            Keyboard.Focus(textBox);
            textBox.CaretIndex = textBox.Text?.Length ?? 0;
        }, DispatcherPriority.Input);
    }

    private static string? TryMapKeyToSeedChar(Key key, ModifierKeys mods)
    {
        if (mods != ModifierKeys.None && mods != ModifierKeys.Shift)
            return null;

        if (key is >= Key.A and <= Key.Z)
        {
            var ch = (char)('a' + (key - Key.A));
            return mods == ModifierKeys.Shift ? char.ToUpperInvariant(ch).ToString() : ch.ToString();
        }

        if (mods == ModifierKeys.None && key is >= Key.D0 and <= Key.D9)
        {
            return ((char)('0' + (key - Key.D0))).ToString();
        }

        if (mods == ModifierKeys.None && key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return ((char)('0' + (key - Key.NumPad0))).ToString();
        }

        return null;
    }

    // =========================================================================
    // 7. NATIVE PURCHASE RETURN PRINT / DEBIT NOTE ENGINE
    // =========================================================================

    private void OnRequestPrintDocument(PurchaseReturnDetailDto detail)
    {
        try
        {
            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true)
                return;

            var doc = BuildPurchaseReturnPrintDocument(detail, dlg.PrintableAreaWidth);
            IDocumentPaginatorSource idp = doc;
            dlg.PrintDocument(idp.DocumentPaginator, $"Purchase Return {detail.PostedNumber}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to print purchase return: {ex.Message}",
                "Print Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private FlowDocument BuildPurchaseReturnPrintDocument(PurchaseReturnDetailDto detail, double pageWidth)
    {
        var storeTitle = Vm?.StoreName ?? "Karobar App";
        var storeAddr = Vm?.StoreAddress ?? string.Empty;
        var storePhone = Vm?.StorePhone ?? string.Empty;
        var currency = Vm?.CurrencySymbol ?? "Rs.";

        var doc = new FlowDocument
        {
            PageWidth = pageWidth > 0 ? pageWidth : 790,
            PagePadding = new Thickness(32),
            ColumnWidth = double.PositiveInfinity,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11
        };

        // Store & Document Header
        var headerPara = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
        headerPara.Inlines.Add(new Run(storeTitle + "\n") { FontSize = 18, FontWeight = FontWeights.ExtraBold });
        if (!string.IsNullOrWhiteSpace(storeAddr))
            headerPara.Inlines.Add(new Run(storeAddr + "\n") { FontSize = 10, Foreground = Brushes.DimGray });
        if (!string.IsNullOrWhiteSpace(storePhone))
            headerPara.Inlines.Add(new Run($"Phone: {storePhone}\n") { FontSize = 10, Foreground = Brushes.DimGray });
        headerPara.Inlines.Add(new Run($"PURCHASE RETURN / DEBIT NOTE — {detail.PostedNumber}") { FontSize = 14, FontWeight = FontWeights.Bold });
        doc.Blocks.Add(headerPara);

        // Metadata
        var metaPara = new Paragraph { Margin = new Thickness(0, 0, 0, 12) };
        metaPara.Inlines.Add(new Run($"Supplier: {detail.SupplierName}    |    Date: {detail.DateFormatted}\n") { FontWeight = FontWeights.SemiBold });
        metaPara.Inlines.Add(new Run($"Linked Invoice: {detail.PurchaseInvoicePostedNumber ?? "Open Return"}    |    Settlement: {detail.SettlementBadgeLabel}"));
        doc.Blocks.Add(metaPara);

        // Items Table
        var table = new Table { CellSpacing = 0, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1) };
        table.Columns.Add(new TableColumn { Width = new GridLength(36) });
        table.Columns.Add(new TableColumn { Width = new GridLength(220) });
        table.Columns.Add(new TableColumn { Width = new GridLength(85) });
        table.Columns.Add(new TableColumn { Width = new GridLength(65) });
        table.Columns.Add(new TableColumn { Width = new GridLength(65) });
        table.Columns.Add(new TableColumn { Width = new GridLength(65) });
        table.Columns.Add(new TableColumn { Width = new GridLength(75) });
        table.Columns.Add(new TableColumn { Width = new GridLength(85) });

        var rg = new TableRowGroup();
        var hr = new TableRow { Background = Brushes.WhiteSmoke, FontWeight = FontWeights.Bold };
        foreach (var h in new[] { "#", "Product", "Batch", "Expiry", "Pack.Q", "Unit.Q", "Unit Cost", "Sub Total" })
        {
            hr.Cells.Add(new TableCell(new Paragraph(new Run(h)) { Margin = new Thickness(4) }));
        }
        rg.Rows.Add(hr);

        foreach (var item in detail.Items)
        {
            var r = new TableRow();
            r.Cells.Add(new TableCell(new Paragraph(new Run(item.RowNumber.ToString())) { Margin = new Thickness(4) }));
            r.Cells.Add(new TableCell(new Paragraph(new Run(item.ProductName)) { Margin = new Thickness(4) }));
            r.Cells.Add(new TableCell(new Paragraph(new Run(string.IsNullOrWhiteSpace(item.Batch) ? "—" : item.Batch)) { Margin = new Thickness(4) }));
            r.Cells.Add(new TableCell(new Paragraph(new Run(item.ExpiryFormatted)) { Margin = new Thickness(4) }));
            r.Cells.Add(new TableCell(new Paragraph(new Run(item.ReturnPackQuantity.ToString("N2"))) { Margin = new Thickness(4) }));
            r.Cells.Add(new TableCell(new Paragraph(new Run(item.ReturnUnitQuantity.ToString())) { Margin = new Thickness(4) }));
            r.Cells.Add(new TableCell(new Paragraph(new Run(item.UnitPurchasePrice.ToString("N2"))) { Margin = new Thickness(4) }));
            r.Cells.Add(new TableCell(new Paragraph(new Run(item.SubTotal.ToString("N2"))) { Margin = new Thickness(4), FontWeight = FontWeights.SemiBold }));
            rg.Rows.Add(r);
        }

        table.RowGroups.Add(rg);
        doc.Blocks.Add(table);

        // Summary Totals
        var totalsPara = new Paragraph { Margin = new Thickness(0, 12, 0, 0), TextAlignment = TextAlignment.Right };
        totalsPara.Inlines.Add(new Run($"Gross Total: {currency} {detail.GrossTotal:N2}\n"));
        totalsPara.Inlines.Add(new Run($"Discount ({detail.DiscountPercentage:N2}%): {currency} {detail.DiscountAmount:N2}\n"));
        totalsPara.Inlines.Add(new Run($"Tax ({detail.TaxPercentage:N2}%): {currency} {detail.TaxAmount:N2}\n"));
        totalsPara.Inlines.Add(new Run($"Net Return Total: {currency} {detail.Total:N2}") { FontSize = 13, FontWeight = FontWeights.ExtraBold });
        doc.Blocks.Add(totalsPara);

        return doc;
    }
}
