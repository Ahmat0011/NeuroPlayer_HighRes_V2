using System;
using System.Threading;
using NAudio.Wave;

namespace NeuroPlayer_HighRes_V2.Core.Streams;

public class AsyncGaplessBufferStream : IWaveProvider, IDisposable
{
    private readonly WaveFileReader _fileReader;
    private readonly BufferedWaveProvider _bufferedWaveProvider;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Thread _bufferThread;

    // Gewährleistet, dass Slider (UI) und Background-Load NICHT zeitgleich kollidieren!
    private readonly object _lockObj = new();

    private long _bytesPlayed = 0;

    public WaveFormat WaveFormat => _fileReader.WaveFormat;
    public bool IsLooping { get; set; } = true;
    public TimeSpan TotalTime => _fileReader.TotalTime;

    public TimeSpan CurrentTime
    {
        get
        {
            lock (_lockObj)
            {
                return TimeSpan.FromSeconds((double)_bytesPlayed / WaveFormat.AverageBytesPerSecond);
            }
        }
        set
        {
            lock (_lockObj) // Kritischer Crash-Schutz beim Slider-Spulen!
            {
                _fileReader.CurrentTime = value;
                _bytesPlayed = _fileReader.Position;
                _bufferedWaveProvider.ClearBuffer();
            }
        }
    }

    public AsyncGaplessBufferStream(string filePath, int bufferSeconds = 3)
    {
        _fileReader = new WaveFileReader(filePath);

        _bufferedWaveProvider = new BufferedWaveProvider(_fileReader.WaveFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(bufferSeconds),
            DiscardOnBufferOverflow = false,
            ReadFully = false
        };

        _cancellationTokenSource = new CancellationTokenSource();

        // Dedizierter Thread statt Task.Run (Verhindert GC-Lags im ThreadPool)
        _bufferThread = new Thread(BufferDataLoop)
        {
            IsBackground = true,
            Priority = ThreadPriority.Highest,
            Name = "NeuroHighRes_BufferThread"
        };
        _bufferThread.Start();
    }

    private void BufferDataLoop()
    {
        int readSize = _fileReader.WaveFormat.AverageBytesPerSecond / 10;
        readSize -= readSize % _fileReader.WaveFormat.BlockAlign; // Perfekt!
        byte[] readBuffer = new byte[readSize];

        // Hoisted outside loop: value type, zero heap allocation in the hot path.
        // True = buffer full or EOF reached without looping (triggers sleep to yield CPU).
        bool isBufferFull;

        while (!_cancellationTokenSource.IsCancellationRequested)
        {
            isBufferFull = false;

            lock (_lockObj) // Schützt den Reader vor dem UI-Thread
            {
                if (_bufferedWaveProvider.BufferedBytes + readSize <= _bufferedWaveProvider.BufferLength)
                {
                    int bytesRead = _fileReader.Read(readBuffer, 0, readSize);

                    if (bytesRead == 0)
                    {
                        if (IsLooping)
                        {
                            _fileReader.Position = 0;
                            // Direkter Neuzugriff verhindert Mikrolücken beim erneuten Load
                            bytesRead = _fileReader.Read(readBuffer, 0, readSize);
                        }
                        else
                        {
                            isBufferFull = true; // CPU-Hog-Schutz am Dateiende
                        }
                    }

                    if (bytesRead > 0)
                    {
                        _bufferedWaveProvider.AddSamples(readBuffer, 0, bytesRead);
                    }
                }
                else
                {
                    isBufferFull = true;
                }
            }

            if (isBufferFull)
            {
                Thread.Sleep(5); // Entlastet Hardware extrem effizient
            }
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        // EOF STOP-SIGNAL AN NAUDIO/ASIO
        if (!IsLooping && _bytesPlayed >= _fileReader.Length)
        {
            return 0;
        }

        int read = _bufferedWaveProvider.Read(buffer, offset, count);

        // KRITISCHER ASIO-DAC-SCHUTZ:
        // Falls die Festplatte dem RAM nicht hinterherkommt, knallt das alte Byte-Mapping
        // laut in den DAC! Zero-Fill garantiert absolute Stille anstelle von kaputten Bytes!
        if (read < count)
        {
            Array.Clear(buffer, offset + read, count - read);
            // WICHTIG: Erhöhe read hier NICHT künstlich auf count, damit _bytesPlayed
            // nur bei tatsächlichen Audiodaten wächst und nicht durch Stille-Lücken (Underruns).
        }

        _bytesPlayed += read;

        if (IsLooping && _bytesPlayed >= _fileReader.Length)
        {
            _bytesPlayed %= _fileReader.Length;
        }

        // ASIO benötigt exakt 'count' Bytes, ob mit Audiodaten oder Stille gefüllt.
        return count;
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();

        if (_bufferThread.IsAlive)
            _bufferThread.Join(1000);

        _cancellationTokenSource.Dispose();
        _fileReader.Dispose();
        _bufferedWaveProvider.ClearBuffer();

        GC.SuppressFinalize(this);
    }
}