using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Karobar.Desktop.Core.Navigation;
using Karobar.Desktop.ViewModels;

namespace Karobar.Desktop.Views;

public partial class CustomersView : UserControl, IPageKeyboardHandler
{
    public CustomersView()
    {
        InitializeComponent();
    }

    public bool HandleNewShortcut()
    {
        if (DataContext is not CustomersViewModel vm || !vm.IsDirectoryMode || !vm.CanCreate)
            return false;

        if (vm.OpenCreateModeCommand.CanExecute(null))
        {
            vm.OpenCreateModeCommand.Execute(null);
            Dispatcher.BeginInvoke(() =>
            {
                CustomerFormNameTextBox?.Focus();
                if (CustomerFormNameTextBox != null)
                {
                    Keyboard.Focus(CustomerFormNameTextBox);
                    CustomerFormNameTextBox.SelectAll();
                }
            }, DispatcherPriority.Background);
            return true;
        }

        return false;
    }

    public bool HandleSaveShortcut()
    {
        if (DataContext is not CustomersViewModel vm)
            return false;

        if (vm.IsReminderModalOpen && vm.SendViaWhatsAppCommand.CanExecute(null))
        {
            vm.SendViaWhatsAppCommand.Execute(null);
            return true;
        }

        if (vm.IsPriceModalOpen && vm.SaveCustomWholesalePriceCommand.CanExecute(null))
        {
            vm.SaveCustomWholesalePriceCommand.Execute(null);
            return true;
        }

        if (vm.IsFormMode && vm.SaveCustomerCommand.CanExecute(null))
        {
            vm.SaveCustomerCommand.Execute(null);
            return true;
        }

        return false;
    }

    public bool HandleSearchShortcut()
    {
        if (DataContext is not CustomersViewModel vm || !vm.IsDirectoryMode)
            return false;

        Dispatcher.BeginInvoke(() =>
        {
            CustomerSearchTextBox?.Focus();
            if (CustomerSearchTextBox != null)
            {
                Keyboard.Focus(CustomerSearchTextBox);
                CustomerSearchTextBox.SelectAll();
            }
        }, DispatcherPriority.Input);
        return true;
    }

    public bool HandleEscapeShortcut()
    {
        if (DataContext is not CustomersViewModel vm)
            return false;

        if (vm.IsReminderModalOpen)
        {
            vm.CloseReminderModalCommand.Execute(null);
            return true;
        }

        if (vm.IsCreditModalOpen)
        {
            vm.CloseCreditModalCommand.Execute(null);
            return true;
        }

        if (vm.IsPriceModalOpen)
        {
            vm.CloseWholesalePriceModalCommand.Execute(null);
            return true;
        }

        if (vm.IsImportModalOpen)
        {
            vm.CloseImportModalCommand.Execute(null);
            return true;
        }

        if (vm.IsFormMode || vm.IsDetailMode)
        {
            vm.BackToDirectoryCommand.Execute(null);
            return true;
        }

        if (vm.IsDirectoryMode && !string.IsNullOrEmpty(vm.SearchQuery))
        {
            vm.SearchQuery = string.Empty;
            return true;
        }

        return false;
    }

    public bool HandleRefreshShortcut()
    {
        if (DataContext is not CustomersViewModel vm)
            return false;

        if (vm.LoadCustomersCommand.CanExecute(null))
        {
            vm.LoadCustomersCommand.Execute(null);
            return true;
        }

        return false;
    }

    public bool HandleImportShortcut()
    {
        if (DataContext is not CustomersViewModel vm || !vm.IsDirectoryMode || !vm.CanImport)
            return false;

        if (vm.OpenImportModalCommand.CanExecute(null))
        {
            vm.OpenImportModalCommand.Execute(null);
            return true;
        }

        return false;
    }

    public bool HandleExportShortcut()
    {
        if (DataContext is not CustomersViewModel vm || !vm.IsDirectoryMode || !vm.CanExport)
            return false;

        if (vm.ExportCsvCommand.CanExecute(null))
        {
            vm.ExportCsvCommand.Execute(null);
            return true;
        }

        return false;
    }

    private void CustomersView_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Handled centrally via MainWindow -> IPageKeyboardHandler
    }
}
