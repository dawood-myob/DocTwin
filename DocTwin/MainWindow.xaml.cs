using System.Windows;

namespace DocTwin;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e) => Themes.ThemeService.Current.ToggleCommand.Execute(null);

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        new HelpWindow { Owner = this }.ShowDialog();
    }

    private void ConnectSampleButton_Click(object sender, RoutedEventArgs e)
    {
        new ConfluenceWindow { Owner = this }.ShowDialog();
    }
}