using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using System;
using System.Reflection;

namespace LR1BinaryEditor
{
    public class AboutWindow : Window
    {
        public AboutWindow()
        {
            Title = "About";
            Width = 380;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            string version = FormatVersion(Assembly.GetExecutingAssembly().GetName().Version);

            var text = new TextBlock
            {
                Text = "LR1 Binary Editor\n\n" +
                       "Originally made and previously maintained by Will Kirkby\n" +
                       "Maintained and updated by Dust Storm\n\n" +
                       "Uses LibLR1 by Will Kirkby\n\n" +
                       "Thanks to:\n" +
                       "Will Kirkby - for LibLR1 and the original LR1 Binary Editor\n\n" +
                       version,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };

            var ok = new Button { Content = "OK", MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Center };
            ok.Click += (_, _) => Close();

            var panel = new StackPanel { Margin = new Thickness(20), Spacing = 8 };
            panel.Children.Add(text);
            panel.Children.Add(ok);
            Content = panel;
        }

        private static string FormatVersion(Version version)
        {
            if (version == null)
                return "vUnknown";

            return string.Format("v{0}.{1}.{2}", version.Major, version.Minor, version.Build);
        }
    }
}
