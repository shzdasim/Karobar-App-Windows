using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Karobar.Desktop.ViewModels;

namespace Karobar.Desktop.Views;

public partial class CategoriesView : UserControl
{
    public CategoriesView()
    {
        InitializeComponent();
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
