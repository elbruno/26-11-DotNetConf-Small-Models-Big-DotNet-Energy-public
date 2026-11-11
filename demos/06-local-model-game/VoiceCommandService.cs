// Purpose: Capture one explicitly armed utterance and pass it to the shared local ASR component.
// Demo role: Accept exact START, PAUSE, or STOP commands without always-on microphone access.
// Requirements: A cached Nemotron speech model and a Windows microphone.
// Expected result: One final transcript, or a visible error with no audio file created.
using System.Collections.Concurrent;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using SpeechDemo;

namespace SmallModels.SpaceGame;

public enum VoiceCommand
{
    Start,
    Pause,
    Stop,
}

public static class VoiceCommandParser
{
    public static bool TryParse(string transcript, out VoiceCommand command)
    {
        var exactText = transcript.Trim();
        if (string.Equals(exactText, "START", StringComparison.OrdinalIgnoreCase))
        {
            command = VoiceCommand.Start;
            return true;
        }

        if (string.Equals(exactText, "PAUSE", StringComparison.OrdinalIgnoreCase))
        {
            command = VoiceCommand.Pause;
            return true;
        }

        if (string.Equals(exactText, "STOP", StringComparison.OrdinalIgnoreCase))
        {
            command = VoiceCommand.Stop;
            return true;
        }

        command = default;
        return false;
    }
}

internal sealed class VoiceCommandService : IAsyncDisposable
{
    private static readonly TimeSpan CaptureLimit = TimeSpan.FromSeconds(3);
    private readonly CancellationTokenSource _shutdown;
    private readonly ConcurrentQueue<string> _transcripts = new();
    private LocalVoiceRecognizer? _recognizer;
    private Task<LocalVoiceRecognizer>? _prepareTask;
    private MicrophoneCapture? _capture;
    private Task<List<byte[]>>? _frameCollection;
    private Task? _recognitionTask;
    private TimeSpan _captureRemaining;
    private string _status = "VOICE MODEL NOT PREPARED. PRESS V TO PREPARE.";

    public VoiceCommandService(CancellationToken shutdownToken)
    {
        _shutdown = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
    }

    public string Status => Volatile.Read(ref _status);

    public bool TryDequeueTranscript(out string transcript) =>
        _transcripts.TryDequeue(out transcript!);

    public void ToggleListening()
    {
        if (_capture is not null)
        {
            StopCapture();
            return;
        }

        if (_recognitionTask is { IsCompleted: false })
        {
            SetStatus("VOICE COMMAND PROCESSING.");
            return;
        }

        if (_recognizer is null)
        {
            if (_prepareTask is null)
            {
                SetStatus("PREPARING LOCAL SPEECH MODEL. MICROPHONE IS OFF.");
                _prepareTask = LocalVoiceRecognizer.CreateAsync(_shutdown.Token);
            }
            else
            {
                SetStatus("LOCAL SPEECH MODEL IS PREPARING. MICROPHONE IS OFF.");
            }

            return;
        }

        StartCapture();
    }

