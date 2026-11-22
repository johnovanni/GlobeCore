using System.Windows.Controls;

namespace GlobeCore.Wpf.Status;

/// <summary>Status dots for a row of data sources, such as in a time bar. DataContext: <see cref="StatusDotsViewModel"/>.</summary>
public partial class StatusDotsView : UserControl
{
    public StatusDotsView() => InitializeComponent();
}
