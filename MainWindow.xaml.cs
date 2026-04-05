using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.Wave;
using NeuroPlayer_HighRes_V2.Core.Services;
using NeuroPlayer_HighRes_V2.Core.Streams;

namespace NeuroPlayer_HighRes_V2;

public partial class MainWindow : Window
{
    // Frozen brush – allocated once, immutable, optimal for WPF rendering
    private static readonly Brush TealBrush = CreateFrozenBrush(0x00, 0xE6, 0xB8);

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private readonly HighResAudioService _audioService = new();
    private AsyncGaplessBufferStream _currentStream;
    private string _selectedFilePath;

    private readonly DispatcherTimer _updateTimer;
    private bool _isDragging = false;

    public MainWindow()
    {
        InitializeComponent();
        LoadAsioDevices();

        // Timer aktualisiert UI alle 50 ms; Background-Priorität entlastet den Main-Thread
        _updateTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _updateTimer.Tick += UpdateTimer_Tick;
    }

    private void LoadAsioDevices()
    {
        try
        {
            foreach (string driver in AsioOut.GetDriverNames())
                DeviceComboBox.Items.Add($"[ASIO] {driver}");

            if (DeviceComboBox.Items.Count > 0)
                DeviceComboBox.SelectedIndex = 0;
            else
            {
                StatusSubLabel.Text = "Kein ASIO Treiber gefunden!";
                StatusSubLabel.Foreground = Brushes.Red;
            }
        }
        catch (Exception)
        {
            StatusSubLabel.Text = "Fehler beim Laden der Audiogeräte.";
            StatusSubLabel.Foreground = Brushes.Red;
        }
    }

    private void Laden_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog openFileDialog = new()
        {
            Filter = "WAV Audio (*.wav)|*.wav",
            Title = "Wähle eine High-Res WAV Datei"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            _selectedFilePath = openFileDialog.FileName;
            FileLabel.Text = Path.GetFileName(_selectedFilePath);
            StatusSubLabel.Text = "Datei in Warteschlange. Bereit zum Abspielen.";
            StatusSubLabel.Foreground = Brushes.White;
        }
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedFilePath))
        {
            MessageBox.Show("Bitte zuerst über LADEN eine Datei auswählen.", "Hinweis");
            return;
        }

        try
        {
            StopAudio();

            string selectedDriver = DeviceComboBox.SelectedItem?.ToString()?.Replace("[ASIO] ", "");

            _audioService.InitializeAsio(selectedDriver);

            _currentStream = new AsyncGaplessBufferStream(_selectedFilePath, 4)
            {
                IsLooping = LoopCheckBox.IsChecked ?? false
            };

            ProgressSlider.Maximum = _currentStream.TotalTime.TotalSeconds;

            _audioService.PlayStream(_currentStream);

            _updateTimer.Start();

            StatusMainLabel.Text = "PLAYING";
            StatusSubLabel.Text = "ASIO Engine aktiv (Bit-Perfect Flow)";
            StatusSubLabel.Foreground = TealBrush;
        }
        catch (Exception ex)
        {
            StatusMainLabel.Text = "FEHLER";
            StatusSubLabel.Text = ex.Message;
            StatusSubLabel.Foreground = Brushes.Red;
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        StopAudio();
        StatusMainLabel.Text = "GESTOPPT";
        StatusSubLabel.Text = "Bereit.";
        StatusSubLabel.Foreground = Brushes.White;
    }

    private void StopAudio()
    {
        _updateTimer.Stop();
        _audioService?.CleanUp();

        if (_currentStream is not null)
        {
            _currentStream.Dispose();
            _currentStream = null;
        }

        ProgressSlider.Value = 0;
        TimeLabel.Text = "00:00 / 00:00";
    }

    private void UpdateTimer_Tick(object sender, EventArgs e)
    {
        if (_currentStream is not null && !_isDragging)
        {
            ProgressSlider.Value = _currentStream.CurrentTime.TotalSeconds;
            TimeLabel.Text = $"{_currentStream.CurrentTime:mm\\:ss} / {_currentStream.TotalTime:mm\\:ss}";
        }
    }

    private void ProgressSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        _isDragging = true;
    }

    private void ProgressSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _isDragging = false;
        if (_currentStream is not null)
            _currentStream.CurrentTime = TimeSpan.FromSeconds(ProgressSlider.Value);
    }

    protected override void OnClosed(EventArgs e)
    {
        StopAudio();
        _audioService?.Dispose();
        base.OnClosed(e);
    }
}