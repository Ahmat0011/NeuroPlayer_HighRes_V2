using System;
using System.Runtime.CompilerServices;
using NAudio.Wave;

namespace NeuroPlayer_HighRes_V2.Core.Services;

public sealed class HighResAudioService : IDisposable
{
    private AsioOut _asioOut;
    private IWaveProvider _currentStream;
    private bool _disposed;

    public event EventHandler<string> PlaybackError;
    public event System.EventHandler PlaybackStopped;

    public bool IsPlaying
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _asioOut?.PlaybackState == PlaybackState.Playing;
    }

    public void InitializeAsio(string driverName = null)
    {
        CleanUp();
        string[] availableDrivers = AsioOut.GetDriverNames();

        if (availableDrivers.Length == 0)
            throw new InvalidOperationException("Kein ASIO-Treiber auf diesem System gefunden.");
        
        _asioOut = new AsioOut(driverName ?? availableDrivers[0]);
        _asioOut.PlaybackStopped += OnPlaybackStopped;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PlayStream(IWaveProvider stream)
    {
        if (_asioOut is null)
            throw new InvalidOperationException("ASIO nicht initialisiert");
        _currentStream = stream;
        _asioOut.Init(_currentStream);
        _asioOut.Play();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Stop() => _asioOut?.Stop();

    private void OnPlaybackStopped(object sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            PlaybackError?.Invoke(this, e.Exception.Message);
        }
        else
        {
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    public void CleanUp()
    {
        if (_asioOut is null) return;
        try
        {
            if (IsPlaying) _asioOut.Stop();
        }
        catch { /* Ignore */ }
        
        _asioOut.PlaybackStopped -= OnPlaybackStopped;
        
        try
        {
            _asioOut.Dispose();
        }
        catch { /* Ignore COM/Hardware exceptions during disconnect */ }
        
        _asioOut = null;
        _currentStream = null;
    }

    private void Dispose(bool disposing)
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
