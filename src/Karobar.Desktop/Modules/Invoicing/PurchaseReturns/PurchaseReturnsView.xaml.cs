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
    private bool _isDraggingProductModal;
    private Point _productModalDragStart;
    private double _productModalStartLeft;
    private double _productModalStartTop;

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
        }

        _wiredVm = e.NewValue as PurchaseReturnsViewModel;

        if (_wiredVm != null)
        {
            _wiredVm.RequestFocusField += OnRequestFocusField;
            _wiredVm.RequestPrintDocument += OnRequestPrintDocument;
            _wiredVm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_wiredVm == null) return;

        if (e.PropertyName == nameof(PurchaseReturnsViewModel.IsSupplierSearchModalOpen) && _wiredVm.IsSupplierSearchModalOpen)
        {
            Dispatcher.BeginInvoke(() =>
            {
                SupplierModalSearchBox.Focus();
                SupplierModalSearchBox.SelectAll();
            }, DispatcherPriority.Input);
        }
        else if (e.PropertyName == nameof(PurchaseReturnsViewModel.IsInvoiceSearchModalOpen) && _wiredVm.IsInvoiceSearchModalOpen)
        {
            Dispatcher.BeginInvoke(() =>
            {
                InvoiceModalSearchBox.Focus();
                InvoiceModalSearchBox.SelectAll();
            }, DispatcherPriority.Input);
        }
        else if (e.PropertyName == nameof(PurchaseReturnsViewModel.IsProductSearchModalOpen) && _wiredVm.IsProductSearchModalOpen)
        {
            Dispatcher.BeginInvoke(() =>
            {
                ProductModalSearchBox.Focus();
                ProductModalSearchBox.SelectAll();
            }, DispatcherPriority.Input);
        }
        else if (e.PropertyName == nameof(PurchaseReturnsViewModel.IsBatchSearchModalOpen) && _wiredVm.IsBatchSearchModalOpen)
        {
            Dispatcher.BeginInvoke(() =>
            {
                BatchModalSearchBox.Focus();
                BatchModalSearchBox.SelectAll();
            }, DispatcherPriority.Input);
        }
        else if (e.PropertyName == nameof(PurchaseReturnsViewModel.IsDeleteModalOpen) && _wiredVm.IsDeleteModalOpen)
        {
            Dispatcher.BeginInvoke(() =>
            {
                DeletePasswordBox.Focus();
                DeletePasswordBox.SelectAll();
            }, DispatcherPriority.Input);
        }
    }

    // =========================================================================
    // 1. IPageKeyboardHandler IMPLEMENTATION
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
            DirectorySearchPostedBox.Focus();
            DirectorySearchPostedBox.SelectAll();
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
    // 2. PAGE & REPEATER KEYBOARD SHORTCUTS
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
                _ = Vm.OpenBatchSearchForRowAsync(Vm.ActiveRowIndex);
                return;
            }
        }
        else if (Vm.IsDirectoryMode && isCtrl && key == Key.N)
        {
            e.Handled = true;
            _ = Vm.OpenCreateFormAsync();
        }
    }

    private void DirectoryFilterBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Vm != null)
        {
            e.Handled = true;
            _ = Vm.SearchDirectoryAsync();
        }
    }

    private void DirectoryGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm?.SelectedDirectoryRow != null)
        {
            _ = Vm.ViewReturnDetailAsync(Vm.SelectedDirectoryRow);
        }
    }

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

        if (e.Key == Key.Enter || e.Key == Key.Space)
        {
            e.Handled = true;
            await Vm.OpenProductSearchForRowAsync(rowIndex);
        }
        else if (e.Key == Key.Down)
        {
            e.Handled = true;
            if (rowIndex == Vm.Lines.Count - 1)
                Vm.AddRow();
            else
                OnRequestFocusField("product", rowIndex + 1);
        }
        else if (e.Key == Key.Up && rowIndex > 0)
        {
            e.Handled = true;
            OnRequestFocusField("product", rowIndex - 1);
        }
    }

    private async void BatchCellButton_Click(object sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var rowIndex = GetRowIndexFromElement(sender as FrameworkElement);
        if (rowIndex >= 0)
        {
            await Vm.OpenBatchSearchForRowAsync(rowIndex);
        }
    }

    private async void BatchCellButton_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null) return;
        var rowIndex = GetRowIndexFromElement(sender as FrameworkElement);
        if (rowIndex < 0) return;
        Vm.ActiveRowIndex = rowIndex;

        if (e.Key == Key.Enter || e.Key == Key.Space)
        {
            e.Handled = true;
            await Vm.OpenBatchSearchForRowAsync(rowIndex);
        }
    }

    private void LineTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            var idx = GetRowIndexFromElement(tb);
            if (idx >= 0 && Vm != null)
                Vm.ActiveRowIndex = idx;
            tb.SelectAll();
        }
    }

    private void LineTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null || sender is not TextBox tb) return;
        var rowIndex = GetRowIndexFromElement(tb);
        if (rowIndex < 0) return;

        var fieldTag = tb.Tag as string ?? string.Empty;

        // Sequential Enter Chain matching PurchaseReturnForm.jsx lines 1251-1264:
        // pack_quantity -> pack_purchase_price -> item_discount -> next row product
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
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
                if (rowIndex == Vm.Lines.Count - 1)
                {
                    Vm.AddRow();
                }
                else
                {
                    OnRequestFocusField("product", rowIndex + 1);
                }
            }
            return;
        }

        if (e.Key == Key.Down)
        {
            e.Handled = true;
            if (rowIndex == Vm.Lines.Count - 1)
            {
                Vm.AddRow();
            }
            else
            {
                OnRequestFocusField(fieldTag, rowIndex + 1);
            }
            return;
        }

        if (e.Key == Key.Up && rowIndex > 0)
        {
            e.Handled = true;
            OnRequestFocusField(fieldTag, rowIndex - 1);
        }
    }

    private void OnRequestFocusField(string fieldTag, int rowIndex)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (Vm == null || rowIndex < 0 || rowIndex >= Vm.Lines.Count)
                return;

            var container = LinesRepeater.ItemContainerGenerator.ContainerFromIndex(rowIndex) as DependencyObject;
            if (container == null)
            {
                LinesRepeater.UpdateLayout();
                container = LinesRepeater.ItemContainerGenerator.ContainerFromIndex(rowIndex) as DependencyObject;
            }
            if (container == null) return;

            var target = FindChildByTag(container, fieldTag);
            if (target is TextBox tb)
            {
                tb.Focus();
                tb.SelectAll();
            }
            else if (target is UIElement ui)
            {
                ui.Focus();
            }
        }, DispatcherPriority.Loaded);
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
    // 3. MODAL KEYBOARD & MOUSE INTERACTIONS
    // =========================================================================

    private void SupplierModalSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null) return;
        if (e.Key == Key.Down && Vm.FilteredSuppliers.Count > 0)
        {
            e.Handled = true;
            var idx = Vm.SelectedSupplierInModal != null ? Vm.FilteredSuppliers.IndexOf(Vm.SelectedSupplierInModal) : -1;
            Vm.SelectedSupplierInModal = Vm.FilteredSuppliers[Math.Min(Vm.FilteredSuppliers.Count - 1, idx + 1)];
            SupplierModalGrid.ScrollIntoView(Vm.SelectedSupplierInModal);
        }
        else if (e.Key == Key.Up && Vm.FilteredSuppliers.Count > 0)
        {
            e.Handled = true;
            var idx = Vm.SelectedSupplierInModal != null ? Vm.FilteredSuppliers.IndexOf(Vm.SelectedSupplierInModal) : 0;
            Vm.SelectedSupplierInModal = Vm.FilteredSuppliers[Math.Max(0, idx - 1)];
            SupplierModalGrid.ScrollIntoView(Vm.SelectedSupplierInModal);
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
        }
    }

    private void SupplierModalGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm?.SelectedSupplierInModal != null)
        {
            Vm.SelectSupplierFromModal(Vm.SelectedSupplierInModal);
        }
    }

    private async void InvoiceModalSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null) return;
        if (e.Key == Key.Down && Vm.InvoiceSearchResults.Count > 0)
        {
            e.Handled = true;
            var idx = Vm.SelectedInvoiceInModal != null ? Vm.InvoiceSearchResults.IndexOf(Vm.SelectedInvoiceInModal) : -1;
            Vm.SelectedInvoiceInModal = Vm.InvoiceSearchResults[Math.Min(Vm.InvoiceSearchResults.Count - 1, idx + 1)];
            InvoiceModalGrid.ScrollIntoView(Vm.SelectedInvoiceInModal);
        }
        else if (e.Key == Key.Up && Vm.InvoiceSearchResults.Count > 0)
        {
            e.Handled = true;
            var idx = Vm.SelectedInvoiceInModal != null ? Vm.InvoiceSearchResults.IndexOf(Vm.SelectedInvoiceInModal) : 0;
            Vm.SelectedInvoiceInModal = Vm.InvoiceSearchResults[Math.Max(0, idx - 1)];
            InvoiceModalGrid.ScrollIntoView(Vm.SelectedInvoiceInModal);
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
        }
    }

    private async void InvoiceModalGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm?.SelectedInvoiceInModal != null)
        {
            await Vm.SelectInvoiceFromModalAsync(Vm.SelectedInvoiceInModal);
        }
    }

    private async void ProductModalSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null) return;
        if (e.Key == Key.Down && Vm.ProductSearchResults.Count > 0)
        {
            e.Handled = true;
            var idx = Vm.SelectedProductInModal != null ? Vm.ProductSearchResults.IndexOf(Vm.SelectedProductInModal) : -1;
            Vm.SelectedProductInModal = Vm.ProductSearchResults[Math.Min(Vm.ProductSearchResults.Count - 1, idx + 1)];
            ProductModalGrid.ScrollIntoView(Vm.SelectedProductInModal);
        }
        else if (e.Key == Key.Up && Vm.ProductSearchResults.Count > 0)
        {
            e.Handled = true;
            var idx = Vm.SelectedProductInModal != null ? Vm.ProductSearchResults.IndexOf(Vm.SelectedProductInModal) : 0;
            Vm.SelectedProductInModal = Vm.ProductSearchResults[Math.Max(0, idx - 1)];
            ProductModalGrid.ScrollIntoView(Vm.SelectedProductInModal);
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await Vm.SelectProductFromModalAsync(Vm.SelectedProductInModal);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Vm.CloseProductSearchModal();
        }
    }

    private async void ProductModalGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm?.SelectedProductInModal != null)
        {
            await Vm.SelectProductFromModalAsync(Vm.SelectedProductInModal);
        }
    }

    private void BatchModalSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null) return;
        if (e.Key == Key.Down && Vm.BatchSearchResults.Count > 0)
        {
            e.Handled = true;
            var idx = Vm.SelectedBatchInModal != null ? Vm.BatchSearchResults.IndexOf(Vm.SelectedBatchInModal) : -1;
            Vm.SelectedBatchInModal = Vm.BatchSearchResults[Math.Min(Vm.BatchSearchResults.Count - 1, idx + 1)];
            BatchModalGrid.ScrollIntoView(Vm.SelectedBatchInModal);
        }
        else if (e.Key == Key.Up && Vm.BatchSearchResults.Count > 0)
        {
            e.Handled = true;
            var idx = Vm.SelectedBatchInModal != null ? Vm.BatchSearchResults.IndexOf(Vm.SelectedBatchInModal) : 0;
            Vm.SelectedBatchInModal = Vm.BatchSearchResults[Math.Max(0, idx - 1)];
            BatchModalGrid.ScrollIntoView(Vm.SelectedBatchInModal);
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Vm.SelectBatchFromModal(Vm.SelectedBatchInModal);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Vm.CloseBatchSearchModal();
        }
    }

    private void BatchModalGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm?.SelectedBatchInModal != null)
        {
            Vm.SelectBatchFromModal(Vm.SelectedBatchInModal);
        }
    }

    // Draggable & Resizable Product Search Modal Handlers
    private void ProductModalHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement header)
        {
            _isDraggingProductModal = true;
            _productModalDragStart = e.GetPosition(this);
            _productModalStartLeft = Canvas.GetLeft(ProductModalDialog);
            _productModalStartTop = Canvas.GetTop(ProductModalDialog);
            header.CaptureMouse();
            e.Handled = true;
        }
    }

    private void ProductModalHeader_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingProductModal) return;
        var pos = e.GetPosition(this);
        var dx = pos.X - _productModalDragStart.X;
        var dy = pos.Y - _productModalDragStart.Y;
        Canvas.SetLeft(ProductModalDialog, Math.Max(0, _productModalStartLeft + dx));
        Canvas.SetTop(ProductModalDialog, Math.Max(0, _productModalStartTop + dy));
    }

    private void ProductModalHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingProductModal && sender is UIElement header)
        {
            _isDraggingProductModal = false;
            header.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void ProductModalResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ProductModalDialog.Width = Math.Max(ProductModalDialog.MinWidth, ProductModalDialog.Width + e.HorizontalChange);
        ProductModalDialog.Height = Math.Max(ProductModalDialog.MinHeight, ProductModalDialog.Height + e.VerticalChange);
    }

    // =========================================================================
    // 4. NATIVE PURCHASE RETURN PRINT / DEBIT NOTE ENGINE
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
