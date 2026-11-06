using System.Windows;

namespace HourChime.Views;

/// <summary>与深色主题一致的确认对话框（替代系统 MessageBox）。</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        SourceInitialized += (_, _) => Platform.EnableRoundedCorners(this);
    }

    public static bool Ask(Window? owner, string title, string message)
    {
        var dlg = new ConfirmDialog(title, message);
        if (owner != null && owner.IsVisible) dlg.Owner = owner;
        return dlg.ShowDialog() == true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
