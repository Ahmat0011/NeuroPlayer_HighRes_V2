using System;
using NAudio.Wave;

namespace NeuroPlayer_HighRes_V2.Core.Services
{
    public class HighResAudioService : IDisposable
    {
        private AsioOut _asioOut;
        private IWaveProvider _currentStream;

        // Gibt zurück, ob das Gerät gerade abspielt
        public bool IsPlaying => _asioOut?.PlaybackState == PlaybackState.Playing;

        public HighResAudioService()
        {
            // Initialisierung ohne direktes Starten
        }

        /// <summary>
        /// Initialisiert den ASIO-Treiber exklusiv für bit-perfect Audio.
        /// </summary>
        /// <param name="driverName">Optionaler Name des ASIO-Treibers. Wenn null, wird der erste verfügbare genommen.</param>
        public void InitializeAsio(string driverName = null)
        {
            CleanUp(); // Sicherstellen, dass alles vorherige geschlossen ist

            // Nimmt den ersten ASIO Treiber (z.B. "Lavaudio USB Audio ASIO Driver"), falls keiner spezifiziert ist
            string selectedDriver = driverName ?? AsioOut.GetDriverNames()[0];
            
            _asioOut = new AsioOut(selectedDriver);
            _asioOut.PlaybackStopped += OnPlaybackStopped;
        }

        /// <summary>
        /// Verbindet den Audio-Stream und startet die Wiedergabe.
        /// </summary>
        public void PlayStream(IWaveProvider stream)
        {
            if (_asioOut == null)
            {
                throw new InvalidOperationException("ASIO wurde nicht initialisiert. Rufe zuerst InitializeAsio() auf.");
            }

            _currentStream = stream;
            
            // ASIO initialisieren und starten
            _asioOut.Init(_currentStream);
            _asioOut.Play();
        }

        public void Stop()
        {
            _asioOut?.Stop();
        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            // Event für die Benutzeroberfläche (kann später erweitert werden)
            if (e.Exception != null)
            {
                Console.WriteLine($"ASIO Playback Fehler: {e.Exception.Message}");
            }
        }

        /// <summary>
        /// Räumt den Speicher sauber auf, ohne GC-Spikes zu verursachen.
        /// </summary>
        public void CleanUp()
        {
            if (_asioOut != null)
            {
                if (_asioOut.PlaybackState == PlaybackState.Playing)
                {
                    _asioOut.Stop();
                }
                
                _asioOut.PlaybackStopped -= OnPlaybackStopped;
                _asioOut.Dispose();
                _asioOut = null;
            }

            _currentStream = null; 
        }

        public void Dispose()
        {
            CleanUp();
            GC.SuppressFinalize(this);
        }
    }
}
