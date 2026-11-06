using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace HourChime.Views;

/// <summary>屏幕右下角的提醒卡片，90 秒后自动消失；用户点击按钮则不再重复提醒。</summary>
public partial class ToastWindow : Window
{
    private readonly ToastRequest _request;
    private readonly DispatcherTimer _autoClose = new() { Interval = TimeSpan.FromSeconds(90) };
    private bool _closing;

    /// <summary>true 表示用户点击了按钮，或被新提醒顶替。</summary>
    public bool DismissedByUser { get; private set; }

    public ToastWindow(ToastRequest request)
    {
        InitializeComponent();
        _request = request;

        TitleText.Text = request.Title;
        TimeText.Text = request.TimeText;
        MessageText.Text = request.Message;

        if (string.IsNullOrEmpty(request.SnoozeLabel))
        {
            SnoozeButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            SnoozeButton.Content = request.SnoozeLabel;
        }

        Loaded += OnLoaded;
        _autoClose.Tick += (_, _) => CloseQuietly();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 定位到工作区右下角
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - 22;
        Top = wa.Bottom - ActualHeight - 22;

        // 滑入 + 淡入
        Card.Opacity = 0;
        var shift = new TranslateTransform(0, 16);
        Card.RenderTransform = shift;

        Card.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        shift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

        _autoClose.Start();
    }

    private void Snooze_Click(object sender, RoutedEventArgs e)
    {
        DismissedByUser = true;
        _request.OnSnooze?.Invoke();
        CloseQuietly();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DismissedByUser = true;
        CloseQuietly();
    }

    /// <summary>关闭弹窗（自动关闭时 DismissedByUser 保持 false，用于触发重复提醒）。</summary>
    public void CloseQuietly()
    {
        if (_closing) return;
        _closing = true;
        _autoClose.Stop();
        try { Close(); } catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoClose.Stop();
        base.OnClosed(e);
    }
}
