using System;
using NAudio.Wave;

namespace NeuroPlayer_HighRes_V2.Core.Services;

public class HighResAudioService : IDisposable
{
    private AsioOut _asioOut;
    private IWaveProvider _currentStream;

    public event EventHandler<string> PlaybackError;

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

        string[] availableDrivers = AsioOut.GetDriverNames();
        if (availableDrivers.Length == 0)
            throw new InvalidOperationException("Kein ASIO-Treiber auf diesem System gefunden.");

        string selectedDriver = driverName ?? availableDrivers[0];

        _asioOut = new AsioOut(selectedDriver);
        _asioOut.PlaybackStopped += OnPlaybackStopped;
    }

    /// <summary>
    /// Verbindet den Audio-Stream und startet die Wiedergabe.
    /// </summary>
    public void PlayStream(IWaveProvider stream)
    {
        if (_asioOut is null)
            throw new InvalidOperationException("ASIO wurde nicht initialisiert. Rufe zuerst InitializeAsio() auf.");

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
        if (e.Exception is not null)
        {
            Console.WriteLine($"ASIO Playback Fehler: {e.Exception.Message}");
            PlaybackError?.Invoke(this, e.Exception.Message);
        }
    }

    /// <summary>
    /// Räumt den Speicher sauber auf, ohne GC-Spikes zu verursachen.
    /// </summary>
    public void CleanUp()
    {
        if (_asioOut is not null)
        {
            try 
            {
                if (_asioOut.PlaybackState == PlaybackState.Playing)
                    _asioOut.Stop();
            }
            catch { /* Ignore exception on stop if already offline */ }

            _asioOut.PlaybackStopped -= OnPlaybackStopped;
            
            try
            {
                _asioOut.Dispose();
            }
            catch { /* Ignore COM/Hardware exceptions during disconnect */ }
            
            _asioOut = null;
        }

        _currentStream = null;
    }

    private bool _disposed;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                CleanUp();
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
