using System;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading;
using NAudio.Wave;

namespace NeuroPlayer_HighRes_V2.Core.Streams;

public unsafe class AsyncGaplessBufferStream : IWaveProvider, IDisposable
{
    private readonly WaveFileReader _fileReader;
    private readonly BufferedWaveProvider _bufferedWaveProvider;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Thread _bufferThread;
    private readonly object _lockObj = new();

    private long _bytesPlayed;
    private bool _disposed;
    private byte* _nativeBuffer;
    private byte[] _managedBuffer;
    private int _bufferSize;

    public WaveFormat WaveFormat => _fileReader.WaveFormat;
    public bool IsLooping { get; set; } = true;
    public TimeSpan TotalTime => _fileReader.TotalTime;

    public TimeSpan CurrentTime
    {
        get
        {
            lock (_lockObj)
            {
                return TimeSpan.FromSeconds(Interlocked.Read(ref _bytesPlayed) / WaveFormat.AverageBytesPerSecond);
            }
        }
        set
        {
            lock (_lockObj)
            {
                _fileReader.CurrentTime = value;
                Interlocked.Exchange(ref _bytesPlayed, _fileReader.Position);
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
        _bufferSize = _fileReader.WaveFormat.AverageBytesPerSecond / 10;
        _bufferSize -= _bufferSize % _fileReader.WaveFormat.BlockAlign;
        _nativeBuffer = (byte*)NativeMemory.Alloc((nuint)_bufferSize);
        _managedBuffer = new byte[_bufferSize];
        _bufferThread = new Thread(BufferDataLoop)
        {
            IsBackground = true,
            Priority = ThreadPriority.Highest,
            Name = "NeuroHighRes_BufferThread"
        };
        Thread.BeginThreadAffinity();
        _bufferThread.Start();
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void BufferDataLoop()
    {
        try
        {
            bool isBufferFull;
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                isBufferFull = false;
                lock (_lockObj)
                {
                    if (_bufferedWaveProvider.BufferedBytes + _bufferSize <= _bufferedWaveProvider.BufferLength)
                    {
                        int bytesRead = _fileReader.Read(new Span<byte>(_nativeBuffer, _bufferSize));
                        if (bytesRead == 0 && IsLooping)
                        {
                            _fileReader.Position = 0;
                            bytesRead = _fileReader.Read(new Span<byte>(_nativeBuffer, _bufferSize));
                        }
                        if (bytesRead > 0)
                        {
                            new Span<byte>(_nativeBuffer, bytesRead).CopyTo(_managedBuffer);
                            _bufferedWaveProvider.AddSamples(_managedBuffer, 0, bytesRead);
                        }
                    }
                    else
                    {
                        isBufferFull = true;
                    }
                }
                if (isBufferFull)
                {
                    Thread.Sleep(5); // CPU-Entlastung
                }
            }
        }
        finally
        {
            Thread.EndThreadAffinity();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Read(byte[] buffer, int offset, int count)
    {
        long currentBytes = Interlocked.Read(ref _bytesPlayed);
        if (!IsLooping && currentBytes >= _fileReader.Length)
        {
            return 0;
        }
        int read = _bufferedWaveProvider.Read(buffer, offset, count);
        // SIMD-optimierte Nullung
        if (read < count)
        {
            ZeroBuffer(buffer.AsSpan(offset + read, count - read));
        }
        Interlocked.Add(ref _bytesPlayed, read);
        currentBytes = Interlocked.Read(ref _bytesPlayed);
        if (IsLooping && currentBytes >= _fileReader.Length)
        {
            Interlocked.Exchange(ref _bytesPlayed, currentBytes % _fileReader.Length);
        }
        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ZeroBuffer(Span<byte> buffer)
    {
        if (Avx2.IsSupported && buffer.Length >= 32)
        {
            var zeroVector = Vector256<byte>.Zero;
            ref byte ptr = ref MemoryMarshal.GetReference(buffer);
            int i = 0;
            for (; i <= buffer.Length - 32; i += 32)
            {
                Unsafe.As<byte, Vector256<byte>>(ref Unsafe.Add(ref ptr, i)) = zeroVector;
            }
            // Rest mit kleineren Blöcken
            for (; i < buffer.Length; i++)
            {
                Unsafe.Add(ref ptr, i) = 0;
            }
        }
        else
        {
            buffer.Clear();
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _cancellationTokenSource.Cancel();
                if (_bufferThread.IsAlive)
                    _bufferThread.Join(1000);
                _cancellationTokenSource.Dispose();
                _fileReader.Dispose();
                _bufferedWaveProvider.ClearBuffer();
            }
            if (_nativeBuffer != null)
            {
                NativeMemory.Free(_nativeBuffer);
                _nativeBuffer = null;
            }
            _disposed = true;
        }
    }

    public void Dispose() => Dispose(true);
}