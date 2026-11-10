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

public partial class PurchaseInvoicesView : UserControl, IPageKeyboardHandler
{
    private PurchaseInvoicesViewModel? _wiredVm;

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

    private bool _isDraggingQuickAddProduct;
    private Point _quickAddProductDragOffset;
    private ModalResizeState? _quickAddProductResizeState;

    public PurchaseInvoicesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_wiredVm != null)
        {
            _wiredVm.RequestFocusSupplierSelect = null;
            _wiredVm.RequestFocusSupplierModalSearch = null;
            _wiredVm.RequestFocusInvoiceNumber = null;
            _wiredVm.RequestFocusBillAmount = null;
            _wiredVm.RequestFocusDirectorySearch = null;
            _wiredVm.RequestFocusProductModalSearch = null;
            _wiredVm.RequestFocusQuickAddProductName = null;
            _wiredVm.RequestApplyProductSearchModalLayout = null;
            _wiredVm.RequestApplyQuickAddProductModalLayout = null;
            _wiredVm.RequestFocusLineCell = null;
            _wiredVm.RequestFocusFooterField = null;
            _wiredVm.RequestPrintDocument = null;
        }

        _wiredVm = e.NewValue as PurchaseInvoicesViewModel;
        if (_wiredVm != null)
        {
            _wiredVm.RequestFocusSupplierSelect = () => Dispatcher.BeginInvoke(() =>
            {
                SupplierTriggerButton?.Focus();
                if (SupplierTriggerButton != null)
                    Keyboard.Focus(SupplierTriggerButton);
            }, DispatcherPriority.Input);

            _wiredVm.RequestFocusSupplierModalSearch = () => FocusAndPlaceCaretAtEnd(SupplierModalSearchTextBox);
            _wiredVm.RequestFocusInvoiceNumber = () => FocusAndSelectTextBox(InvoiceNumberTextBox);
            _wiredVm.RequestFocusBillAmount = () => FocusAndSelectTextBox(BillAmountTextBox);
            _wiredVm.RequestFocusDirectorySearch = () => FocusAndSelectTextBox(DirectorySearchTextBox);
            _wiredVm.RequestFocusProductModalSearch = () => FocusAndSelectTextBox(ProductModalSearchTextBox);
            _wiredVm.RequestFocusQuickAddProductName = () => FocusAndSelectTextBox(QuickAddProductNameTextBox);
            _wiredVm.RequestApplyProductSearchModalLayout = () => Dispatcher.BeginInvoke(ApplyProductSearchModalLayout, DispatcherPriority.Loaded);
            _wiredVm.RequestApplyQuickAddProductModalLayout = () => Dispatcher.BeginInvoke(ApplyQuickAddProductModalLayout, DispatcherPriority.Loaded);
            _wiredVm.RequestFocusLineCell = (rowIndex, fieldTag) => FocusLineCell(rowIndex, fieldTag);
            _wiredVm.RequestFocusFooterField = FocusFooterField;
            _wiredVm.RequestPrintDocument = ExecutePrintPurchaseInvoice;
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

    private void DeleteConfirmPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is PurchaseInvoicesViewModel vm && sender is PasswordBox pb)
        {
            vm.DeletePassword = pb.Password;
        }
    }

    private void DeleteConfirmPasswordBox_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is PasswordBox pb && pb.IsVisible)
        {
            pb.Clear();
            Dispatcher.BeginInvoke(() =>
            {
                pb.Focus();
                Keyboard.Focus(pb);
            }, DispatcherPriority.Input);
        }
    }

    private async void DeleteConfirmPasswordBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None &&
            DataContext is PurchaseInvoicesViewModel { IsDeleteModalOpen: true, IsDeleteStep2: true } vm)
        {
            e.Handled = true;
            await vm.ConfirmDeleteInvoiceAsync();
        }
    }

    // =========================================================================
    // 2. HEADER KEYBOARD CHAIN (Supplier Modal -> Invoice # -> Bill Amt -> Row 0 Product Modal)
    // =========================================================================

    private void SupplierTriggerButton_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not PurchaseInvoicesViewModel vm)
            return;

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None && (e.Key == Key.Enter || e.Key == Key.Space || e.Key == Key.Down))
        {
            e.Handled = true;
            vm.OpenSupplierSearchModal();
            return;
        }

        var seed = TryMapKeyToSeedChar(e.Key, mods);
        if (seed != null)
        {
            e.Handled = true;
            vm.OpenSupplierSearchModal(seed);
        }
    }

    private void SupplierModalSearchTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not PurchaseInvoicesViewModel vm)
            return;

        if (e.Key == Key.Down)
        {
            e.Handled = true;
            vm.MoveSupplierModalHighlight(1);
            if (vm.HighlightedModalSupplier != null)
                SupplierModalResultsListBox.ScrollIntoView(vm.HighlightedModalSupplier);
        }
        else if (e.Key == Key.Up)
        {
            e.Handled = true;
            vm.MoveSupplierModalHighlight(-1);
            if (vm.HighlightedModalSupplier != null)
                SupplierModalResultsListBox.ScrollIntoView(vm.HighlightedModalSupplier);
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            vm.CommitSupplierModalSelection();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            vm.CloseSupplierSearchModal();
            Dispatcher.BeginInvoke(() => SupplierTriggerButton?.Focus(), DispatcherPriority.Input);
        }
    }

    private void SupplierModalResultsListBox_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not PurchaseInvoicesViewModel vm)
            return;

        if (ItemsControl.ContainerFromElement(SupplierModalResultsListBox, e.OriginalSource as DependencyObject) is ListBoxItem lbi &&
            lbi.DataContext is PurchaseSupplierLookupDto sup)
        {
            vm.CommitSupplierModalSelection(sup);
        }
    }

    private void InvoiceNumberTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            FocusAndSelectTextBox(BillAmountTextBox);
        }
    }

    private async void BillAmountTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not PurchaseInvoicesViewModel vm)
            return;

        if (Keyboard.Modifiers == ModifierKeys.None && (e.Key == Key.Enter || e.Key == Key.Tab))
        {
            e.Handled = true;
            if (vm.Lines.Count == 0)
            {
                vm.AppendEmptyLine();
            }

            // Requirement #2: hitting Enter in Bill Amt automatically opens the Add/Search Product Modal for Row 0!
            await vm.OpenProductSearchForLineAsync(vm.Lines[0]);
        }
    }

    // =========================================================================
    // 3. LINE-ITEM GRID KEYBOARD CHAIN (Enter & ArrowUp/ArrowDown)
    // =========================================================================

    private async void ProductCellButton_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not PurchaseInvoicesViewModel vm ||
            sender is not FrameworkElement fe ||
            fe.DataContext is not PurchaseInvoiceLineRowViewModel row)
        {
            return;
        }

        var mods = Keyboard.Modifiers;
        var rowIndex = vm.Lines.IndexOf(row);

        if (mods == ModifierKeys.None && e.Key == Key.Enter)
        {
            e.Handled = true;
            if (!row.HasProduct)
            {
                await vm.OpenProductSearchForLineAsync(row);
            }
            else
            {
                var nextCol = row.BatchOptions.Count > 1 ? "batch" : "pack_quantity";
                FocusLineCell(rowIndex, nextCol);
            }
            return;
        }

        // Pressing Tab on an empty trailing row jumps cleanly to Footer Discount %
        if (mods == ModifierKeys.None && e.Key == Key.Tab && !row.HasProduct && vm.ActiveProductLinesCount > 0)
        {
            e.Handled = true;
            FocusAndSelectTextBox(FooterDiscountPctTextBox);
            return;
        }

        if (mods == ModifierKeys.None && e.Key == Key.Up && rowIndex > 0)
        {
            e.Handled = true;
            FocusLineCell(rowIndex - 1, "product");
            return;
        }

        if (mods == ModifierKeys.None && e.Key == Key.Down)
        {
            e.Handled = true;
            if (rowIndex + 1 < vm.Lines.Count)
            {
                var nextRow = vm.Lines[rowIndex + 1];
                if (!nextRow.HasProduct)
                    await vm.OpenProductSearchForLineAsync(nextRow);
                else
                    FocusLineCell(rowIndex + 1, "product");
            }
            else
            {
                // Requirement #4: ArrowDown on last row appends new row AND opens Add Product Modal!
                vm.AppendEmptyLine();
                await vm.OpenProductSearchForLineAsync(vm.Lines[^1]);
            }
            return;
        }

        // Typing any alphanumeric character on the Product trigger opens the Product Search Modal seeded with that character!
        var seed = TryMapKeyToSeedChar(e.Key, mods);
        if (seed != null)
        {
            e.Handled = true;
            await vm.OpenProductSearchWithSeedAsync(row, seed);
        }
    }

    private void LineBatchSelect_OnEnterCommitted(object? sender, EventArgs e)
    {
        if (DataContext is not PurchaseInvoicesViewModel vm ||
            sender is not FrameworkElement fe ||
            fe.DataContext is not PurchaseInvoiceLineRowViewModel row)
        {
            return;
        }

        var rowIndex = vm.Lines.IndexOf(row);
        if (rowIndex < 0)
            return;

        if (!string.IsNullOrWhiteSpace(row.BatchNumber) && !row.ExpiryDate.HasValue)
        {
            FocusLineCell(rowIndex, "expiry");
        }
        else
        {
            FocusLineCell(rowIndex, "pack_quantity");
        }
    }

    private async void LineExpiryDatePicker_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not PurchaseInvoicesViewModel vm ||
            sender is not FrameworkElement fe ||
            fe.DataContext is not PurchaseInvoiceLineRowViewModel row)
        {
            return;
        }

        var rowIndex = vm.Lines.IndexOf(row);
        if (rowIndex < 0)
            return;

        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Enter)
        {
            e.Handled = true;
            FocusLineCell(rowIndex, "pack_quantity");
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Down)
        {
            e.Handled = true;
            await HandleRowArrowDownAsync(vm, rowIndex, "expiry");
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Up && rowIndex > 0)
        {
            e.Handled = true;
            var targetTag = vm.Lines[rowIndex - 1].HasProduct ? "expiry" : "product";
            FocusLineCell(rowIndex - 1, targetTag);
        }
    }

    private void LineCellTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PurchaseInvoiceLineRowViewModel row })
        {
            row.NormalizeDisplayTexts();
        }
    }

    private async void LineCellTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not PurchaseInvoicesViewModel vm ||
            sender is not TextBox tb ||
            tb.DataContext is not PurchaseInvoiceLineRowViewModel row)
        {
            return;
        }

        var mods = Keyboard.Modifiers;
        var fieldTag = tb.Tag as string ?? string.Empty;
        var rowIndex = vm.Lines.IndexOf(row);
        if (rowIndex < 0)
            return;

        if (mods == ModifierKeys.None && e.Key == Key.Enter)
        {
            e.Handled = true;
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            row.NormalizeDisplayTexts();

            switch (fieldTag)
            {
                case "pack_quantity":
                case "unit_quantity":
                    FocusLineCell(rowIndex, "pack_purchase_price");
                    break;

                case "pack_purchase_price":
                case "unit_purchase_price":
                    FocusLineCell(rowIndex, "item_discount_percentage");
                    break;

                case "item_discount_percentage":
                    FocusLineCell(rowIndex, "pack_bonus");
                    break;

                case "pack_bonus":
                case "unit_bonus":
                    FocusLineCell(rowIndex, "pack_sale_price");
                    break;

                case "pack_sale_price":
                case "unit_sale_price":
                    if (vm.HasWholesale)
                        FocusLineCell(rowIndex, "whole_sale_pack_price");
                    else
                        await AdvanceToNextRowProductAsync(vm, rowIndex);
                    break;

                case "whole_sale_pack_price":
                    FocusLineCell(rowIndex, "whole_sale_unit_price");
                    break;

                case "whole_sale_unit_price":
                    await AdvanceToNextRowProductAsync(vm, rowIndex);
                    break;
            }
            return;
        }

        // Vertical Arrow Navigation (↑ / ↓) across same column
        if (mods == ModifierKeys.None && e.Key == Key.Up && rowIndex > 0)
        {
            e.Handled = true;
            var targetTag = vm.Lines[rowIndex - 1].HasProduct ? fieldTag : "product";
            FocusLineCell(rowIndex - 1, targetTag);
            return;
        }

        if (mods == ModifierKeys.None && e.Key == Key.Down)
        {
            e.Handled = true;
            await HandleRowArrowDownAsync(vm, rowIndex, fieldTag);
        }
    }

    private async Task HandleRowArrowDownAsync(PurchaseInvoicesViewModel vm, int currentRowIndex, string fieldTag)
    {
        if (currentRowIndex + 1 < vm.Lines.Count)
        {
            var nextRow = vm.Lines[currentRowIndex + 1];
            if (!nextRow.HasProduct)
            {
                await vm.OpenProductSearchForLineAsync(nextRow);
            }
            else
            {
                FocusLineCell(currentRowIndex + 1, fieldTag);
            }
        }
        else
        {
            // Requirement #4: ArrowDown on last row appends new row AND opens Add Product Modal!
            vm.AppendEmptyLine();
            await vm.OpenProductSearchForLineAsync(vm.Lines[^1]);
        }
    }

    private async Task AdvanceToNextRowProductAsync(PurchaseInvoicesViewModel vm, int currentRowIndex)
    {
        if (currentRowIndex + 1 < vm.Lines.Count)
        {
            var nextRow = vm.Lines[currentRowIndex + 1];
            if (!nextRow.HasProduct)
                await vm.OpenProductSearchForLineAsync(nextRow);
            else
                FocusLineCell(currentRowIndex + 1, "product");
        }
        else
        {
            vm.AppendEmptyLine();
            await vm.OpenProductSearchForLineAsync(vm.Lines[^1]);
        }
    }

    // =========================================================================
    // 4. PRODUCT SEARCH MODAL & FOOTER KEYBOARD HANDLERS
    // =========================================================================

    private async void ProductModalSearchTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not PurchaseInvoicesViewModel vm)
            return;

        if (e.Key == Key.Down)
        {
            e.Handled = true;
            vm.MoveProductSearchHighlight(1);
            if (vm.HighlightedSearchProduct != null)
                ProductModalResultsListBox.ScrollIntoView(vm.HighlightedSearchProduct);
        }
        else if (e.Key == Key.Up)
        {
            e.Handled = true;
            vm.MoveProductSearchHighlight(-1);
            if (vm.HighlightedSearchProduct != null)
                ProductModalResultsListBox.ScrollIntoView(vm.HighlightedSearchProduct);
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await vm.CommitProductSelectionAsync();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            var wasTrailingEmpty = vm.ActiveSearchLineIndex >= 0 &&
                                   vm.ActiveSearchLineIndex < vm.Lines.Count &&
                                   !vm.Lines[vm.ActiveSearchLineIndex].HasProduct &&
                                   vm.ActiveProductLinesCount > 0;
            vm.CloseProductSearchModal();
            if (wasTrailingEmpty)
            {
                FocusAndSelectTextBox(FooterDiscountPctTextBox);
            }
            else
            {
                FocusLineCell(vm.ActiveSearchLineIndex, "product");
            }
        }
    }

    private void ProductModalResultsListBox_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not PurchaseInvoicesViewModel vm)
            return;

        var pos = e.GetPosition(ProductModalResultsListBox);
        if (Math.Abs(pos.X - _lastProductModalMousePos.X) < 1.0 &&
            Math.Abs(pos.Y - _lastProductModalMousePos.Y) < 1.0)
        {
            return;
        }
        _lastProductModalMousePos = pos;

        if (ItemsControl.ContainerFromElement(ProductModalResultsListBox, e.OriginalSource as DependencyObject) is ListBoxItem lbi &&
            lbi.DataContext is PurchaseProductSearchItemDto hovered &&
            !ReferenceEquals(vm.HighlightedSearchProduct, hovered))
        {
            vm.HighlightedSearchProduct = hovered;
        }
    }

    private async void ProductModalResultsListBox_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not PurchaseInvoicesViewModel vm)
            return;

        if (ItemsControl.ContainerFromElement(ProductModalResultsListBox, e.OriginalSource as DependencyObject) is ListBoxItem lbi &&
            lbi.DataContext is PurchaseProductSearchItemDto item)
        {
            await vm.CommitProductSelectionAsync(item);
        }
    }

    // =========================================================================
    // 4B. DRAGGABLE & 8-WAY RESIZABLE PRODUCT SEARCH & QUICK-ADD MODALS
    //     (Exact ProductSearchInput.jsx:66-131, 367-506 & ProductFormModal.jsx)
    // =========================================================================

    private void ProductSearchOverlay_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not PurchaseInvoicesViewModel vm)
            return;

        // Close on backdrop click (ProductSearchInput.jsx:552-554)
        if (ReferenceEquals(e.OriginalSource, ProductSearchOverlayGrid) ||
            ReferenceEquals(e.OriginalSource, ProductSearchOverlayCanvas))
        {
            e.Handled = true;
            vm.CloseProductSearchModal();
        }
    }

    private void ProductSearchOverlayCanvas_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is PurchaseInvoicesViewModel { IsProductSearchModalOpen: true })
        {
            ApplyProductSearchModalLayout();
        }
    }

    private void ApplyProductSearchModalLayout()
    {
        if (DataContext is not PurchaseInvoicesViewModel vm ||
            ProductSearchDialogBorder == null ||
            ProductSearchOverlayCanvas == null)
        {
            return;
        }

        const double minW = 640d;
        const double minH = 420d;

        double canvasW = ProductSearchOverlayCanvas.ActualWidth > 100
            ? ProductSearchOverlayCanvas.ActualWidth
            : (ActualWidth > 100 ? ActualWidth : 1280d);
        double canvasH = ProductSearchOverlayCanvas.ActualHeight > 100
            ? ProductSearchOverlayCanvas.ActualHeight
            : (ActualHeight > 100 ? ActualHeight : 800d);

        var layout = vm.ProductSearchModalLayout;
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

        // Do not start dragging when clicking inside the search TextBox or header Buttons
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
        if (DataContext is not PurchaseInvoicesViewModel vm || ProductSearchDialogBorder == null)
            return;

        var left = Canvas.GetLeft(ProductSearchDialogBorder);
        var top = Canvas.GetTop(ProductSearchDialogBorder);
        var width = ProductSearchDialogBorder.Width;
        var height = ProductSearchDialogBorder.Height;

        if (double.IsFinite(left) && double.IsFinite(top) && double.IsFinite(width) && double.IsFinite(height))
        {
            _ = vm.SaveProductSearchModalLayoutAsync(left, top, width, height);
        }
    }

    // Quick "+ Add Product" Modal Drag & 8-Way Resize (ProductFormModal.jsx parity)
    private void QuickAddProductOverlay_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not PurchaseInvoicesViewModel vm)
            return;

        if (ReferenceEquals(e.OriginalSource, QuickAddProductOverlayGrid) ||
            ReferenceEquals(e.OriginalSource, QuickAddProductOverlayCanvas))
        {
            e.Handled = true;
            vm.CloseQuickAddProductModal();
        }
    }

    private void QuickAddProductOverlayCanvas_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is PurchaseInvoicesViewModel { IsQuickAddProductModalOpen: true })
        {
            ApplyQuickAddProductModalLayout();
        }
    }

    private void ApplyQuickAddProductModalLayout()
    {
        if (DataContext is not PurchaseInvoicesViewModel vm ||
            QuickAddProductDialogBorder == null ||
            QuickAddProductOverlayCanvas == null)
        {
            return;
        }

        const double minW = 600d;
        const double minH = 400d;

        double canvasW = QuickAddProductOverlayCanvas.ActualWidth > 100
            ? QuickAddProductOverlayCanvas.ActualWidth
            : (ActualWidth > 100 ? ActualWidth : 1280d);
        double canvasH = QuickAddProductOverlayCanvas.ActualHeight > 100
            ? QuickAddProductOverlayCanvas.ActualHeight
            : (ActualHeight > 100 ? ActualHeight : 800d);

        var layout = vm.QuickAddProductModalLayout;
        double maxW = Math.Max(minW, canvasW - 16d);
        double maxH = Math.Max(minH, canvasH - 24d);

        double width = Math.Clamp(layout.Width > 0 ? layout.Width : 800d, minW, maxW);
        double height = Math.Clamp(layout.Height > 0 ? layout.Height : 560d, minH, maxH);

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

        QuickAddProductDialogBorder.Width = width;
        QuickAddProductDialogBorder.Height = height;
        Canvas.SetLeft(QuickAddProductDialogBorder, left);
        Canvas.SetTop(QuickAddProductDialogBorder, top);
    }

    private void QuickAddProductHeader_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (QuickAddProductDialogBorder == null || QuickAddProductOverlayCanvas == null)
            return;

        if (IsInteractiveHeaderChild(e.OriginalSource as DependencyObject, sender as DependencyObject))
            return;

        var pos = e.GetPosition(QuickAddProductOverlayCanvas);
        var curLeft = Canvas.GetLeft(QuickAddProductDialogBorder);
        var curTop = Canvas.GetTop(QuickAddProductDialogBorder);
        if (double.IsNaN(curLeft)) curLeft = 160d;
        if (double.IsNaN(curTop)) curTop = 70d;

        _quickAddProductDragOffset = new Point(pos.X - curLeft, pos.Y - curTop);
        _isDraggingQuickAddProduct = true;
        (sender as UIElement)?.CaptureMouse();
        e.Handled = true;
    }

    private void QuickAddProductHeader_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingQuickAddProduct || QuickAddProductDialogBorder == null || QuickAddProductOverlayCanvas == null)
            return;

        var pos = e.GetPosition(QuickAddProductOverlayCanvas);
        double canvasW = Math.Max(QuickAddProductDialogBorder.Width + 16d, QuickAddProductOverlayCanvas.ActualWidth);
        double canvasH = Math.Max(QuickAddProductDialogBorder.Height + 16d, QuickAddProductOverlayCanvas.ActualHeight);

        double nextLeft = Math.Clamp(
            pos.X - _quickAddProductDragOffset.X,
            0d,
            Math.Max(0d, canvasW - QuickAddProductDialogBorder.Width));
        double nextTop = Math.Clamp(
            pos.Y - _quickAddProductDragOffset.Y,
            0d,
            Math.Max(0d, canvasH - QuickAddProductDialogBorder.Height));

        Canvas.SetLeft(QuickAddProductDialogBorder, nextLeft);
        Canvas.SetTop(QuickAddProductDialogBorder, nextTop);
    }

    private void QuickAddProductHeader_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndQuickAddProductDrag(sender as UIElement);
        e.Handled = true;
    }

    private void QuickAddProductHeader_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        EndQuickAddProductDrag(sender as UIElement);
    }

    private void EndQuickAddProductDrag(UIElement? senderElement)
    {
        if (!_isDraggingQuickAddProduct)
            return;

        _isDraggingQuickAddProduct = false;
        if (senderElement?.IsMouseCaptured == true)
            senderElement.ReleaseMouseCapture();

        PersistCurrentQuickAddProductLayout();
    }

    private void QuickAddProductResizeHandle_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (QuickAddProductDialogBorder == null || QuickAddProductOverlayCanvas == null || sender is not FrameworkElement handle)
            return;

        var dir = (handle.Tag as string ?? "se").ToLowerInvariant();
        var pos = e.GetPosition(QuickAddProductOverlayCanvas);
        var curLeft = Canvas.GetLeft(QuickAddProductDialogBorder);
        var curTop = Canvas.GetTop(QuickAddProductDialogBorder);
        if (double.IsNaN(curLeft)) curLeft = 160d;
        if (double.IsNaN(curTop)) curTop = 70d;

        _quickAddProductResizeState = new ModalResizeState(
            dir,
            pos.X,
            pos.Y,
            QuickAddProductDialogBorder.Width,
            QuickAddProductDialogBorder.Height,
            curLeft,
            curTop);

        handle.CaptureMouse();
        e.Handled = true;
    }

    private void QuickAddProductResizeHandle_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_quickAddProductResizeState == null || QuickAddProductDialogBorder == null || QuickAddProductOverlayCanvas == null)
            return;

        var r = _quickAddProductResizeState;
        var pos = e.GetPosition(QuickAddProductOverlayCanvas);
        ApplyEightWayResize(
            r,
            pos.X - r.StartX,
            pos.Y - r.StartY,
            minW: 600d,
            minH: 400d,
            QuickAddProductOverlayCanvas,
            QuickAddProductDialogBorder);
    }

    private void QuickAddProductResizeHandle_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndQuickAddProductResize(sender as UIElement);
        e.Handled = true;
    }

    private void QuickAddProductResizeHandle_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        EndQuickAddProductResize(sender as UIElement);
    }

    private void EndQuickAddProductResize(UIElement? handleElement)
    {
        if (_quickAddProductResizeState == null)
            return;

        _quickAddProductResizeState = null;
        if (handleElement?.IsMouseCaptured == true)
            handleElement.ReleaseMouseCapture();

        PersistCurrentQuickAddProductLayout();
    }

    private void PersistCurrentQuickAddProductLayout()
    {
        if (DataContext is not PurchaseInvoicesViewModel vm || QuickAddProductDialogBorder == null)
            return;

        var left = Canvas.GetLeft(QuickAddProductDialogBorder);
        var top = Canvas.GetTop(QuickAddProductDialogBorder);
        var width = QuickAddProductDialogBorder.Width;
        var height = QuickAddProductDialogBorder.Height;

        if (double.IsFinite(left) && double.IsFinite(top) && double.IsFinite(width) && double.IsFinite(height))
        {
            _ = vm.SaveQuickAddProductModalLayoutAsync(left, top, width, height);
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

    private void FooterDiscountPctTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            FocusAndSelectTextBox(FooterTaxPctTextBox);
        }
    }

    private void FooterDiscountAmtTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
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
            SaveInvoiceFooterButton?.Focus();
        }
    }

    private void FooterTaxAmtTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            SaveInvoiceFooterButton?.Focus();
        }
    }

    // =========================================================================
    // 5. PAGE-LEVEL SHORTCUTS & IPageKeyboardHandler (All 7 Methods)
    // =========================================================================

    private async void PurchaseInvoicesView_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not PurchaseInvoicesViewModel vm)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;

        if (mods == ModifierKeys.Alt)
        {
            if (key == Key.T && vm.IsFormMode && !vm.HasAnyModalOpen)
            {
                e.Handled = true;
                vm.ToggleInvoiceType();
                return;
            }
            if (key == Key.H && vm.IsFormMode && !vm.HasAnyModalOpen)
            {
                e.Handled = true;
                await vm.HoldCurrentDraftAsync();
                return;
            }
            if (key == Key.D && (vm.IsFormMode || vm.IsDirectoryMode))
            {
                e.Handled = true;
                await vm.OpenHeldDraftsModalAsync();
                return;
            }
            if (key == Key.E && vm.IsDetailMode)
            {
                e.Handled = true;
                vm.EditCurrentDetail();
                return;
            }
        }

        if (mods == ModifierKeys.Control && key == Key.P && vm.IsDetailMode)
        {
            e.Handled = true;
            vm.PrintCurrentDetailA4();
        }
    }

    public bool HandleNewShortcut()
    {
        if (DataContext is not PurchaseInvoicesViewModel vm)
            return false;

        vm.GoToCreateForm();
        return true;
    }

    public bool HandleSaveShortcut()
    {
        if (DataContext is not PurchaseInvoicesViewModel vm)
            return false;

        if (vm.IsQuickAddProductModalOpen)
        {
            _ = vm.SaveQuickAddProductAsync();
            return true;
        }

        if (vm.IsReverseModalOpen)
        {
            _ = vm.ConfirmReverseInvoiceAsync();
            return true;
        }

        if (vm.IsDeleteModalOpen && vm.IsDeleteStep2)
        {
            _ = vm.ConfirmDeleteInvoiceAsync();
            return true;
        }

        if (vm.IsFormMode)
        {
            _ = vm.SaveInvoiceAsync();
            return true;
        }

        return false;
    }

    public bool HandleSearchShortcut()
    {
        if (DataContext is not PurchaseInvoicesViewModel vm)
            return false;

        if (vm.IsDirectoryMode)
        {
            FocusAndSelectTextBox(DirectorySearchTextBox);
            return true;
        }

        if (vm.IsFormMode)
        {
            var emptyIdx = -1;
            for (int i = 0; i < vm.Lines.Count; i++)
            {
                if (!vm.Lines[i].HasProduct)
                {
                    emptyIdx = i;
                    break;
                }
            }
            if (emptyIdx < 0)
            {
                vm.AppendEmptyLine();
                emptyIdx = vm.Lines.Count - 1;
            }
            _ = vm.OpenProductSearchForLineAsync(vm.Lines[emptyIdx]);
            return true;
        }

        return false;
    }

    public bool HandleEscapeShortcut()
    {
        if (DataContext is not PurchaseInvoicesViewModel vm)
            return false;

        if (vm.HasAnyModalOpen)
        {
            vm.CloseAllModals();
            return true;
        }

        if (vm.IsFormMode || vm.IsDetailMode)
        {
            vm.BackToDirectory();
            return true;
        }

        if (vm.IsDirectoryMode && (!string.IsNullOrEmpty(vm.DirectorySearchText) || !string.IsNullOrEmpty(vm.DirectorySupplierSearchText)))
        {
            vm.DirectorySearchText = string.Empty;
            vm.DirectorySupplierSearchText = string.Empty;
            return true;
        }

        return false;
    }

    public bool HandleRefreshShortcut()
    {
        if (DataContext is not PurchaseInvoicesViewModel vm)
            return false;

        if (vm.IsDirectoryMode)
        {
            _ = vm.RefreshDirectoryAsync();
            return true;
        }

        if (vm.IsDetailMode && vm.CurrentDetail != null)
        {
            _ = vm.OpenDetailModeAsync(vm.CurrentDetail.Id);
            return true;
        }

        return false;
    }

    public bool HandleImportShortcut() => false;

    public bool HandleExportShortcut()
    {
        if (DataContext is PurchaseInvoicesViewModel { IsDetailMode: true } vm)
        {
            vm.PrintCurrentDetailA4();
            return true;
        }
        return false;
    }

    // =========================================================================
    // 6. VISUAL TREE FOCUS HELPERS
    // =========================================================================

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

    private void FocusFooterField(string fieldKey)
    {
        if (fieldKey == "discount_percentage")
            FocusAndSelectTextBox(FooterDiscountPctTextBox);
        else if (fieldKey == "tax_percentage")
            FocusAndSelectTextBox(FooterTaxPctTextBox);
        else if (fieldKey == "total_paid")
            FocusAndSelectTextBox(TotalPaidTextBox);
        else
            SaveInvoiceFooterButton?.Focus();
    }

    private void FocusLineCell(int rowIndex, string fieldTag)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (LinesItemsControl == null || rowIndex < 0 || rowIndex >= LinesItemsControl.Items.Count)
                return;

            LinesItemsControl.UpdateLayout();
            var container = LinesItemsControl.ItemContainerGenerator.ContainerFromIndex(rowIndex) as DependencyObject;
            if (container == null)
                return;

            var target = FindDescendantByTag(container, fieldTag);
            if (target == null)
                return;

            target.BringIntoView();

            if (target is SearchableSelect ss)
            {
                ss.FocusSearchInput();
            }
            else if (target is DatePicker dp)
            {
                dp.ApplyTemplate();
                if (dp.Template?.FindName("PART_TextBox", dp) is DatePickerTextBox dpTb)
                {
                    dpTb.Focus();
                    Keyboard.Focus(dpTb);
                    dpTb.SelectAll();
                }
                else
                {
                    dp.Focus();
                }
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

    private static FrameworkElement? FindDescendantByTag(DependencyObject root, string expectedTag)
    {
        var childrenCount = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < childrenCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe &&
                string.Equals(fe.Tag as string, expectedTag, StringComparison.OrdinalIgnoreCase))
            {
                return fe;
            }

            var nested = FindDescendantByTag(child, expectedTag);
            if (nested != null)
                return nested;
        }
        return null;
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
    // 7. NATIVE WINDOWS PRINTING (A4 GRN VOUCHER & 80mm THERMAL RECEIPT)
    // =========================================================================

    public void ExecutePrintPurchaseInvoice(PurchaseInvoiceDetailDto detail, bool thermal80mm)
    {
        if (DataContext is not PurchaseInvoicesViewModel vm)
            return;

        try
        {
            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true)
                return;

            var doc = BuildPurchaseFlowDocument(detail, vm.StoreSettings, thermal80mm);
            if (thermal80mm)
            {
                doc.PageWidth = 280; // ~80mm printable roll width in DIPs
                doc.PagePadding = new Thickness(10, 12, 10, 16);
                doc.ColumnWidth = 260;
            }
            else
            {
                doc.PageWidth = printDialog.PrintableAreaWidth > 0 ? printDialog.PrintableAreaWidth : 793;
                doc.PageHeight = printDialog.PrintableAreaHeight > 0 ? printDialog.PrintableAreaHeight : 1122;
                doc.PagePadding = new Thickness(36);
                doc.ColumnWidth = doc.PageWidth - 72;
            }

            IDocumentPaginatorSource paginatorSource = doc;
            printDialog.PrintDocument(
                paginatorSource.DocumentPaginator,
                $"Purchase Invoice {detail.PostedNumber}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to print purchase invoice: {ex.Message}",
                "Print Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static FlowDocument BuildPurchaseFlowDocument(
        PurchaseInvoiceDetailDto detail,
        PurchaseStoreSettingsDto store,
        bool thermal)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = thermal ? 10 : 11.5,
            Foreground = Brushes.Black
        };

        var headerPara = new Paragraph
        {
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
        headerPara.Inlines.Add(new Bold(new Run(store.StoreName)) { FontSize = thermal ? 13 : 18 });
        if (!string.IsNullOrWhiteSpace(store.Address))
        {
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new Run(store.Address) { FontSize = thermal ? 9 : 10.5 });
        }
        if (!string.IsNullOrWhiteSpace(store.PhoneNumber))
        {
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new Run($"Tel: {store.PhoneNumber}") { FontSize = thermal ? 9 : 10.5 });
        }
        headerPara.Inlines.Add(new LineBreak());
        headerPara.Inlines.Add(new Bold(new Run(
            string.Equals(detail.Status, "reversed", StringComparison.OrdinalIgnoreCase)
                ? "PURCHASE INVOICE (REVERSED / VOID)"
                : "PURCHASE INVOICE / GRN")));
        doc.Blocks.Add(headerPara);

        var metaPara = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
        metaPara.Inlines.Add(new Bold(new Run("Posted #: ")));
        metaPara.Inlines.Add(new Run($"{detail.PostedNumber}    "));
        metaPara.Inlines.Add(new Bold(new Run("Date: ")));
        metaPara.Inlines.Add(new Run($"{detail.PostedDate:dd-MM-yyyy}\n"));
        metaPara.Inlines.Add(new Bold(new Run("Supplier: ")));
        metaPara.Inlines.Add(new Run($"{detail.SupplierName}    "));
        metaPara.Inlines.Add(new Bold(new Run("Supplier Inv #: ")));
        metaPara.Inlines.Add(new Run($"{detail.InvoiceNumber}\n"));
        metaPara.Inlines.Add(new Bold(new Run("Type: ")));
        metaPara.Inlines.Add(new Run($"{detail.InvoiceType.ToUpperInvariant()}"));
        if (detail.DueDate.HasValue)
        {
            metaPara.Inlines.Add(new Run($"    Due: {detail.DueDate.Value:dd-MM-yyyy}"));
        }
        doc.Blocks.Add(metaPara);

        var table = new Table { CellSpacing = 0, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 1, 0, 1) };
        if (thermal)
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(115) });
            table.Columns.Add(new TableColumn { Width = new GridLength(42) });
            table.Columns.Add(new TableColumn { Width = new GridLength(48) });
            table.Columns.Add(new TableColumn { Width = new GridLength(55) });
        }
        else
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(32) });
            table.Columns.Add(new TableColumn { Width = new GridLength(200) });
            table.Columns.Add(new TableColumn { Width = new GridLength(95) });
            table.Columns.Add(new TableColumn { Width = new GridLength(80) });
            table.Columns.Add(new TableColumn { Width = new GridLength(60) });
            table.Columns.Add(new TableColumn { Width = new GridLength(75) });
            table.Columns.Add(new TableColumn { Width = new GridLength(95) });
        }

        var rg = new TableRowGroup();
        var hdrRow = new TableRow { Background = Brushes.WhiteSmoke };
        if (thermal)
        {
            hdrRow.Cells.Add(MakeCell("Item", true));
            hdrRow.Cells.Add(MakeCell("Qty", true, TextAlignment.Right));
            hdrRow.Cells.Add(MakeCell("Cost", true, TextAlignment.Right));
            hdrRow.Cells.Add(MakeCell("Total", true, TextAlignment.Right));
        }
        else
        {
            hdrRow.Cells.Add(MakeCell("#", true));
            hdrRow.Cells.Add(MakeCell("Product", true));
            hdrRow.Cells.Add(MakeCell("Batch", true));
            hdrRow.Cells.Add(MakeCell("Expiry", true));
            hdrRow.Cells.Add(MakeCell("Qty+B", true, TextAlignment.Right));
            hdrRow.Cells.Add(MakeCell("Unit Cost", true, TextAlignment.Right));
            hdrRow.Cells.Add(MakeCell("Net Total", true, TextAlignment.Right));
        }
        rg.Rows.Add(hdrRow);

        for (int i = 0; i < detail.Items.Count; i++)
        {
            var it = detail.Items[i];
            var tr = new TableRow();
            var qtyLabel = it.UnitBonus > 0 ? $"{it.UnitQuantity}+{it.UnitBonus}" : $"{it.UnitQuantity}";
            if (thermal)
            {
                tr.Cells.Add(MakeCell(it.ProductName, false));
                tr.Cells.Add(MakeCell(qtyLabel, false, TextAlignment.Right));
                tr.Cells.Add(MakeCell($"{it.UnitPurchasePrice:N2}", false, TextAlignment.Right));
                tr.Cells.Add(MakeCell($"{it.SubTotal:N2}", false, TextAlignment.Right));
            }
            else
            {
                tr.Cells.Add(MakeCell($"{i + 1}", false));
                tr.Cells.Add(MakeCell(it.ProductName, false));
                tr.Cells.Add(MakeCell(string.IsNullOrWhiteSpace(it.Batch) ? "—" : it.Batch, false));
                tr.Cells.Add(MakeCell(it.Expiry.HasValue ? $"{it.Expiry.Value:dd-MM-yyyy}" : "—", false));
                tr.Cells.Add(MakeCell(qtyLabel, false, TextAlignment.Right));
                tr.Cells.Add(MakeCell($"{it.UnitPurchasePrice:N2}", false, TextAlignment.Right));
                tr.Cells.Add(MakeCell($"{it.SubTotal:N2}", false, TextAlignment.Right));
            }
            rg.Rows.Add(tr);
        }

        table.RowGroups.Add(rg);
        doc.Blocks.Add(table);

        var totalsPara = new Paragraph
        {
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        totalsPara.Inlines.Add(new Run($"Gross Total: {store.CurrencySymbol} {detail.GrossTotal:N2}\n"));
        if (detail.DiscountAmount > 0m)
            totalsPara.Inlines.Add(new Run($"Discount: -{store.CurrencySymbol} {detail.DiscountAmount:N2}\n"));
        if (detail.TaxAmount > 0m)
            totalsPara.Inlines.Add(new Run($"Tax: +{store.CurrencySymbol} {detail.TaxAmount:N2}\n"));
        totalsPara.Inlines.Add(new Bold(new Run($"Net Total: {store.CurrencySymbol} {detail.TotalAmount:N2}\n")) { FontSize = thermal ? 11.5 : 13.5 });
        totalsPara.Inlines.Add(new Run($"Paid: {store.CurrencySymbol} {detail.TotalPaid:N2}  |  Payable Due: {store.CurrencySymbol} {detail.RemainingAmount:N2}"));
        doc.Blocks.Add(totalsPara);

        return doc;

        static TableCell MakeCell(string text, bool bold, TextAlignment align = TextAlignment.Left)
        {
            var run = new Run(text);
            Inline inlineContent = bold ? new Bold(run) : run;
            var p = new Paragraph(inlineContent)
            {
                Margin = new Thickness(3, 3, 3, 3),
                TextAlignment = align
            };
            return new TableCell(p);
        }
    }
}
