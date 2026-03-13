using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.Wave;
using NeuroPlayer_HighRes_V2.Core.Services;
using NeuroPlayer_HighRes_V2.Core.Streams;

namespace NeuroPlayer_HighRes_V2
{
    public partial class MainWindow : Window
    {
        private HighResAudioService _audioService;
        private AsyncGaplessBufferStream _currentStream;
        private string _selectedFilePath;

        // NEU: Timer & UI Logik
        private DispatcherTimer _updateTimer;
        private bool _isDragging = false;

        public MainWindow()
        {
            InitializeComponent();
            _audioService = new HighResAudioService();
            LoadAsioDevices();

            // NEU: Timer einrichten (aktualisiert UI alle 50ms für flüssige Optik)
            _updateTimer = new DispatcherTimer();
            _updateTimer.Interval = TimeSpan.FromMilliseconds(50);
            _updateTimer.Tick += UpdateTimer_Tick;
        }

        private void LoadAsioDevices()
        {
            try
            {
                var asioDrivers = AsioOut.GetDriverNames();
                foreach (var driver in asioDrivers)
                {
                    DeviceComboBox.Items.Add($"[ASIO] {driver}");
                }

                if (DeviceComboBox.Items.Count > 0)
                {
                    DeviceComboBox.SelectedIndex = 0;
                }
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
            OpenFileDialog openFileDialog = new OpenFileDialog
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

                string selectedDriver = null;
                if (DeviceComboBox.SelectedItem != null)
                {
                    selectedDriver = DeviceComboBox.SelectedItem.ToString().Replace("[ASIO] ", "");
                }

                _audioService.InitializeAsio(selectedDriver);

                _currentStream = new AsyncGaplessBufferStream(_selectedFilePath, 4)
                {
                    IsLooping = LoopCheckBox.IsChecked ?? false
                };

                // NEU: Slider Maximum auf die Gesamtlänge des Audios setzen
                ProgressSlider.Maximum = _currentStream.TotalTime.TotalSeconds;

                _audioService.PlayStream(_currentStream);

                // NEU: UI-Update starten
                _updateTimer.Start();

                StatusMainLabel.Text = "PLAYING";
                StatusSubLabel.Text = "ASIO Engine aktiv (Bit-Perfect Flow)";
                StatusSubLabel.Foreground = (Brush)new BrushConverter().ConvertFromString("#00E6B8");
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
            _updateTimer.Stop(); // NEU: Timer stoppen
            _audioService?.CleanUp();
            if (_currentStream != null)
            {
                _currentStream.Dispose();
                _currentStream = null;
            }
            // NEU: Slider und Text zurücksetzen
            ProgressSlider.Value = 0;
            TimeLabel.Text = "00:00 / 00:00";
        }

        // NEU: Methode um die UI jeden Moment anzupassen
        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            if (_currentStream != null && !_isDragging)
            {
                ProgressSlider.Value = _currentStream.CurrentTime.TotalSeconds;
                TimeLabel.Text = $"{_currentStream.CurrentTime:mm\\:ss} / {_currentStream.TotalTime:mm\\:ss}";
            }
        }

        // NEU: Wenn du den Slider ziehst (Maus klickt)
        private void ProgressSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            _isDragging = true;
        }

        // NEU: Wenn du den Slider loslässt (Maus loslassen = Spulen im Audio)
        private void ProgressSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            _isDragging = false;
            if (_currentStream != null)
            {
                _currentStream.CurrentTime = TimeSpan.FromSeconds(ProgressSlider.Value);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            StopAudio();
            _audioService?.Dispose();
            base.OnClosed(e);
        }
    }
}