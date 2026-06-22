using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LR1BinaryEditor
{
    public sealed class SyntaxHighlightingColorsWindow : Window
    {
        private readonly Dictionary<string, string> m_colors;
        private readonly Dictionary<string, string> m_defaults;
        private readonly ComboBox m_colorSelector;
        private readonly TextBox m_hexBox;
        private readonly Border m_swatch;
        private readonly TextBlock m_errorText;

        public SyntaxHighlightingColorsWindow(
            Dictionary<string, string> currentColors,
            Dictionary<string, string> defaultColors)
        {
            m_colors = new Dictionary<string, string>(currentColors, StringComparer.OrdinalIgnoreCase);
            m_defaults = new Dictionary<string, string>(defaultColors, StringComparer.OrdinalIgnoreCase);

            Title = "Syntax Highlighting Colors";
            Width = 420;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            CanResize = false;
            FontSize = 12;

            m_colorSelector = new ComboBox
            {
                ItemsSource = m_colors.Keys.OrderBy(x => x).ToList(),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = 24,
                Padding = new Thickness(4, 1)
            };
            m_colorSelector.SelectionChanged += ColorSelector_SelectionChanged;

            m_hexBox = new TextBox
            {
                Watermark = "#RRGGBB",
                Width = 120,
                MinHeight = 24,
                Padding = new Thickness(4, 1)
            };
            m_hexBox.TextChanged += HexBox_TextChanged;

            m_swatch = new Border
            {
                Width = 34,
                Height = 22,
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(6, 0, 0, 0)
            };

            m_errorText = new TextBlock
            {
                Foreground = Brushes.Firebrick,
                FontSize = 11,
                MinHeight = 18
            };

            Button resetSelectedButton = CreateDialogButton("Reset Selected", 105, new Thickness(0, 6, 4, 0));
            resetSelectedButton.Click += ResetSelected_Click;

            Button resetAllButton = CreateDialogButton("Reset All", 85, new Thickness(0, 6, 0, 0));
            resetAllButton.Click += ResetAll_Click;

            Button okButton = CreateDialogButton("OK", 75, new Thickness(3));
            okButton.Click += Ok_Click;

            Button cancelButton = CreateDialogButton("Cancel", 75, new Thickness(3));
            cancelButton.Click += (_, _) => Close(null);

            var colorRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 4
            };
            colorRow.Children.Add(new TextBlock { Text = "Color:", VerticalAlignment = VerticalAlignment.Center });
            colorRow.Children.Add(m_hexBox);
            colorRow.Children.Add(m_swatch);

            var resetRow = new StackPanel { Orientation = Orientation.Horizontal };
            resetRow.Children.Add(resetSelectedButton);
            resetRow.Children.Add(resetAllButton);

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            buttonRow.Children.Add(okButton);
            buttonRow.Children.Add(cancelButton);

            var panel = new StackPanel
            {
                Margin = new Thickness(10),
                Spacing = 5
            };
            panel.Children.Add(new TextBlock { Text = "Syntax element" });
            panel.Children.Add(m_colorSelector);
            panel.Children.Add(colorRow);
            panel.Children.Add(m_errorText);
            panel.Children.Add(resetRow);
            panel.Children.Add(buttonRow);

            Content = panel;

            if (m_colors.Count > 0)
                m_colorSelector.SelectedIndex = 0;
        }

        private static Button CreateDialogButton(string content, double minWidth, Thickness margin)
        {
            return new Button
            {
                Content = content,
                MinWidth = minWidth,
                MinHeight = 24,
                Padding = new Thickness(6, 2),
                Margin = margin,
                FontSize = 12
            };
        }

        private void ColorSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string key = SelectedKey;
            if (key == null)
                return;

            m_hexBox.Text = m_colors[key];
            UpdateSwatch();
        }

        private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string key = SelectedKey;
            if (key == null)
                return;

            m_colors[key] = m_hexBox.Text?.Trim() ?? "";
            UpdateSwatch();
        }

        private void ResetSelected_Click(object sender, RoutedEventArgs e)
        {
            string key = SelectedKey;
            if (key == null || !m_defaults.ContainsKey(key))
                return;

            m_colors[key] = m_defaults[key];
            m_hexBox.Text = m_colors[key];
            UpdateSwatch();
        }

        private void ResetAll_Click(object sender, RoutedEventArgs e)
        {
            m_colors.Clear();
            foreach (KeyValuePair<string, string> kvp in m_defaults)
                m_colors[kvp.Key] = kvp.Value;

            string key = SelectedKey;
            if (key != null && m_colors.ContainsKey(key))
                m_hexBox.Text = m_colors[key];
            UpdateSwatch();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            foreach (KeyValuePair<string, string> kvp in m_colors)
            {
                if (!MainWindow.TryParseColor(kvp.Value, out _))
                {
                    m_errorText.Text = kvp.Key + " has an invalid color. Use #RRGGBB.";
                    return;
                }
            }

            Close(new Dictionary<string, string>(m_colors, StringComparer.OrdinalIgnoreCase));
        }

        private string SelectedKey => m_colorSelector.SelectedItem as string;

        private void UpdateSwatch()
        {
            if (MainWindow.TryParseColor(m_hexBox.Text, out Avalonia.Media.Color color))
            {
                m_swatch.Background = new SolidColorBrush(color);
                m_errorText.Text = "";
            }
            else
            {
                m_swatch.Background = Brushes.Transparent;
                m_errorText.Text = "Use a hex color like #0066CC.";
            }
        }
    }
}
