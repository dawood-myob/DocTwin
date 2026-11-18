using System.Windows;
using System.Windows.Controls;

namespace DocTwin;

public partial class BreakthroughBackdrop : UserControl
{
    public BreakthroughBackdrop()
    {
        InitializeComponent();
        // Honour the Windows "Show animations" setting: the backdrop then stays still.
        if (!SystemParameters.ClientAreaAnimation) Triggers.Clear();
    }
}
