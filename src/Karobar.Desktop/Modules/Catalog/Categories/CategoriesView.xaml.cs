using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Karobar.Desktop.Core.Navigation;
using Karobar.Desktop.ViewModels;

namespace Karobar.Desktop.Views;

public partial class CategoriesView : UserControl, IPageKeyboardHandler
{
    public CategoriesView()
    {
        InitializeComponent();
    }

    public bool HandleNewShortcut()
    {
        if (DataContext is not CategoriesViewModel vm || !vm.CanCreate)
            return false;

        vm.ResetFormCommand.Execute(null);
        Dispatcher.BeginInvoke(() =>
        {
            CategoryNameTextBox?.Focus();
            if (CategoryNameTextBox != null)
            {
                Keyboard.Focus(CategoryNameTextBox);
                CategoryNameTextBox.SelectAll();
            }
        }, DispatcherPriority.Input);
        return true;
    }

    public bool HandleSaveShortcut()
    {
        if (DataContext is not CategoriesViewModel vm)
            return false;

        if (vm.SaveCategoryCommand.CanExecute(null))
        {
            vm.SaveCategoryCommand.Execute(null);
            return true;
        }

        return false;
    }

    public bool HandleSearchShortcut()
    {
        if (DataContext is not CategoriesViewModel)
            return false;

        Dispatcher.BeginInvoke(() =>
        {
            SearchCategoriesBox?.Focus();
            if (SearchCategoriesBox != null)
            {
                Keyboard.Focus(SearchCategoriesBox);
                SearchCategoriesBox.SelectAll();
            }
        }, DispatcherPriority.Input);
        return true;
    }

    public bool HandleEscapeShortcut()
    {
        if (DataContext is not CategoriesViewModel vm)
            return false;

        if (vm.IsImportModalOpen)
        {
            vm.CloseImportModalCommand.Execute(null);
            return true;
        }

        if (vm.IsEditing)
        {
            vm.ResetFormCommand.Execute(null);
            return true;
        }

        if (!string.IsNullOrEmpty(vm.SearchQuery))
        {
            vm.SearchQuery = string.Empty;
            return true;
        }

        return false;
    }

    public bool HandleRefreshShortcut()
    {
        if (DataContext is not CategoriesViewModel vm)
            return false;

        if (vm.LoadCategoriesCommand.CanExecute(null))
        {
            vm.LoadCategoriesCommand.Execute(null);
            return true;
        }

        return false;
    }

    public bool HandleImportShortcut()
    {
        if (DataContext is not CategoriesViewModel vm || !vm.CanImport)
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
        if (DataContext is not CategoriesViewModel vm || !vm.CanExport)
            return false;

        if (vm.ExportCsvCommand.CanExecute(null))
        {
            vm.ExportCsvCommand.Execute(null);
            return true;
        }

        return false;
    }

    private void CategoryNameTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is CategoriesViewModel vm)
        {
            e.Handled = true;
            if (vm.SaveCategoryCommand.CanExecute(null))
            {
                vm.SaveCategoryCommand.Execute(null);
            }
        }
    }

    private void EditRowButton_OnClick(object sender, RoutedEventArgs e)
    {
        CategoryNameTextBox.Focus();
        CategoryNameTextBox.SelectAll();
    }
}
