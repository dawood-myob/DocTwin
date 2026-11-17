using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace DocTwin;

public partial class DiscoveryView : UserControl
{
    public DiscoveryView() => InitializeComponent();

    private void SourceTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ViewModels.ConfluenceViewModel viewModel && e.NewValue is ViewModels.SourceTreeItem node)
            viewModel.SourceTreeDetails = node.Details;
    }

    private void PageLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        if (e.Uri.Scheme != Uri.UriSchemeHttps) return;
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Win32Exception)
        {
            MessageBox.Show("Windows could not open the browser. Use the original page URL in the exported report.", "Browser unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