    public void Update(float elapsedSeconds)
    {
        if (_prepareTask is { IsCompleted: true } prepareTask)
        {
            _prepareTask = null;
            try
            {
                _recognizer = prepareTask.GetAwaiter().GetResult();
                SetStatus("VOICE MODEL READY. PRESS V TO LISTEN.");
            }
            catch (Exception exception)
            {
                ReportFailure("MODEL PREPARATION", exception);
            }
        }

        if (_capture is not null)
        {
            _captureRemaining -= TimeSpan.FromSeconds(Math.Max(0, elapsedSeconds));
            if (_captureRemaining <= TimeSpan.Zero)
            {
                StopCapture();
            }
        }

        if (_recognitionTask is { IsCompleted: true } recognitionTask)
        {
            _recognitionTask = null;
            try
            {
                recognitionTask.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                ReportFailure("RECOGNITION", exception);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();

        if (_capture is not null)
        {
            var capture = _capture;
            var frameCollection = _frameCollection;
            _capture = null;
            _frameCollection = null;
            await capture.StopAsync();
            if (frameCollection is not null)
            {
                await frameCollection;
            }

            await capture.DisposeAsync();
        }

        if (_recognitionTask is not null)
        {
            await _recognitionTask;
        }

        if (_prepareTask is not null)
        {
            try
            {
                _recognizer = await _prepareTask;
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                SetStatus("VOICE PREPARATION CANCELLED.");
            }
            catch (Exception exception)
            {
                ReportFailure("MODEL PREPARATION", exception);
            }
        }

        if (_recognizer is not null)
        {
            await _recognizer.DisposeAsync();
        }

        _shutdown.Dispose();
    }

    private void StartCapture()
    {
        MicrophoneCapture? capture = null;
        try
        {
            if (WaveInEvent.DeviceCount == 0)
            {
                throw new InvalidOperationException("No microphone was found. Connect or enable an input device.");
            }

            capture = new MicrophoneCapture();
            capture.Start();
            _capture = capture;
            _frameCollection = CollectFramesAsync(capture);
            _captureRemaining = CaptureLimit;
            SetStatus("LISTENING FOR ONE COMMAND. PRESS V TO SUBMIT.");
        }
        catch (Exception exception)
        {
            ReportFailure("MICROPHONE", exception);
            try
            {
                capture?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception cleanupException)
            {
                ReportFailure("MICROPHONE CLEANUP", cleanupException);
            }
        }
    }

    private void StopCapture()
    {
        var capture = _capture;
        var frameCollection = _frameCollection;
        _capture = null;
        _frameCollection = null;
        _captureRemaining = TimeSpan.Zero;

        if (capture is null || frameCollection is null)
        {
            return;
        }

        SetStatus("PROCESSING FINAL SPEECH RESULT.");
        _recognitionTask = StopAndRecognizeAsync(capture, frameCollection, _shutdown.Token);
    }

    private async Task StopAndRecognizeAsync(
        MicrophoneCapture capture,
        Task<List<byte[]>> frameCollection,
        CancellationToken cancellationToken)
    {
        try
        {
            var stopError = await capture.StopAsync();
            if (stopError is not null)
            {
                throw stopError;
            }

            var frames = await frameCollection;
            if (frames.Count == 0)
            {
                throw new InvalidDataException("No microphone audio was captured.");
            }

            var recognizer = _recognizer
                ?? throw new InvalidOperationException("The local speech model is not ready.");
            var result = await recognizer.TranscribeFramesAsync(frames, cancellationToken);
            _transcripts.Enqueue(result.Transcript);
            SetStatus("SPEECH RESULT READY. COMMAND WILL BE VALIDATED.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus("VOICE COMMAND CANCELLED.");
        }
        catch (Exception exception)
        {
            ReportFailure("RECOGNITION", exception);
        }
        finally
        {
            await capture.DisposeAsync();
        }
    }

    private static async Task<List<byte[]>> CollectFramesAsync(
        MicrophoneCapture capture)
    {
        var frames = new List<byte[]>();
        await foreach (var frame in capture.Frames.ReadAllAsync())
        {
            frames.Add(frame);
        }

        return frames;
    }

    private void SetStatus(string value) => Volatile.Write(ref _status, value);

    private void ReportFailure(string stage, Exception exception)
    {
        // Keep the full exception and inner cause in the local console, not in the audience HUD.
        Console.Error.WriteLine($"VOICE {stage} FAILED:{Environment.NewLine}{exception}");
        SetStatus(DescribeFailure(stage, exception));
    }

    internal static string DescribeFailure(string stage, Exception exception)
    {
        var cause = exception.GetBaseException();
        var reason = cause switch
        {
            OperationCanceledException => "CANCELLED.",
            FileNotFoundException or DirectoryNotFoundException =>
                "MODEL FILES MISSING. RUN PREPARE-DEMOS.PS1 -PREPARESPEECHMODEL.",
            KeyNotFoundException => "SPEECH VARIANT NOT IN CATALOG.",
            DllNotFoundException or BadImageFormatException or PlatformNotSupportedException =>
                "RUNTIME OR DEVICE UNSUPPORTED.",
            InvalidDataException => "INVALID MODEL OR AUDIO DATA.",
            _ when stage.StartsWith("MICROPHONE", StringComparison.Ordinal) =>
                "CHECK INPUT DEVICE AND MICROPHONE ACCESS.",
            _ => $"{stage} FAILED.",
        };
        return $"VOICE UNAVAILABLE: {reason} SEE CONSOLE. KEYBOARD STILL WORKS.";
    }
}

internal sealed class LocalVoiceRecognizer : IAsyncDisposable
{
    private const string SpeechModelId = "nemotron-speech-streaming-en-0.6b-generic-cpu:3";
    private readonly FoundryLocalManager _manager;
    private readonly IModel _model;
    private readonly ILoggerFactory _loggerFactory;
    private bool _loaded = true;

    private LocalVoiceRecognizer(
        FoundryLocalManager manager,
        IModel model,
        ILoggerFactory loggerFactory)
    {
        _manager = manager;
        _model = model;
        _loggerFactory = loggerFactory;
    }

    public static async Task<LocalVoiceRecognizer> CreateAsync(CancellationToken cancellationToken)
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        FoundryLocalManager? manager = null;
        try
        {
            await FoundryLocalManager.CreateAsync(
                new Configuration
                {
                    // Share the cache prepared for the speech demos, without copying or downloading models.
                    AppName = "SmallModelsSpeechDemo",
                    LogLevel = Microsoft.AI.Foundry.Local.LogLevel.Warning,
                },
                loggerFactory.CreateLogger("FoundryLocal"),
                cancellationToken);

            manager = FoundryLocalManager.Instance;
            var catalog = await manager.GetCatalogAsync(cancellationToken);
            var model = await catalog.GetModelVariantAsync(SpeechModelId, cancellationToken)
                ?? throw new KeyNotFoundException(
                    $"Foundry Local does not list model variant '{SpeechModelId}'.");

            if (!await model.IsCachedAsync())
            {
                throw new FileNotFoundException(
                    $"Speech model '{SpeechModelId}' is not cached for SmallModelsSpeechDemo. " +
                    "Run Prepare-Demos.ps1 -PrepareSpeechModel before launching the game.");
            }

            await model.LoadAsync(cancellationToken);
            var recognizer = new LocalVoiceRecognizer(manager, model, loggerFactory);
            manager = null;
            return recognizer;
        }
        catch
        {
            manager?.Dispose();
            loggerFactory.Dispose();
            throw;
        }
    }

    public async Task<LocalSpeechResult> TranscribeFramesAsync(
        IReadOnlyList<byte[]> frames,
        CancellationToken cancellationToken)
    {
        using var session = new AudioSession(_model);
        session.SetStreaming(true);
        return await LocalSpeechRecognizer.TranscribeFramesAsync(
            session,
            frames,
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_loaded)
            {
                _loaded = false;
                await _model.UnloadAsync(CancellationToken.None);
            }
        }
        finally
        {
            _manager.Dispose();
            _loggerFactory.Dispose();
        }
    }
}
