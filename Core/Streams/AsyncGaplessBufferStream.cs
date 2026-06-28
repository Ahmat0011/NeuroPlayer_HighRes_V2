using System;
using System.Runtime.CompilerServices;
using System.Threading;
using NAudio.Wave;

namespace NeuroPlayer_HighRes_V2.Core.Streams;

public sealed class AsyncGaplessBufferStream : IWaveProvider, IDisposable
{
    private readonly object _fileLock = new();
    private readonly WaveFileReader _fileReader;
    private readonly WaveFormat _waveFormat;
    private readonly TimeSpan _totalTime;
    
    private readonly CircularBuffer _circularBuffer;
    private readonly Thread _bufferThread;
    private readonly CancellationTokenSource _cts = new();
    
    private long _bytesPlayed;
    private bool _eofReached;
    private bool _disposed;

    public WaveFormat WaveFormat => _waveFormat;
    public bool IsLooping { get; set; } = true;
    public TimeSpan TotalTime => _totalTime;

    public TimeSpan CurrentTime
    {
        get
        {
            return TimeSpan.FromSeconds((double)Interlocked.Read(ref _bytesPlayed) / _waveFormat.AverageBytesPerSecond);
        }
        set
        {
            lock (_fileLock)
            {
                long pos = (long)(value.TotalSeconds * _waveFormat.AverageBytesPerSecond);
                pos = Math.Max(0, Math.Min(pos, _fileReader.Length));
                pos -= pos % _waveFormat.BlockAlign; // Align to sample block
                
                _fileReader.Position = pos;
                _circularBuffer.Clear();
                _eofReached = false;
                Interlocked.Exchange(ref _bytesPlayed, pos);
            }
        }
    }

    public AsyncGaplessBufferStream(string filePath)
    {
        _fileReader = new WaveFileReader(filePath);
        _waveFormat = _fileReader.WaveFormat;
        _totalTime = _fileReader.TotalTime;

        // Circular buffer size: 32 MB or the file size, whichever is smaller. Aligned to BlockAlign.
        long maxBuf = Math.Min(32 * 1024 * 1024, _fileReader.Length);
        maxBuf -= maxBuf % _waveFormat.BlockAlign;
        _circularBuffer = new CircularBuffer((int)maxBuf);

        _bufferThread = new Thread(BufferLoop)
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
            Name = "NeuroPlayer_BufferThread"
        };
        _bufferThread.Start();
    }

    private void BufferLoop()
    {
        byte[] tempBuffer = new byte[65536]; // 64 KB read chunks
        while (!_cts.Token.IsCancellationRequested)
        {
            int freeSpace = _circularBuffer.FreeSpace;
            if (freeSpace < tempBuffer.Length)
            {
                Thread.Sleep(5);
                continue;
            }

            int bytesToRead = Math.Min(tempBuffer.Length, freeSpace);
            bytesToRead -= bytesToRead % _waveFormat.BlockAlign;
            if (bytesToRead == 0)
            {
                Thread.Sleep(2);
                continue;
            }

            int read = 0;
            lock (_fileLock)
            {
                if (_fileReader.Position >= _fileReader.Length)
                {
                    if (IsLooping)
                    {
                        _fileReader.Position = 0;
                    }
                    else
                    {
                        _eofReached = true;
                    }
                }

                if (!_eofReached)
                {
                    read = _fileReader.Read(tempBuffer, 0, bytesToRead);
                    if (read == 0)
                    {
                        if (IsLooping)
                        {
                            _fileReader.Position = 0;
                            read = _fileReader.Read(tempBuffer, 0, bytesToRead);
                        }
                        else
                        {
                            _eofReached = true;
                        }
                    }
                }
            }

            if (read > 0)
            {
                _circularBuffer.Write(tempBuffer.AsSpan(0, read));
            }
            else if (_eofReached && _circularBuffer.Count == 0)
            {
                Thread.Sleep(10);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int Read(byte[] buffer, int offset, int count)
    {
        if (_disposed)
        {
            new Span<byte>(buffer, offset, count).Clear();
            return 0;
        }

        int read = _circularBuffer.Read(buffer.AsSpan(offset, count));
        Interlocked.Add(ref _bytesPlayed, read);

        // Underrun handling: if the buffer is empty but we aren't at EOF, wait briefly
        int retry = 0;
        while (read < count && !_eofReached && !_disposed && retry < 10)
        {
            Thread.Sleep(2);
            int additionalRead = _circularBuffer.Read(buffer.AsSpan(offset + read, count - read));
            if (additionalRead > 0)
            {
                read += additionalRead;
                Interlocked.Add(ref _bytesPlayed, additionalRead);
            }
            retry++;
        }

        if (read < count)
        {
            // Fill the rest with silence
            new Span<byte>(buffer, offset + read, count - read).Clear();
            
            if (_eofReached)
            {
                return read; // Return actual read bytes to signal EOF
            }
            else
            {
                return count; // Return count to keep the ASIO stream active during brief underruns
            }
        }

        return count;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        if (_bufferThread.IsAlive)
        {
            _bufferThread.Join(500);
        }
        _cts.Dispose();

        lock (_fileLock)
        {
            _fileReader.Dispose();
        }
    }
}

// Simple thread-safe circular buffer
internal sealed class CircularBuffer
{
    private readonly byte[] _buffer;
    private int _head;
    private int _tail;
    private int _count;
    private readonly object _lock = new();

    public int Count { get { lock (_lock) return _count; } }
    public int FreeSpace { get { lock (_lock) return _buffer.Length - _count; } }

    public CircularBuffer(int capacity)
    {
        _buffer = new byte[capacity];
    }

    public int Write(ReadOnlySpan<byte> data)
    {
        lock (_lock)
        {
            int toWrite = Math.Min(data.Length, _buffer.Length - _count);
            if (toWrite <= 0) return 0;

            int firstPart = Math.Min(toWrite, _buffer.Length - _head);
            data.Slice(0, firstPart).CopyTo(_buffer.AsSpan(_head, firstPart));
            _head = (_head + firstPart) % _buffer.Length;

            if (toWrite > firstPart)
            {
                int secondPart = toWrite - firstPart;
                data.Slice(firstPart, secondPart).CopyTo(_buffer.AsSpan(_head, secondPart));
                _head = (_head + secondPart) % _buffer.Length;
            }

            _count += toWrite;
            return toWrite;
        }
    }

    public int Read(Span<byte> dest)
    {
        lock (_lock)
        {
            int toRead = Math.Min(dest.Length, _count);
            if (toRead <= 0) return 0;

            int firstPart = Math.Min(toRead, _buffer.Length - _tail);
            _buffer.AsSpan(_tail, firstPart).CopyTo(dest.Slice(0, firstPart));
            _tail = (_tail + firstPart) % _buffer.Length;

            if (toRead > firstPart)
            {
                int secondPart = toRead - firstPart;
                _buffer.AsSpan(_tail, secondPart).CopyTo(dest.Slice(firstPart, secondPart));
                _tail = (_tail + secondPart) % _buffer.Length;
            }

            _count -= toRead;
            return toRead;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _head = 0;
            _tail = 0;
            _count = 0;
        }
    }
}