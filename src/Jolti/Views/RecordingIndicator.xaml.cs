using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Jolti.Infrastructure;
using Jolti.ViewModels;
namespace Jolti.Views;
public partial class RecordingIndicator : Window
{
    private readonly MainViewModel _model;
    private readonly Border[] _bars = new Border[13];
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
        _model.PropertyChanged += OnStatusChanged;
        SystemParameters.StaticPropertyChanged += OnDisplayChanged;
        Closed += (_, _) =>
        {
            _model.PropertyChanged -= OnStatusChanged;
            SystemParameters.StaticPropertyChanged -= OnDisplayChanged;
            foreach (var bar in _bars) bar.BeginAnimation(HeightProperty, null);
        };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            // No-activate, tool-window and transparent styles prevent focus theft and let clicks pass through.
            NativeMethods.SetWindowLong(handle, -20, NativeMethods.GetWindowLong(handle, -20) | 0x08000000 | 0x80 | 0x20);
        };
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
        // Keep the same discreet footprint in every state; details remain in the main window.
        Width = 82;
        Height = 38;
        Pill.Padding = new Thickness(8, 3, 8, 3);
        DotHalo.Width = DotHalo.Height = 10;
        DotHalo.Margin = new Thickness(0, 0, 6, 0);
        StatusDot.Width = StatusDot.Height = 6;
        WaveBars.Height = 12;
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
            bar.Width = 1;
            bar.Margin = new Thickness(.75, 0, .75, 0);
            bar.Height = 2;
            bar.Opacity = recording || processing ? 1 : .55;
            // Decorative activity wave, not an audio-level meter. No extra microphone
            // reads or retained audio are needed. Stop all animation outside active states.
            if ((recording || processing) && SystemParameters.ClientAreaAnimation)
            {
                var envelope = 1 - Math.Abs(i - 6) / 8.0;
                var peak = recording ? 3 + 9 * envelope : 3 + 6 * envelope;
                var animation = new DoubleAnimation(2, peak, TimeSpan.FromMilliseconds(recording ? 290 + (i % 4) * 65 : 650))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds(i * 55),
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                bar.BeginAnimation(HeightProperty, animation);
            }
            else if (recording || processing) bar.Height = 3 + (6 - Math.Abs(i - 6)) * 1.5;
        }
    }
}
