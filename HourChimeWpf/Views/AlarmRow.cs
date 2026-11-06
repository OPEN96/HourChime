using System.ComponentModel;

namespace HourChime.Views;

/// <summary>闹钟列表的一行（供 WPF 绑定；启用状态双向绑定到配置模型）。</summary>
public sealed class AlarmRow : INotifyPropertyChanged
{
    private readonly Action _onChanged;

    public AlarmRow(AlarmItem model, Action onChanged)
    {
        Model = model;
        _onChanged = onChanged;
    }

    public AlarmItem Model { get; }

    public string TimeText => $"{Model.Hour:00}:{Model.Minute:00}";

    public string LabelText => string.IsNullOrWhiteSpace(Model.Label) ? "未命名闹钟" : Model.Label.Trim();

    public string MetaText
    {
        get
        {
            var parts = new List<string> { Texts.RepeatText(Model), Texts.RingModeText(Model.RingMode) };
            if (Model.SnoozeMinutes > 0) parts.Add($"贪睡 {Model.SnoozeMinutes} 分");
            return string.Join("  ·  ", parts);
        }
    }

    public bool Enabled
    {
        get => Model.Enabled;
        set
        {
            if (Model.Enabled == value) return;
            Model.Enabled = value;
            Raise(nameof(Enabled));
            _onChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
