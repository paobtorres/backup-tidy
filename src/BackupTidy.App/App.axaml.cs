using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BackupTidy.App.Servicios;
using BackupTidy.App.ViewModels;
using BackupTidy.App.Views;

namespace BackupTidy.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var ventana = new MainWindow();
            ventana.DataContext = new MainViewModel(new DialogosAvalonia(ventana));
            desktop.MainWindow = ventana;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
