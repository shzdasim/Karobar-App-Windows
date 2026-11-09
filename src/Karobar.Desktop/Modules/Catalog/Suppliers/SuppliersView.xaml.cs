using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Karobar.Desktop.Core.Navigation;
using Karobar.Desktop.ViewModels;

namespace Karobar.Desktop.Views;

public partial class SuppliersView : UserControl, IPageKeyboardHandler
{
    public SuppliersView()
    {
        InitializeComponent();
    }

    public bool HandleNewShortcut()
    {
        if (DataContext is not SuppliersViewModel vm || !vm.IsDirectoryMode || !vm.CanCreate)
            return false;

        if (vm.OpenCreateModeCommand.CanExecute(null))
        {
            vm.OpenCreateModeCommand.Execute(null);
            Dispatcher.BeginInvoke(() =>
            {
                SupplierFormNameTextBox?.Focus();
                if (SupplierFormNameTextBox != null)
                {
                    Keyboard.Focus(SupplierFormNameTextBox);
                    SupplierFormNameTextBox.SelectAll();
                }
            }, DispatcherPriority.Background);
            return true;
        }

        return false;
    }

    public bool HandleSaveShortcut()
    {
        if (DataContext is not SuppliersViewModel vm)
            return false;

        if (vm.IsFormMode && vm.SaveSupplierCommand.CanExecute(null))
        {
            vm.SaveSupplierCommand.Execute(null);
            return true;
        }

        return false;
    }

    public bool HandleSearchShortcut()
    {
        if (DataContext is not SuppliersViewModel vm || !vm.IsDirectoryMode)
            return false;

        Dispatcher.BeginInvoke(() =>
        {
            SupplierSearchTextBox?.Focus();
            if (SupplierSearchTextBox != null)
            {
                Keyboard.Focus(SupplierSearchTextBox);
                SupplierSearchTextBox.SelectAll();
            }
        }, DispatcherPriority.Input);
        return true;
    }

    public bool HandleEscapeShortcut()
    {
        if (DataContext is not SuppliersViewModel vm)
            return false;

        if (vm.IsCreditModalOpen)
        {
            vm.CloseCreditModalCommand.Execute(null);
            return true;
        }

        if (vm.IsImportModalOpen)
        {
            vm.CloseImportModalCommand.Execute(null);
            return true;
        }

        if (vm.IsFormMode)
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
        if (DataContext is not SuppliersViewModel vm)
            return false;

        if (vm.LoadSuppliersCommand.CanExecute(null))
        {
            vm.LoadSuppliersCommand.Execute(null);
            return true;
        }

        return false;
    }

    public bool HandleImportShortcut()
    {
        if (DataContext is not SuppliersViewModel vm || !vm.IsDirectoryMode || !vm.CanImport)
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
        if (DataContext is not SuppliersViewModel vm || !vm.IsDirectoryMode || !vm.CanExport)
            return false;

        if (vm.ExportCsvCommand.CanExecute(null))
        {
            vm.ExportCsvCommand.Execute(null);
            return true;
        }

        return false;
    }

    private void SuppliersView_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Handled centrally via MainWindow -> IPageKeyboardHandler
    }
}
