using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Karobar.Desktop.Core.Navigation;
using Karobar.Desktop.ViewModels;

namespace Karobar.Desktop.Views;

public partial class BrandsView : UserControl, IPageKeyboardHandler
{
    public BrandsView()
    {
        InitializeComponent();
    }

    public bool HandleNewShortcut()
    {
        if (DataContext is not BrandsViewModel vm || !vm.IsDirectoryMode || !vm.CanCreate)
            return false;

        if (vm.OpenCreateModeCommand.CanExecute(null))
        {
            vm.OpenCreateModeCommand.Execute(null);
            Dispatcher.BeginInvoke(() =>
            {
                BrandNameInputBox?.Focus();
                if (BrandNameInputBox != null)
                {
                    Keyboard.Focus(BrandNameInputBox);
                    BrandNameInputBox.SelectAll();
                }
            }, DispatcherPriority.Background);
            return true;
        }

        return false;
    }

    public bool HandleSaveShortcut()
    {
        if (DataContext is not BrandsViewModel vm)
            return false;

        if (vm.IsFormMode && vm.SaveBrandCommand.CanExecute(null))
        {
            vm.SaveBrandCommand.Execute(null);
            return true;
        }

        return false;
    }

    public bool HandleSearchShortcut()
    {
        if (DataContext is not BrandsViewModel vm || !vm.IsDirectoryMode)
            return false;

        Dispatcher.BeginInvoke(() =>
        {
            SearchBrandsBox?.Focus();
            if (SearchBrandsBox != null)
            {
                Keyboard.Focus(SearchBrandsBox);
                SearchBrandsBox.SelectAll();
            }
        }, DispatcherPriority.Input);
        return true;
    }

    public bool HandleEscapeShortcut()
    {
        if (DataContext is not BrandsViewModel vm)
            return false;

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
        if (DataContext is not BrandsViewModel vm)
            return false;

        if (vm.LoadBrandsCommand.CanExecute(null))
        {
            vm.LoadBrandsCommand.Execute(null);
            return true;
        }

        return false;
    }

    public bool HandleImportShortcut()
    {
        if (DataContext is not BrandsViewModel vm || !vm.IsDirectoryMode || !vm.CanImport)
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
        if (DataContext is not BrandsViewModel vm || !vm.IsDirectoryMode || !vm.CanExport)
            return false;

        if (vm.ExportCsvCommand.CanExecute(null))
        {
            vm.ExportCsvCommand.Execute(null);
            return true;
        }

        return false;
    }

    private void BrandFormInput_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is BrandsViewModel vm)
        {
            e.Handled = true;
            if (vm.SaveBrandCommand.CanExecute(null))
            {
                vm.SaveBrandCommand.Execute(null);
            }
        }
    }
}
