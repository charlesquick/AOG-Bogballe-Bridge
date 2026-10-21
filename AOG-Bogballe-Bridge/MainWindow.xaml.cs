using System;
using System.Globalization;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AOGBogballeBridge
{
    public partial class MainWindow : Window
    {
        private const string MinimalPositionRegistryPath = @"Software\AOGBogballeBridge";
        private Rect _restoreBounds;
        private WindowState _restoreState;
        private WindowStyle _restoreStyle;
        private ResizeMode _restoreResizeMode;
        private SizeToContent _restoreSizeToContent;
        private double _restoreMinWidth;
        private double _restoreMinHeight;
        private bool _restoreTopmost;
        private bool _isMinimalDisplay;

        public MainWindow()
        {
            InitializeComponent();
            ConfigureMinimalLayout();
            DataContext = new MainViewModel();
        }

        private void ConfigureMinimalLayout()
        {
            foreach (UIElement element in MinimalLayout.Children)
            {
                if (element is StackPanel panel)
                {
                    int column = Grid.GetColumn(panel);
                    panel.Margin = new Thickness(column == 0 ? 4 : 0, 0, 6, 0);

                    if (panel.Children[0] is TextBlock label)
                        label.FontSize = 9;
                    if (panel.Children[1] is TextBlock value)
                    {
                        value.FontSize = 17;
                        value.Margin = new Thickness(4, 0, 0, 0);
                    }
                }
                else if (element is Button button)
                {
                    button.Padding = new Thickness(6, 2, 6, 2);
                    button.Margin = new Thickness(4, 0, 4, 0);
                }
            }
        }

        private void ShowMinimalDisplay(object sender, RoutedEventArgs e)
        {
            if (_isMinimalDisplay)
                return;

            _restoreBounds = RestoreBounds;
            _restoreState = WindowState;
            _restoreStyle = WindowStyle;
            _restoreResizeMode = ResizeMode;
            _restoreSizeToContent = SizeToContent;
            _restoreMinWidth = MinWidth;
            _restoreMinHeight = MinHeight;
            _restoreTopmost = Topmost;

            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            MinWidth = 0;
            MinHeight = 0;
            SizeToContent = SizeToContent.WidthAndHeight;
            FullLayout.Visibility = Visibility.Collapsed;
            MinimalLayout.Visibility = Visibility.Visible;

            if (TryReadMinimalPosition(out double left, out double top))
            {
                Left = left;
                Top = top;
            }
            else
            {
                Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
                Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;
            }

            _isMinimalDisplay = true;
        }

        private void ShowFullDisplay(object sender, RoutedEventArgs e)
        {
            if (!_isMinimalDisplay)
                return;

            WindowState = WindowState.Normal;
            WindowStyle = _restoreStyle;
            ResizeMode = _restoreResizeMode;
            Topmost = _restoreTopmost;
            MinWidth = _restoreMinWidth;
            MinHeight = _restoreMinHeight;
            SizeToContent = _restoreSizeToContent;
            Left = _restoreBounds.Left;
            Top = _restoreBounds.Top;
            Width = _restoreBounds.Width;
            Height = _restoreBounds.Height;
            MinimalLayout.Visibility = Visibility.Collapsed;
            FullLayout.Visibility = Visibility.Visible;
            WindowState = _restoreState;
            _isMinimalDisplay = false;
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_isMinimalDisplay && e.Key == Key.Escape)
            {
                ShowFullDisplay(sender, e);
                e.Handled = true;
            }
        }

        private void MinimalLayout_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
                return;

            double originalLeft = Left;
            double originalTop = Top;
            DragMove();
            if (_isMinimalDisplay && (Left != originalLeft || Top != originalTop))
                SaveMinimalPosition();
        }

        private static bool TryReadMinimalPosition(out double left, out double top)
        {
            left = 0;
            top = 0;
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(MinimalPositionRegistryPath);
            if (key == null)
                return false;

            return TryReadCoordinate(key.GetValue("MinimalLeft"), out left)
                && TryReadCoordinate(key.GetValue("MinimalTop"), out top);
        }

        private static bool TryReadCoordinate(object? value, out double coordinate)
        {
            coordinate = 0;
            return value is string text
                && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out coordinate)
                && double.IsFinite(coordinate);
        }

        private void SaveMinimalPosition()
        {
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(MinimalPositionRegistryPath, writable: true);
            if (key == null)
                throw new InvalidOperationException("Could not open the registry key to save the minimal display position.");

            key.SetValue("MinimalLeft", Left.ToString("R", CultureInfo.InvariantCulture), RegistryValueKind.String);
            key.SetValue("MinimalTop", Top.ToString("R", CultureInfo.InvariantCulture), RegistryValueKind.String);
        }
    }
}
