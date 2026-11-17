using System.Windows;

namespace DocTwin;

public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        DataContext = new ViewModels.HelpViewModel();
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e) => Themes.ThemeService.Current.ToggleCommand.Execute(null);

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
