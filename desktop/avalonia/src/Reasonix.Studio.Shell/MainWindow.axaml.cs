using Avalonia.Controls;

namespace Reasonix.Studio.Shell;

/// <summary>
/// The top-level window Reasonix Studio opens on launch.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class and loads its XAML.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
    }
}
