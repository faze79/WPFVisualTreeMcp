using System.Windows;

namespace SampleWpfApp;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OpenDragDemo(object sender, RoutedEventArgs e) => new DragDemoWindow { Owner = this }.Show();
    private void OpenTouchDemo(object sender, RoutedEventArgs e) => new TouchDemoWindow { Owner = this }.Show();

    private async void LoadTestStatus(object sender, RoutedEventArgs e)
    {
        TestStatusTextBox.Text = "not loaded";
        await System.Threading.Tasks.Task.Delay(600);
        TestStatusTextBox.Text = "loaded";
    }
}
