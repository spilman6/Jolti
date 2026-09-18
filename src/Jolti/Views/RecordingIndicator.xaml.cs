using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Windows.Threading;
using Jolti.Infrastructure;
using Jolti.ViewModels;
namespace Jolti.Views;
public partial class RecordingIndicator : Window
{
    private readonly MainViewModel _model;
    private readonly Border[] _bars = new Border[9];
    private readonly DispatcherTimer _holdDelay = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private bool _mouseHeld;
    private bool _pillStarted;
    public RecordingIndicator(MainViewModel viewModel)
    {
        InitializeComponent();
        _model = viewModel; DataContext = viewModel;
        for (var i = 0; i < _bars.Length; i++)
        {
            _bars[i] = new Border { Width = 2, Height = 3, CornerRadius = new CornerRadius(1),
                Margin = new Thickness(1, 0, 1, 0), VerticalAlignment = VerticalAlignment.Center };
            WaveBars.Children.Add(_bars[i]);
        }
        Position();
        UpdateAnimation();
        _holdDelay.Tick += OnHoldDelay;
        _model.PropertyChanged += OnStatusChanged;
        SystemParameters.StaticPropertyChanged += OnDisplayChanged;
        Closed += (_, _) =>
        {
            EndPillHold();
            _holdDelay.Tick -= OnHoldDelay;
            _model.PropertyChanged -= OnStatusChanged;
            SystemParameters.StaticPropertyChanged -= OnDisplayChanged;
            Pill.BeginAnimation(HeightProperty, null);
            Pill.BeginAnimation(WidthProperty, null);
            foreach (var bar in _bars) bar.BeginAnimation(HeightProperty, null);
        };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            // Keep the destination focused while the pill receives mouse input.
            NativeMethods.SetWindowLong(handle, -20, (NativeMethods.GetWindowLong(handle, -20) | 0x08000000 | 0x80) & ~0x20);
        };
    }
    private void OnPillDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount >= 2)
        {
            EndPillHold();
            if (Application.Current.MainWindow is MainWindow window) window.ShowSettings();
            e.Handled = true;
            return;
        }
        if (_mouseHeld || !_model.CanEdit) return;
        _mouseHeld = true;
        if (!Pill.CaptureMouse()) EndPillHold();
        else _holdDelay.Start();
        e.Handled = true;
    }
    private void OnHoldDelay(object? sender, EventArgs e)
    {
        _holdDelay.Stop();
        if (_mouseHeld && Mouse.LeftButton == MouseButtonState.Pressed)
        {
            _pillStarted = _model.BeginPillHold();
            if (!_pillStarted) EndPillHold();
        }
    }
    private void OnPillUp(object sender, MouseButtonEventArgs e)
    {
        if (!_mouseHeld || e.ChangedButton != MouseButton.Left) return;
        EndPillHold();
        e.Handled = true;
    }
    private void OnPillCaptureLost(object sender, MouseEventArgs e) => EndPillHold();
    private void EndPillHold()
    {
        if (!_mouseHeld) return;
        _mouseHeld = false;
        _holdDelay.Stop();
        if (_pillStarted) _model.EndPillHold();
        _pillStarted = false;
        if (Pill.IsMouseCaptured) Pill.ReleaseMouseCapture();
    }
    private void Position()
    {
        // WorkArea excludes the taskbar and uses WPF's device-independent units.
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Bottom - Height - 14;
    }
    private void OnDisplayChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.InvokeAsync(Position);
    private void OnStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Status)) UpdateAnimation();
    }
    private void UpdateAnimation()
    {
        var recording = _model.Status == "Recording";
        var processing = _model.Status == "Transcribing";
        // Reserve transparent space so expansion keeps the center and bottom edge anchored.
        // Must fit the largest pill size (44x20) plus the 6px margin on every side.
        Width = 56;
        Height = 32;
        Pill.BeginAnimation(WidthProperty, null);
        Pill.Width = recording ? 44 : 40;
        var targetHeight = recording ? 20d : 10d;
        var currentHeight = Pill.Height;
        Pill.BeginAnimation(HeightProperty, null);
        Pill.Height = targetHeight;
        if (SystemParameters.ClientAreaAnimation && currentHeight != targetHeight)
        {
            Pill.BeginAnimation(HeightProperty, new DoubleAnimation(currentHeight, targetHeight,
                TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.Stop
            });
        }
        Pill.CornerRadius = new CornerRadius(recording ? 6 : 3);
        Pill.Padding = recording ? new Thickness(5, 2, 5, 2) : new Thickness(4, 1, 4, 1);
        DotHalo.Width = DotHalo.Height = 4;
        DotHalo.Margin = new Thickness(0, 0, 3, 0);
        StatusDot.Width = StatusDot.Height = 3;
        WaveBars.Height = recording ? 12 : 4;
        StatusLabel.Visibility = Visibility.Collapsed;
        WaveBars.Margin = new Thickness(0);
        Position();
        var color = recording ? "#EF5366" : processing ? "#F0C44C" : _model.Status == "Text inserted" ? "#36C99A" : _model.Status == "Error" ? "#E87987" : "#468C91";
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        StatusDot.Fill = brush;
        for (var i = 0; i < _bars.Length; i++)
        {
            var bar = _bars[i];
            bar.BeginAnimation(HeightProperty, null);
            bar.Background = brush;
            bar.Width = recording ? 2 : 1;
            bar.Margin = new Thickness(recording ? .25 : .75, 0, recording ? .25 : .75, 0);
            bar.Height = 1;
            bar.Opacity = recording || processing ? 1 : .55;
            // Decorative activity wave, not an audio-level meter. No extra microphone
            // reads or retained audio are needed. Stop all animation outside active states.
            if ((recording || processing) && SystemParameters.ClientAreaAnimation)
            {
                var envelope = 1 - Math.Abs(i - 4) / 5.0;
                var peak = recording ? 3 + 9 * envelope : 1 + 2 * envelope;
                var animation = new DoubleAnimation(recording ? 2 : 1, peak, TimeSpan.FromMilliseconds(recording ? 290 + (i % 4) * 65 : 650))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds(i * 55),
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                bar.BeginAnimation(HeightProperty, animation);
            }
            else if (recording || processing) bar.Height = recording ? 3 + (4 - Math.Abs(i - 4)) * 2.25 : 1 + (4 - Math.Abs(i - 4)) * .75;
        }
    }
}
