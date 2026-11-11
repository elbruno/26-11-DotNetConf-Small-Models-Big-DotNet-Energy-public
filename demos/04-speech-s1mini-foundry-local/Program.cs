// Purpose: Transcribe a fixed WAV file with Foundry Local, then clean the real transcript with S1-Mini, also in Foundry Local.
// Demo role: Demo 4 repeats Demo 3 with one change: S1-Mini runs as a Foundry Local chat model, not through the library.
//   Steps 1 to 9 repeat Demo 1. Steps 10 to 14 are new.
// Requirements: Windows x64, the .NET SDK pinned by ../global.json, Foundry Local, the cached speech
//   model, and the staged S1-Mini ONNX package (tools\Stage-FoundryS1Mini.ps1, run by Prepare-Demos.ps1).
// Expected result: The raw transcript, then the cleaned transcript streamed from S1-Mini. Output can vary between runs.
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using System.Text;

const string SpeechModelId = "nemotron-speech-streaming-en-0.6b-generic-cpu:3";
const string CleanupModelId = "s1-mini-int4:2";
const string WavPath = @"artifacts\fixture\english-cleanup-cases.wav";
const string StagedModelPath = @"artifacts\foundry-s1mini-cache\s1-mini-int4";
const string SystemPrompt =
    "You are a text normalizer for speech-to-text transcripts. The input begins with a control line " +
    "specifying the styling, structure, and context settings; clean the transcript to match those " +
    "settings and output only the cleaned text.";
const string ControlLine = "[Styling: semi-formal] [Structure: prose] [Context: general]";

// Step 1: Check the WAV before starting the speech model.
if (!File.Exists(WavPath))
{
    throw new FileNotFoundException(
        "The WAV file was not found. From this demo directory, run .\\tools\\New-SyntheticFixture.ps1 " +
        "or ..\\..\\Prepare-Demos.ps1 -FixturesOnly, then run dotnet run again.",
        WavPath);
}

using var wav = new WaveFileReader(WavPath);
if (wav.WaveFormat is not { Encoding: WaveFormatEncoding.Pcm, SampleRate: 16000, Channels: 1, BitsPerSample: 16 })
{
    throw new InvalidDataException("The speech model needs 16-kHz, 16-bit, mono PCM audio.");
}

// Step 2: Create ONE manager for both models. The SDK uses a process singleton, so never recreate it.
using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
await FoundryLocalManager.CreateAsync(
    new Configuration
    {
        AppName = "SmallModelsSpeechDemo",
        LogLevel = Microsoft.AI.Foundry.Local.LogLevel.Warning,
    },
    loggerFactory.CreateLogger("FoundryLocal"));

using var manager = FoundryLocalManager.Instance;
var catalog = await manager.GetCatalogAsync();
var speechModel = await catalog.GetModelVariantAsync(SpeechModelId)
    ?? throw new InvalidOperationException($"Foundry Local does not list model variant '{SpeechModelId}'.");

// Step 3: Load the cached speech model. A live demo never downloads anything.
if (!await speechModel.IsCachedAsync())
{
    throw new InvalidOperationException(
        $"Model '{SpeechModelId}' is not cached. Run Prepare-Demos.ps1 -PrepareSpeechModel before recording.");
}

string rawTranscript;
await speechModel.LoadAsync();
try
{
    // Step 4: Open a streaming session and describe the audio as 16-kHz mono PCM.
    using var session = new AudioSession(speechModel);
    session.SetStreaming(true);

    using var audioQueue = new ItemQueue();
    using var request = new Request();
    request.AddItem(AudioItem.CreateFormatDescriptor("pcm", 16000, 1));
    request.AddItem(audioQueue, takeOwnership: false);
    await using var stream = session.ProcessStreamingRequestAsync(request);

    // Step 5: Push the WAV audio into the queue in small chunks while the model transcribes it.
    var sendAudio = Task.Run(() =>
    {
        try
        {
            var buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = wav.Read(buffer, 0, buffer.Length)) > 0)
            {
                // The queue owns this copy while the reader reuses its buffer.
                using var chunk = BytesItem.CreateOwned(buffer.AsMemory(0, bytesRead).ToArray());
                audioQueue.Push(chunk);
            }
        }
        finally
        {
            // Step 6: Tell the model the audio has ended, so it can finish the transcript.
            audioQueue.MarkFinished();
        }
    });

    // Step 7: Print each piece of text as soon as the model produces it.
    Console.Write("[STREAMING] ");
    await foreach (var item in stream)
    {
        using (item)
        {
            if (item is SpeechSegmentItem segment)
            {
                Console.Write(segment.Text);
            }
        }
    }

    await sendAudio;
    Console.WriteLine();

    // Step 8: Read the complete transcript once the stream has ended. This is the cleanup input.
    using var finalResponse = await stream.FinalResponse;
    if (finalResponse.FinishReason != FinishReason.Stop)
    {
        throw new InvalidDataException($"Speech recognition ended with {finalResponse.FinishReason}, not Stop.");
    }

    var transcript = finalResponse.OfType<SpeechResultItem>().SingleOrDefault()?.Text;
    if (string.IsNullOrWhiteSpace(transcript))
    {
        throw new InvalidDataException("Foundry Local speech returned an empty transcript; skipping S1-Mini cleanup.");
    }

    rawTranscript = transcript;
}
finally
{
    // Step 9: Release the speech model before S1-Mini starts, even when something above failed.
    await speechModel.UnloadAsync();
}

