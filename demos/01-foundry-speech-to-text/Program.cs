// Purpose: Turn one fixed WAV file into text with a local Foundry Local speech model.
// Demo role: Demo 1 is the shortest speech-to-text path. Demo 2 repeats it with a live microphone.
// Requirements: Windows x64, the .NET SDK pinned by ../global.json, Foundry Local, the cached
//   speech model (Prepare-Demos.ps1 -PrepareSpeechModel) and the fixture WAV (see README.md).
// Expected result: The text streams in piece by piece, then the final transcript is printed.
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

const string ModelId = "nemotron-speech-streaming-en-0.6b-generic-cpu:3";
const string WavPath = @"artifacts\fixture\english-cleanup-cases.wav";

// Step 1: Check the input first, so a missing or unsupported file fails before any model work.
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

    // Step 8: Read the complete transcript once the stream has ended.
    using var finalResponse = await stream.FinalResponse;
    if (finalResponse.FinishReason != FinishReason.Stop)
    {
        throw new InvalidDataException($"Speech recognition ended with {finalResponse.FinishReason}, not Stop.");
    }

    var transcript = finalResponse.OfType<SpeechResultItem>().SingleOrDefault()?.Text;
    if (string.IsNullOrWhiteSpace(transcript))
    {
        throw new InvalidDataException("Speech recognition returned an empty transcript.");
    }

    Console.WriteLine($"[FINAL TRANSCRIPT] {transcript}");
}
finally
{
    // Step 9: Release the model from memory, even when something above failed.
    await model.UnloadAsync();
}
