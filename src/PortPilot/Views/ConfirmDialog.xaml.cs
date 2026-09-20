using System.Windows;
namespace PortPilot.Views;
public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message) { InitializeComponent(); Title = title; Heading.Text = title; Message.Text = message; }
    private void ConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
