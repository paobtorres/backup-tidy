using CommunityToolkit.Mvvm.ComponentModel;

namespace BackupTidy.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Greeting { get; set; } = "BackupTidy: organizador de backups de celulares";
}
