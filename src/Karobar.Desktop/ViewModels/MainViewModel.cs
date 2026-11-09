using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Karobar.Application.Interfaces;

namespace Karobar.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IAuthService _authService;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private bool _isLoginVisible = true;

    [ObservableProperty]
    private bool _isBusy;

    public MainViewModel(IAuthService authService)
    {
        _authService = authService;
    }

    public Task InitializeAsync()
    {
        IsAuthenticated = false;
        IsLoginVisible = true;
        HasError = false;
        ErrorMessage = string.Empty;
        return Task.CompletedTask;
    }

    [RelayCommand]
    public async Task LoginAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            await _authService.LoginAsync(Username, Password);
            IsAuthenticated = true;
            IsLoginVisible = false;
        }
        catch (Exception ex)
        {
            IsAuthenticated = false;
            IsLoginVisible = true;
            ErrorMessage = ex.Message;
            HasError = true;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
