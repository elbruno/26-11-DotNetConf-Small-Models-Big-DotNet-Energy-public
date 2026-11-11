// Purpose: Turn live microphone speech into text with a local Foundry Local speech model.
// Demo role: Demo 2 is Demo 1 with a live source. Only the audio changes: a microphone
//   replaces the WAV file, and ENTER ends the session.
// Requirements: Windows x64, the .NET SDK pinned by ../global.json, a working microphone, Foundry
//   Local and the cached speech model (Prepare-Demos.ps1 -PrepareSpeechModel).
// Expected result: Words appear while you speak. Press ENTER to stop and see the final transcript.
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

const string ModelId = "nemotron-speech-streaming-en-0.6b-generic-cpu:3";

// Step 1: Fail early, with a clear message, when this PC has no microphone.
if (WaveInEvent.DeviceCount == 0)
{
    throw new InvalidOperationException("No microphone was found. Connect or enable one, then run again.");
}

// Step 2: Start Foundry Local and find the speech model in its catalog.
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
var model = await catalog.GetModelVariantAsync(ModelId)
    ?? throw new InvalidOperationException($"Foundry Local does not list model variant '{ModelId}'.");

// Step 3: Load the cached model. A live demo never downloads anything.
if (!await model.IsCachedAsync())
{
    throw new InvalidOperationException(
        $"Model '{ModelId}' is not cached. Run Prepare-Demos.ps1 -PrepareSpeechModel before recording.");
}

await model.LoadAsync();
try
{
    // Step 4: Open a streaming session and describe the audio as 16-kHz mono PCM.
    using var session = new AudioSession(model);
    session.SetStreaming(true);

    using var audioQueue = new ItemQueue();
    using var request = new Request();
    request.AddItem(AudioItem.CreateFormatDescriptor("pcm", 16000, 1));
    request.AddItem(audioQueue, takeOwnership: false);
    await using var stream = session.ProcessStreamingRequestAsync(request);

    // Step 5: Capture the microphone as 16-kHz, 16-bit, mono PCM and push every buffer to the model.
    Exception? microphoneError = null;
    using var microphone = new WaveInEvent
    {
        WaveFormat = new WaveFormat(16000, 16, 1),
        BufferMilliseconds = 100,
    };
    microphone.DataAvailable += (_, e) =>
    {
        // The queue owns this copy while NAudio reuses its capture buffer.
        using var chunk = BytesItem.CreateOwned(e.Buffer.AsMemory(0, e.BytesRecorded).ToArray());
        audioQueue.Push(chunk);
    };
    // Step 6: When capture stops, tell the model the audio is complete.
    microphone.RecordingStopped += (_, e) =>
    {
        microphoneError = e.Exception;
        audioQueue.MarkFinished();
    };

    // Step 7: Start listening and print each piece of text as soon as the model produces it.
    Console.WriteLine("=== LIVE TRANSCRIPTION ACTIVE ===");
    Console.WriteLine("Speak into your microphone. Press ENTER to stop.");
    microphone.StartRecording();

    var printTranscript = Task.Run(async () =>
    {
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
    });

    // Step 8: ENTER stops the microphone. Then wait until the model has delivered every word.
    Console.ReadLine();
    microphone.StopRecording();
    await printTranscript;
    Console.WriteLine();

    // Step 9: Read the complete transcript once the stream has ended.
    using var finalResponse = await stream.FinalResponse;
    if (microphoneError is not null)
    {
        throw new InvalidOperationException("Microphone capture failed.", microphoneError);
    }

    if (finalResponse.FinishReason != FinishReason.Stop)
    {
        throw new InvalidDataException($"Speech recognition ended with {finalResponse.FinishReason}, not Stop.");
    }

    var transcript = finalResponse.OfType<SpeechResultItem>().SingleOrDefault()?.Text;
    Console.WriteLine(string.IsNullOrWhiteSpace(transcript)
        ? "Nothing was heard."
        : $"[FINAL TRANSCRIPT] {transcript}");
}
finally
{
    // Step 10: Release the model from memory, even when something above failed.
    await model.UnloadAsync();
}
