using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;

namespace LR1BinaryEditor
{
    public class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                MainWindow mainWindow = new MainWindow(desktop.Args ?? Array.Empty<string>());
                desktop.MainWindow = mainWindow;
                Program.SingleInstance?.StartServer(args =>
                    Dispatcher.UIThread.Post(() => mainWindow.OpenFilesFromExternal(args)));
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
