using System.Collections.Concurrent;

namespace HourChime;

/// <summary>
/// 基于 Windows SAPI（SAPI.SpVoice COM）的语音播报，跑在独立 STA 线程上，不阻塞 UI。
/// 若系统不支持 SAPI，则自动降级为仅提示音。
/// </summary>
public sealed class SpeechService : IDisposable
{
    private sealed record Request(string Text, int Volume, int Rate, string? VoiceName);

    private readonly BlockingCollection<Request> _queue = new(new ConcurrentQueue<Request>());
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly Thread _worker;
    private string[] _voices = Array.Empty<string>();
    private dynamic? _voice;
    private dynamic? _tokens;
    private bool _disposed;

    public SpeechService()
    {
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "HourChime.TTS" };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    /// <summary>可用语音名称列表（阻塞至初始化完成，最多 2 秒）。</summary>
    public IReadOnlyList<string> Voices
    {
        get
        {
            _ready.Wait(TimeSpan.FromSeconds(5));
            return _voices;
        }
    }

    public bool Available
    {
        get
        {
            _ready.Wait(TimeSpan.FromSeconds(2));
            return _voice != null;
        }
    }

    private void WorkerLoop()
    {
        try
        {
            var type = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (type != null)
            {
                _voice = Activator.CreateInstance(type);
                _tokens = _voice!.GetVoices();
                int count = (int)_tokens.Count;
                var names = new List<string>(count);
                for (int i = 0; i < count; i++)
                    names.Add((string)_tokens.Item(i).GetDescription());
                _voices = names.ToArray();
            }
        }
        catch
        {
            _voice = null;
            _tokens = null;
        }
        finally
        {
            _ready.Set();
        }

        if (_voice == null) return;

        try
        {
            foreach (var req in _queue.GetConsumingEnumerable())
            {
                try
                {
                    _voice.Volume = Math.Clamp(req.Volume, 0, 100);
                    _voice.Rate = Math.Clamp(req.Rate, -10, 10);
                    if (!string.IsNullOrEmpty(req.VoiceName) && _tokens != null)
                    {
                        int count = (int)_tokens.Count;
                        for (int i = 0; i < count; i++)
                        {
                            if ((string)_tokens.Item(i).GetDescription() == req.VoiceName)
                            {
                                _voice.Voice = _tokens.Item(i);
                                break;
                            }
                        }
                    }
                    _voice.Speak(req.Text, 0);   // 同步播报
                }
                catch
                {
                    // 单次播报失败不影响后续
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // 退出时正常
        }
    }

    public void Speak(string text, AppSettings settings)
    {
        if (_disposed || !Available) return;
        Drain();
        try { _queue.Add(new Request(text, settings.Volume, settings.Rate, settings.VoiceName)); }
        catch (InvalidOperationException) { /* 已关闭 */ }
    }

    public void Stop()
    {
        Drain();
        try { _voice?.Speak("", 2); }   // 2 = PurgeBeforeSpeak
        catch { /* 忽略 */ }
    }

    private void Drain()
    {
        while (_queue.TryTake(out _)) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _queue.CompleteAdding(); } catch { }
        _ready.Dispose();
    }
}

/// <summary>程序内置提示音（正弦双音/三音阶），无需音频文件。</summary>
public static class Tones
{
    /// <summary>按设置播放提示音（优先自定义 WAV，失败或未设置时用内置三音阶）。</summary>
    public static void PlayChime(AppSettings settings)
    {
        var path = settings.ChimePath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                using var custom = new System.Media.SoundPlayer(path);
                custom.Load();
                custom.Play();
                return;
            }
            catch
            {
                // 自定义音频损坏时回退内置提示音
            }
        }
        PlayChime(settings.Volume);
    }

    public static void PlayChime(int volumePercent)
    {
        try
        {
            var wav = BuildWav(new[]
            {
                (Hz: 880.0, Seconds: 0.16),
                (Hz: 1174.7, Seconds: 0.16),
                (Hz: 1568.0, Seconds: 0.34)
            }, volumePercent);
            using var ms = new MemoryStream(wav);
            using var player = new System.Media.SoundPlayer(ms);
            player.Load();   // 先载入内存，之后可立即释放流
            player.Play();
        }
        catch
        {
            System.Media.SystemSounds.Beep.Play();
        }
    }

    private static byte[] BuildWav((double Hz, double Seconds)[] notes, int volumePercent)
    {
        const int rate = 44100;
        double vol = Math.Clamp(volumePercent, 0, 100) / 100.0 * 0.55;
        int total = notes.Sum(n => (int)(n.Seconds * rate));
        var data = new byte[total * 2];
        int idx = 0;
        double phase = 0;
        foreach (var (hz, seconds) in notes)
        {
            int len = (int)(seconds * rate);
            for (int i = 0; i < len; i++)
            {
                double t = (double)i / len;
                double env = Math.Min(1.0, i / (rate * 0.012)) * Math.Min(1.0, (len - i) / (rate * 0.05));
                double s = Math.Sin(phase) * 0.72 + Math.Sin(phase * 2) * 0.22;
                short v = (short)(Math.Clamp(s * env * vol, -1, 1) * short.MaxValue);
                data[idx++] = (byte)(v & 0xFF);
                data[idx++] = (byte)((v >> 8) & 0xFF);
                phase += 2 * Math.PI * hz / rate;
            }
        }

        var ms = new MemoryStream(44 + data.Length);
        var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(36 + data.Length);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);          // PCM
        w.Write((short)1);          // mono
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(data.Length);
        w.Write(data);
        w.Flush();
        return ms.ToArray();
    }
}
