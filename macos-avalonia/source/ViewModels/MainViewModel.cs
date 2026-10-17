using CommunityToolkit.Mvvm.ComponentModel;

namespace MandelbrotViewer.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string greeting = "Welcome to Avalonia!";
}