// Step 10: Register the staged S1-Mini ONNX package locally, with explicit CPU metadata.
// Source: https://huggingface.co/elbruno/s1-mini-onnx (int4), an unofficial conversion of superwhisper/s1-mini.
// Preparation copies cached files and changes only the staged chat template; it does not train or export weights.
var cleanupModelPath = Path.GetFullPath(StagedModelPath);
if (!File.Exists(Path.Combine(cleanupModelPath, "model.onnx")) ||
    !File.Exists(Path.Combine(cleanupModelPath, "model.onnx.data")) ||
    !File.Exists(Path.Combine(cleanupModelPath, "chat_template.jinja")))
{
    throw new DirectoryNotFoundException(
        "The staged S1-Mini package is incomplete. Run tools\\Stage-FoundryS1Mini.ps1 first.");
}

var localCatalog = await manager.GetCatalogAsync(CatalogType.Local);
var metadata = new ModelInfoBuilder()
    .SetStringProperty(ModelInfoPropertyKeys.Task, "chat-completion")
    .SetStringProperty(ModelInfoPropertyKeys.DisplayName, "S1-Mini INT4 via Foundry Local")
    .SetStringProperty(ModelInfoPropertyKeys.DeviceType, "CPU")
    .SetStringProperty(ModelInfoPropertyKeys.ExecutionProvider, "CPUExecutionProvider")
    .SetIntProperty(ModelInfoPropertyKeys.ContextLength, 40960);
var cleanupModel = await localCatalog.RegisterModelAsync(cleanupModelPath, CleanupModelId, metadata);

try
{
    // Step 11: Load S1-Mini and send it the recognized text, unchanged, in a chat session.
    await cleanupModel.LoadAsync();
    try
    {
        using var chat = new ChatSession(cleanupModel);
        chat.SetStreaming(true);
        using var chatRequest = new Request();
        chatRequest.AddItem(MessageItem.System(SystemPrompt));
        chatRequest.AddItem(MessageItem.User($"{ControlLine}\n{rawTranscript}"));

        Console.WriteLine();
        Console.WriteLine($"[CLEANUP MODEL] {cleanupModel.Id} via CPUExecutionProvider");
        Console.WriteLine("[RAW TRANSCRIPT]");
        Console.WriteLine(rawTranscript);
        Console.WriteLine("[CLEANED TRANSCRIPT]");

        // Step 12: Print the cleaned text as it streams in, and keep it to check that something came back.
        var cleaned = new StringBuilder();
        await using var responseStream = chat.ProcessStreamingRequestAsync(chatRequest);
        await foreach (var item in responseStream)
        {
            using (item)
            {
                if (item is TextItem textItem)
                {
                    cleaned.Append(textItem.Text);
                    Console.Write(textItem.Text);
                }
            }
        }

        using var cleanupResponse = await responseStream.FinalResponse;
        if (cleanupResponse.FinishReason != FinishReason.Stop)
        {
            throw new InvalidDataException($"S1-Mini generation ended with {cleanupResponse.FinishReason}, not Stop.");
        }

        Console.WriteLine();
        if (string.IsNullOrWhiteSpace(cleaned.ToString()))
        {
            throw new InvalidDataException("S1-Mini returned an empty cleaned transcript.");
        }
    }
    finally
    {
        // Step 13: Release S1-Mini, even when something above failed.
        await cleanupModel.UnloadAsync();
    }
}
finally
{
    // Step 14: Remove only the temporary registration. The staged files stay untouched.
    await localCatalog.UnregisterModelAsync(cleanupModel.Id);
}

Console.WriteLine();
Console.WriteLine("[DEMO 4 COMPLETE] Text normalization through Foundry Local. Output can vary between runs.");
