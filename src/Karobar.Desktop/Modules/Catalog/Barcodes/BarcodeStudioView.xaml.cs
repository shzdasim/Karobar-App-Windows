using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Karobar.Desktop.Core.Navigation;
using Karobar.Desktop.ViewModels;

namespace Karobar.Desktop.Views;

public partial class BarcodeStudioView : UserControl, IPageKeyboardHandler
{
    private const double DipPerMm = 96.0 / 25.4;

    public BarcodeStudioView()
    {
        InitializeComponent();
    }

    private sealed record PrintableStickerPayload(
        string StoreName,
        string ProductName,
        string ProductCode,
        string Barcode,
        string BatchText,
        string ExpiryText,
        string PriceText,
        string FooterText);

    // =========================================================================
    // Product Search Combobox & DatePicker Event Handlers
    // =========================================================================

    private void ProductSearchTextBox_OnGotFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is BarcodeStudioViewModel vm && vm.ProductSearchResults.Count > 0)
        {
            vm.IsProductDropdownOpen = true;
        }
    }

    private async void ProductSearchTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not BarcodeStudioViewModel vm)
            return;

        if (e.Key == Key.Down)
        {
            vm.MoveProductSearchHighlight(1);
            if (vm.HighlightedSearchProduct != null)
                ProductSearchListBox.ScrollIntoView(vm.HighlightedSearchProduct);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            vm.MoveProductSearchHighlight(-1);
            if (vm.HighlightedSearchProduct != null)
                ProductSearchListBox.ScrollIntoView(vm.HighlightedSearchProduct);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await vm.CommitHighlightedSearchProductAsync();
        }
        else if (e.Key == Key.Escape && vm.IsProductDropdownOpen)
        {
            vm.IsProductDropdownOpen = false;
            e.Handled = true;
        }
    }

    private void ProductSearchListBox_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is BarcodeStudioViewModel vm && ProductSearchListBox.SelectedItem is Karobar.Application.Interfaces.BarcodeProductLookupDto dto)
        {
            vm.SelectSearchProduct(dto);
            ProductSearchTextBox.Focus();
        }
    }

    private void CustomExpiryDatePicker_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is DatePicker dp && dp.Template?.FindName("PART_TextBox", dp) is DatePickerTextBox dpTextBox)
        {
            dpTextBox.ApplyTemplate();
            if (dpTextBox.Template?.FindName("PART_Watermark", dpTextBox) is ContentControl watermark)
            {
                watermark.Content = "dd-MM-yyyy";
            }
        }
    }

    // =========================================================================
    // Studio Keyboard Shortcuts & IPageKeyboardHandler
    // =========================================================================

    private async void BarcodeStudioView_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not BarcodeStudioViewModel vm)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;

        // Ctrl+P -> Print Stickers
        if (mods == ModifierKeys.Control && key == Key.P)
        {
            e.Handled = true;
            ExecutePrintLabels();
            return;
        }

        // F2 -> Focus Product Search
        if (mods == ModifierKeys.None && key == Key.F2)
        {
            e.Handled = true;
            HandleSearchShortcut();
            return;
        }

        // Ctrl+1 / Ctrl+2 / Ctrl+3 -> Switch Studio Tabs
        if (mods == ModifierKeys.Control)
        {
            if (key is Key.D1 or Key.NumPad1)
            {
                vm.ActiveTab = "queue";
                e.Handled = true;
                return;
            }
            if (key is Key.D2 or Key.NumPad2)
            {
                vm.ActiveTab = "designer";
                e.Handled = true;
                return;
            }
            if (key is Key.D3 or Key.NumPad3)
            {
                vm.ActiveTab = "paper";
                e.Handled = true;
                return;
            }
        }

        // Enter inside Save Preset Modal
        if (vm.IsSavePresetModalOpen && mods == ModifierKeys.None && key == Key.Enter)
        {
            e.Handled = true;
            await vm.ConfirmSaveNewPresetAsync();
        }
    }

    public bool HandleNewShortcut()
    {
        if (DataContext is not BarcodeStudioViewModel vm)
            return false;

        vm.OpenSavePresetModal();
        Dispatcher.BeginInvoke(() =>
        {
            NewPresetNameTextBox.Focus();
            Keyboard.Focus(NewPresetNameTextBox);
            NewPresetNameTextBox.SelectAll();
        }, DispatcherPriority.Input);
        return true;
    }

    public bool HandleSaveShortcut()
    {
        if (DataContext is not BarcodeStudioViewModel vm)
            return false;

        if (vm.IsSavePresetModalOpen)
        {
            _ = vm.ConfirmSaveNewPresetAsync();
        }
        else
        {
            _ = vm.UpdateActiveTemplateAsync();
        }
        return true;
    }

    public bool HandleSearchShortcut()
    {
        if (DataContext is not BarcodeStudioViewModel vm)
            return false;

        vm.ActiveTab = "queue";
        Dispatcher.BeginInvoke(() =>
        {
            MainStudioScrollViewer?.ScrollToTop();
            ProductSearchTextBox.Focus();
            Keyboard.Focus(ProductSearchTextBox);
            ProductSearchTextBox.SelectAll();
            if (vm.ProductSearchResults.Count > 0)
                vm.IsProductDropdownOpen = true;
        }, DispatcherPriority.Input);
        return true;
    }

    public bool HandleEscapeShortcut()
    {
        if (DataContext is not BarcodeStudioViewModel vm)
            return false;

        if (vm.IsSavePresetModalOpen)
        {
            vm.CloseSavePresetModal();
            return true;
        }

        if (vm.IsProductDropdownOpen)
        {
            vm.IsProductDropdownOpen = false;
            return true;
        }

        if (!string.IsNullOrEmpty(vm.ProductSearchText))
        {
            vm.ProductSearchText = string.Empty;
            return true;
        }

        return false;
    }

    public bool HandleRefreshShortcut()
    {
        if (DataContext is not BarcodeStudioViewModel vm)
            return false;

        _ = vm.RefreshStudioAsync();
        return true;
    }

    public bool HandleImportShortcut() => false;

    public bool HandleExportShortcut()
    {
        ExecutePrintLabels();
        return true;
    }

    // =========================================================================
    // Native Windows Vector Printing (Thermal Roll & A4/Letter Sheet)
    // =========================================================================

    private void PrintLabelsButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExecutePrintLabels();
    }

    public void ExecutePrintLabels()
    {
        if (DataContext is not BarcodeStudioViewModel vm)
            return;

        try
        {
            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true)
                return;

            var payloads = BuildPrintablePayloads(vm);
            var fixedDoc = BuildFixedDocument(vm, payloads);

            printDialog.PrintDocument(
                fixedDoc.DocumentPaginator,
                $"Karobar Barcode Labels ({payloads.Count})");

            vm.ErrorMessage = string.Empty;
            vm.StatusMessage = $"Sent {payloads.Count:N0} vector barcode label(s) to printer.";
        }
        catch (Exception ex)
        {
            vm.ErrorMessage = $"Printing failed: {ex.Message}";
        }
    }

    private static List<PrintableStickerPayload> BuildPrintablePayloads(BarcodeStudioViewModel vm)
    {
        var list = new List<PrintableStickerPayload>();
        var storeHeader = vm.PreviewStoreNameText;

        if (vm.PrintQueue.Count == 0)
        {
            // Print 1 calibration sample sticker if queue is empty
            list.Add(new PrintableStickerPayload(
                storeHeader,
                vm.PreviewProductNameText,
                vm.PreviewProductCodeText,
                vm.PreviewBarcodeValue,
                vm.PreviewBatchText,
                vm.PreviewExpiryText,
                vm.PreviewPriceText,
                vm.CustomFooter ?? string.Empty));
            return list;
        }

        foreach (var row in vm.PrintQueue)
        {
            var count = Math.Max(1, row.PrintQty);
            var codeSku = !string.IsNullOrWhiteSpace(row.ProductCode) ? $"SKU: {row.ProductCode}" : string.Empty;
            var batchStr = !string.IsNullOrWhiteSpace(row.BatchNumber) ? $"B:{row.BatchNumber}" : string.Empty;
            var expStr = row.ExpiryDate.HasValue
                ? $"Exp:{BarcodeStudioViewModel.FormatExpiryDate(row.ExpiryDate.Value, vm.ExpiryFormat)}"
                : string.Empty;
            var formattedPrice = vm.FormatCurrency(row.EffectivePrice);
            var priceStr = !string.IsNullOrWhiteSpace(vm.PricePrefix)
                ? $"{vm.PricePrefix.Trim()} {formattedPrice}"
                : formattedPrice;

            var payload = new PrintableStickerPayload(
                storeHeader,
                row.ProductName,
                codeSku,
                row.Barcode,
                batchStr,
                expStr,
                priceStr,
                vm.CustomFooter ?? string.Empty);

            for (var i = 0; i < count; i++)
                list.Add(payload);
        }

        return list;
    }

    private static FixedDocument BuildFixedDocument(
        BarcodeStudioViewModel vm,
        List<PrintableStickerPayload> payloads)
    {
        var doc = new FixedDocument();
        var stickerW = Math.Max(45.0, (double)vm.WidthMm * DipPerMm);
        var stickerH = Math.Max(30.0, (double)vm.HeightMm * DipPerMm);

        if (vm.IsRollMode)
        {
            doc.DocumentPaginator.PageSize = new Size(stickerW, stickerH);

            foreach (var payload in payloads)
            {
                var fixedPage = new FixedPage
                {
                    Width = stickerW,
                    Height = stickerH,
                    Background = Brushes.White
                };

                var card = BuildVectorStickerElement(vm, payload, stickerW, stickerH, isSheetCell: false);
                FixedPage.SetLeft(card, 0);
                FixedPage.SetTop(card, 0);
                fixedPage.Children.Add(card);

                fixedPage.Measure(new Size(stickerW, stickerH));
                fixedPage.Arrange(new Rect(0, 0, stickerW, stickerH));
                fixedPage.UpdateLayout();

                var pageContent = new PageContent();
                ((IAddChild)pageContent).AddChild(fixedPage);
                doc.Pages.Add(pageContent);
            }
        }
        else
        {
            var isLandscapeSheet = string.Equals(vm.Orientation, "landscape", StringComparison.OrdinalIgnoreCase);
            var pageW = (isLandscapeSheet ? 297.0 : 210.0) * DipPerMm;
            var pageH = (isLandscapeSheet ? 210.0 : 297.0) * DipPerMm;
            doc.DocumentPaginator.PageSize = new Size(pageW, pageH);

            var cols = Math.Max(1, vm.Columns);
            var rows = Math.Max(1, vm.Rows);
            var perSheet = cols * rows;

            var marginLeft = Math.Max(0.0, (double)vm.MarginLeftMm * DipPerMm);
            var marginTop = Math.Max(0.0, (double)vm.MarginTopMm * DipPerMm);
            var gapX = Math.Max(0.0, (double)vm.GapXMm * DipPerMm);
            var gapY = Math.Max(0.0, (double)vm.GapYMm * DipPerMm);

            // Prepend skipped slots on Sheet 1 for partial sticker sheet reuse
            var slots = new List<PrintableStickerPayload?>(vm.SkipLabelsCount + payloads.Count);
            for (var s = 0; s < vm.SkipLabelsCount; s++)
                slots.Add(null);
            foreach (var p in payloads)
                slots.Add(p);

            var totalPages = Math.Max(1, (int)Math.Ceiling((double)slots.Count / perSheet));
            for (var pageIndex = 0; pageIndex < totalPages; pageIndex++)
            {
                var fixedPage = new FixedPage
                {
                    Width = pageW,
                    Height = pageH,
                    Background = Brushes.White
                };

                var startSlot = pageIndex * perSheet;
                for (var cell = 0; cell < perSheet; cell++)
                {
                    var slotIndex = startSlot + cell;
                    if (slotIndex >= slots.Count)
                        break;

                    var payload = slots[slotIndex];
                    if (payload == null)
                        continue; // Skipped already-peeled sticker slot!

                    var r = cell / cols;
                    var c = cell % cols;
                    var x = marginLeft + c * (stickerW + gapX);
                    var y = marginTop + r * (stickerH + gapY);

                    var card = BuildVectorStickerElement(vm, payload, stickerW, stickerH, isSheetCell: true);
                    FixedPage.SetLeft(card, x);
                    FixedPage.SetTop(card, y);
                    fixedPage.Children.Add(card);
                }

                fixedPage.Measure(new Size(pageW, pageH));
                fixedPage.Arrange(new Rect(0, 0, pageW, pageH));
                fixedPage.UpdateLayout();

                var pageContent = new PageContent();
                ((IAddChild)pageContent).AddChild(fixedPage);
                doc.Pages.Add(pageContent);
            }
        }

        return doc;
    }

    private static FrameworkElement BuildVectorStickerElement(
        BarcodeStudioViewModel vm,
        PrintableStickerPayload payload,
        double widthDip,
        double heightDip,
        bool isSheetCell)
    {
        var pad = isSheetCell
            ? new Thickness(3.0, 2.0, 3.0, 2.0)
            : vm.StickerPaddingThickness;

        var container = new Border
        {
            Width = widthDip,
            Height = heightDip,
            Background = Brushes.White,
            Padding = pad,
            ClipToBounds = true,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };

        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center
        };

        var textAlign = vm.PreviewTextAlignment;
        var horizAlign = vm.PreviewHorizontalAlignment;

        if (vm.ShowStoreName && !string.IsNullOrWhiteSpace(payload.StoreName))
        {
            stack.Children.Add(new TextBlock
            {
                Text = payload.StoreName,
                FontSize = vm.StoreFontSize,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black,
                TextAlignment = textAlign,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        if (vm.ShowProductName && !string.IsNullOrWhiteSpace(payload.ProductName))
        {
            stack.Children.Add(new TextBlock
            {
                Text = payload.ProductName,
                FontSize = vm.ProductFontSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Black,
                TextAlignment = textAlign,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = vm.ProductNameMaxHeight,
                Margin = new Thickness(0, 1, 0, 0)
            });
        }

        if (vm.ShowProductCode && !string.IsNullOrWhiteSpace(payload.ProductCode))
        {
            stack.Children.Add(new TextBlock
            {
                Text = payload.ProductCode,
                FontSize = vm.CodeSkuFontSize,
                FontFamily = new FontFamily("Consolas"),
                Foreground = Brushes.Black,
                TextAlignment = textAlign
            });
        }

        if (vm.ShowBarcode)
        {
            stack.Children.Add(new BarcodeVectorControl
            {
                Value = payload.Barcode,
                BarHeight = vm.BarcodeHeight,
                BarWidth = vm.BarWidth,
                ShowText = vm.ShowBarcodeText,
                CodeFontSize = vm.BarcodeCodeFontSize,
                HorizontalAlignment = horizAlign,
                Margin = new Thickness(0, 2, 0, 1)
            });
        }

        var showBatchPart = vm.ShowBatch && !string.IsNullOrWhiteSpace(payload.BatchText);
        var showExpPart = vm.ShowExpiry && !string.IsNullOrWhiteSpace(payload.ExpiryText);
        if (showBatchPart || showExpPart || vm.ShowPrice)
        {
            var metaWrap = new WrapPanel
            {
                HorizontalAlignment = horizAlign,
                Margin = new Thickness(0, 1, 0, 0)
            };

            if (showBatchPart)
            {
                metaWrap.Children.Add(new TextBlock
                {
                    Text = payload.BatchText,
                    FontSize = vm.MetaFontSize,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.Black,
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            if (showExpPart)
            {
                metaWrap.Children.Add(new TextBlock
                {
                    Text = payload.ExpiryText,
                    FontSize = vm.MetaFontSize,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.Black,
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            if (vm.ShowPrice && !string.IsNullOrWhiteSpace(payload.PriceText))
            {
                metaWrap.Children.Add(new TextBlock
                {
                    Text = payload.PriceText,
                    FontSize = vm.PriceFontSize,
                    FontWeight = FontWeights.ExtraBold,
                    Foreground = Brushes.Black,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            stack.Children.Add(metaWrap);
        }

        if (!string.IsNullOrWhiteSpace(payload.FooterText))
        {
            stack.Children.Add(new TextBlock
            {
                Text = payload.FooterText,
                FontSize = vm.FooterFontSize,
                Foreground = Brushes.Black,
                TextAlignment = textAlign,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        container.Child = stack;
        return container;
    }
}
