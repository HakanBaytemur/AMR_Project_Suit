namespace DwgTrueView.App;

/// <summary>
/// Short opacity fade for the shared command-tip window.
/// Cancelling mid-fade (hover the next command) keeps the motion snappy.
/// </summary>
internal sealed class CadToolTipFade : IDisposable
{
    private const int FadeInMs = 340;
    private const int FadeOutMs = 280;
    private const int FrameMs = 12;

    private readonly Form _form;
    private readonly System.Windows.Forms.Timer _timer;
    private double _from;
    private double _to;
    private long _startedMs;
    private int _durationMs = FadeInMs;
    private Action? _completed;
    private bool _disposed;

    public CadToolTipFade(Form form)
    {
        _form = form;
        _timer = new System.Windows.Forms.Timer { Interval = FrameMs };
        _timer.Tick += OnTick;
    }

    public bool IsRunning => _timer.Enabled;

    public void Play(double target, Action? completed = null)
    {
        if (_disposed)
        {
            return;
        }

        _completed = completed;
        double current = Math.Clamp(_form.Opacity, 0, 1);
        if (Math.Abs(current - target) < 0.015 && !IsRunning)
        {
            _form.Opacity = target;
            completed?.Invoke();
            return;
        }

        _from = current;
        _to = target;
        _durationMs = target >= current ? FadeInMs : FadeOutMs;
        _startedMs = Environment.TickCount64;
        _timer.Start();
    }

    public void Snap(double opacity)
    {
        if (_disposed)
        {
            return;
        }

        _timer.Stop();
        _completed = null;
        _form.Opacity = Math.Clamp(opacity, 0, 1);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        double t = Math.Clamp((Environment.TickCount64 - _startedMs) / (double)_durationMs, 0, 1);
        double eased = t < 0.5
            ? 4 * t * t * t
            : 1 - (Math.Pow((-2 * t) + 2, 3) / 2);
        _form.Opacity = _from + ((_to - _from) * eased);
        if (t < 1)
        {
            return;
        }

        _timer.Stop();
        _form.Opacity = _to;
        Action? done = _completed;
        _completed = null;
        done?.Invoke();
    }
}
