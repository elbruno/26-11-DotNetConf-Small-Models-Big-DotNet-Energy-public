// Purpose: Download and cache the Foundry Local speech model once, before any recording.
// Demo role: Preparation only. Demos 1 to 4 never download models while you present.
// Requirements: Windows x64, the .NET SDK pinned by ../../global.json, Foundry Local and network
//   access for the first download. Run it through Prepare-Demos.ps1 -PrepareSpeechModel.
// Expected result: The model is cached and loads once, then the program exits.
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging;

const string ModelId = "nemotron-speech-streaming-en-0.6b-generic-cpu:3";

// Step 1: Start Foundry Local with the same app name the demos use, so they share one model cache.
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

// Step 2: Download the model only when it is not cached yet.
if (!await model.IsCachedAsync())
{
    Console.WriteLine($"Downloading {ModelId}. This can take a while.");
    await model.DownloadAsync();
}

// Step 3: Load the model once to prove the cached files work, then release it.
await model.LoadAsync();
await model.UnloadAsync();
Console.WriteLine($"Speech model is cached and loadable: {ModelId}");
