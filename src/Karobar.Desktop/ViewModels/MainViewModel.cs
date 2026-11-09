using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Karobar.Application.Interfaces;

namespace Karobar.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IDatabaseInitializerService _dbInitializer;
    private bool _isDbInitialized;

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

    public MainViewModel(IAuthService authService, IDatabaseInitializerService dbInitializer)
    {
        _authService = authService;
        _dbInitializer = dbInitializer;
    }

    public async Task EnsureDatabaseInitializedAsync()
    {
        if (_isDbInitialized)
            return;

        await _dbInitializer.InitializeAndSeedAsync();
        _isDbInitialized = true;
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
            await EnsureDatabaseInitializedAsync();
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
