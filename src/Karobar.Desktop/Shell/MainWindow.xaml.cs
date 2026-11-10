using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Karobar.Desktop.Core.Navigation;
using Karobar.Desktop.ViewModels;

namespace Karobar.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        Loaded += (_, _) => UsernameTextBox.Focus();
    }

    private void PasswordInput_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
        {
            _viewModel.Password = pb.Password;
        }
    }

    private IPageKeyboardHandler? GetActivePageHandler()
    {
        if (_viewModel.IsSaleInvoicesPage) return SaleInvoicesPageView;
        if (_viewModel.IsPurchaseReturnsPage) return PurchaseReturnsPageView;
        if (_viewModel.IsPurchaseInvoicesPage) return PurchaseInvoicesPageView;
        if (_viewModel.IsBarcodeStudioPage) return BarcodeStudioPageView;
        if (_viewModel.IsProductBatchesPage) return ProductBatchesPageView;
        if (_viewModel.IsProductsPage) return ProductsPageView;
        if (_viewModel.IsCustomersPage) return CustomersPageView;
        if (_viewModel.IsSuppliersPage) return SuppliersPageView;
        if (_viewModel.IsBrandsPage) return BrandsPageView;
        if (_viewModel.IsCategoriesPage) return CategoriesPageView;
        if (_viewModel.IsDashboardPage) return DashboardPageView;
        return null;
    }

    private static void FlushFocusedTextBoxBinding()
    {
        if (Keyboard.FocusedElement is TextBox tb)
        {
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }
    }

    private void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || !_viewModel.IsAuthenticated)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        var isCtrl = modifiers == ModifierKeys.Control;
        var isAlt = modifiers == ModifierKeys.Alt;
        var isNoMod = modifiers == ModifierKeys.None;
        var isShiftOnly = modifiers == ModifierKeys.Shift;
        var isTypingInInput = Keyboard.FocusedElement is TextBox or PasswordBox;

        var pageHandler = GetActivePageHandler();

        // 1. Save / Confirm: Ctrl+S, Alt+S, or Ctrl+Enter
        if ((isCtrl && key == Key.S) || (isAlt && key == Key.S) || (isCtrl && key == Key.Enter))
        {
            FlushFocusedTextBoxBinding();
            if (pageHandler?.HandleSaveShortcut() == true)
            {
                e.Handled = true;
                return;
            }
        }

        // 2. New / Add: Ctrl+N or Alt+N
        if ((isCtrl && key == Key.N) || (isAlt && key == Key.N))
        {
            if (pageHandler?.HandleNewShortcut() == true)
            {
                e.Handled = true;
                return;
            }
        }

        // 3. Focus Live Search: Ctrl+F (or '/' when not typing in an input)
        if ((isCtrl && key == Key.F) ||
            (!isTypingInInput && isNoMod && (key == Key.OemQuestion || key == Key.Divide)))
        {
            if (pageHandler?.HandleSearchShortcut() == true)
            {
                e.Handled = true;
                return;
            }
        }

        // 4. Escape / Cancel: Esc or Alt+C
        if ((isNoMod && key == Key.Escape) || (isAlt && key == Key.C))
        {
            if (pageHandler?.HandleEscapeShortcut() == true)
            {
                e.Handled = true;
                return;
            }
        }

        // 5. Refresh: F5 or Ctrl+R
        if ((isNoMod && key == Key.F5) || (isCtrl && key == Key.R))
        {
            if (pageHandler?.HandleRefreshShortcut() == true)
            {
                e.Handled = true;
                return;
            }
        }

        // 6. CSV Import: Ctrl+I
        if (isCtrl && key == Key.I)
        {
            if (pageHandler?.HandleImportShortcut() == true)
            {
                e.Handled = true;
                return;
            }
        }

        // 7. CSV Export: Ctrl+E
        if (isCtrl && key == Key.E)
        {
            if (pageHandler?.HandleExportShortcut() == true)
            {
                e.Handled = true;
                return;
            }
        }

        // 7b. Print Labels / Vouchers: Ctrl+P
        if (isCtrl && key == Key.P)
        {
            if (_viewModel.IsBarcodeStudioPage)
            {
                FlushFocusedTextBoxBinding();
                BarcodeStudioPageView.ExecutePrintLabels();
                e.Handled = true;
                return;
            }
            if (_viewModel.IsPurchaseInvoicesPage && PurchaseInvoicesPageView.HandleExportShortcut())
            {
                e.Handled = true;
                return;
            }
            if (_viewModel.IsSaleInvoicesPage && SaleInvoicesPageView.HandlePrintShortcut())
            {
                e.Handled = true;
                return;
            }
        }

        // 8. Global Workspace & Quick-Jump Shortcuts
        if ((isCtrl && key == Key.K) || (isAlt && key == Key.K))
        {
            _viewModel.IsSidebarCollapsed = false;
            Dispatcher.BeginInvoke(() =>
            {
                SidebarSearchBox.Focus();
                Keyboard.Focus(SidebarSearchBox);
                SidebarSearchBox.SelectAll();
            }, DispatcherPriority.Input);
            e.Handled = true;
            return;
        }

        if (isAlt && key == Key.P && !_viewModel.IsSaleInvoicesPage && !_viewModel.IsPurchaseInvoicesPage && !_viewModel.IsPurchaseReturnsPage)
        {
            _viewModel.Navigate("/products");
            ProductsPageView.FocusSearchInput();
            e.Handled = true;
            return;
        }

        if (isAlt && key == Key.B && !_viewModel.IsSaleInvoicesPage && !_viewModel.IsPurchaseInvoicesPage && !_viewModel.IsPurchaseReturnsPage)
        {
            _viewModel.Navigate("/barcode-labels");
            BarcodeStudioPageView.HandleSearchShortcut();
            e.Handled = true;
            return;
        }

        if (isAlt && key == Key.G)
        {
            _viewModel.Navigate("/guide");
            e.Handled = true;
            return;
        }

        if ((isNoMod && key == Key.F1) ||
            (isCtrl && key == Key.OemQuestion) ||
            (!isTypingInInput && isShiftOnly && key == Key.OemQuestion))
        {
            _viewModel.Navigate("/shortcuts");
            e.Handled = true;
            return;
        }

        if (!isTypingInInput && isAlt)
        {
            if (key is Key.D1 or Key.NumPad1)
            {
                _viewModel.Navigate("/purchase-invoices/create");
                e.Handled = true;
            }
            else if (key is Key.D2 or Key.NumPad2)
            {
                _viewModel.Navigate("/sale-invoices/create/retail");
                e.Handled = true;
            }
            else if (key == Key.W && !_viewModel.IsSaleInvoicesPage)
            {
                _viewModel.Navigate("/sale-invoices/create/wholesale");
                e.Handled = true;
            }
            else if (key == Key.D && !_viewModel.IsPurchaseInvoicesPage && !_viewModel.IsSaleInvoicesPage)
            {
                _viewModel.Navigate("/cash-drawer");
                e.Handled = true;
            }
            else if (key == Key.A && !_viewModel.IsPurchaseReturnsPage)
            {
                _viewModel.Navigate("/accounts");
                e.Handled = true;
            }
        }
    }
}
