// Purpose: Capture bounded, mono PCM frames from the default Windows microphone.
// Demo role: Feed live audio to the local Foundry speech session without writing recordings.
// Requirements: Windows microphone access and a 16-kHz capture device.
// Expected result: At most 30 seconds of ordered audio frames or an explicit overrun/device error.
using System.Threading.Channels;
using NAudio.Wave;

namespace SpeechDemo;

public sealed class MicrophoneCapture : IAsyncDisposable
{
    private const int BufferMilliseconds = 100;
    private const int BufferedFrameLimit = 300;
    private const int MaximumCaptureBytes = 16000 * 2 * 30;

    private readonly Channel<byte[]> _frames = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(BufferedFrameLimit)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
        });
    private readonly TaskCompletionSource<Exception> _overrun =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<Exception?> _stopped =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly WaveInEvent _input = new()
    {
        DeviceNumber = -1,
        WaveFormat = new WaveFormat(16000, 16, 1),
        BufferMilliseconds = BufferMilliseconds,
    };

    private int _started;
    private int _stopRequested;
    private int _overrunReported;
    private int _capturedBytes;

    public MicrophoneCapture()
    {
        _input.DataAvailable += OnDataAvailable;
        _input.RecordingStopped += OnRecordingStopped;
    }

    public ChannelReader<byte[]> Frames => _frames.Reader;
    public Task<Exception> Overrun => _overrun.Task;
    public Task<Exception?> Stopped => _stopped.Task;

    public void Start()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            throw new InvalidOperationException("Microphone capture has already started.");
        }

        try
        {
            _input.StartRecording();
        }
        catch
        {
            Volatile.Write(ref _started, 0);
            throw;
        }
    }

    public async Task<Exception?> StopAsync()
    {
        if (Volatile.Read(ref _started) == 0)
        {
            return null;
        }

        if (Interlocked.Exchange(ref _stopRequested, 1) == 0 && !_stopped.Task.IsCompleted)
        {
            _input.StopRecording();
        }

        return await _stopped.Task.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _input.DataAvailable -= OnDataAvailable;
        _input.RecordingStopped -= OnRecordingStopped;
        _input.Dispose();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        if (eventArgs.BytesRecorded == 0 || Volatile.Read(ref _overrunReported) != 0)
        {
            return;
        }

        if (eventArgs.BytesRecorded % 2 != 0)
        {
            ReportOverrun(new InvalidDataException("Microphone input ended with a partial 16-bit PCM sample."));
            return;
        }

        if (Interlocked.Add(ref _capturedBytes, eventArgs.BytesRecorded) > MaximumCaptureBytes)
        {
            ReportOverrun(new InvalidOperationException(
                "Microphone capture exceeded the 30-second PCM limit. Capture stopped rather than truncating audio."));
            return;
        }

        var frame = eventArgs.Buffer.AsSpan(0, eventArgs.BytesRecorded).ToArray();
        if (!_frames.Writer.TryWrite(frame))
        {
            ReportOverrun(new InvalidOperationException(
                "The bounded microphone buffer filled. Capture stopped rather than dropping audio."));
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        Interlocked.Exchange(ref _stopRequested, 1);
        _frames.Writer.TryComplete(eventArgs.Exception);
        _stopped.TrySetResult(eventArgs.Exception);
    }

    private void ReportOverrun(Exception error)
    {
        if (Interlocked.Exchange(ref _overrunReported, 1) != 0)
        {
            return;
        }

        _frames.Writer.TryComplete(error);
        _overrun.TrySetResult(error);
    }
}
