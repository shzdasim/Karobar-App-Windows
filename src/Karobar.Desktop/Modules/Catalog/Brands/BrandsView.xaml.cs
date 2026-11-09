using System.Windows.Controls;
using System.Windows.Input;
using Karobar.Desktop.ViewModels;

namespace Karobar.Desktop.Views;

public partial class BrandsView : UserControl
{
    public BrandsView()
    {
        InitializeComponent();
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
