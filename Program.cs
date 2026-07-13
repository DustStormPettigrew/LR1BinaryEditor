using Avalonia;
using System;

namespace LR1BinaryEditor
{
    static class Program
    {
        internal static SingleInstanceManager SingleInstance { get; private set; }

        [STAThread]
        static void Main(string[] args)
        {
            using (SingleInstanceManager singleInstance = SingleInstanceManager.Create(args))
            {
                if (!singleInstance.IsPrimaryInstance)
                    return;

                SingleInstance = singleInstance;
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
        }

        static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
