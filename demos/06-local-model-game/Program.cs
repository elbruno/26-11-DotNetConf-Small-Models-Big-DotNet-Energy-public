// Purpose: Start the interactive local-model side-scroller.
// Demo role: Connect the MonoGame window to local Decisions and the reusable speech pipeline.
// Requirements: .NET 11, a desktop graphics driver, and pre-cached models for live features.
// Expected result: A playable four-wave mission with optional keyboard and voice controls.
using ElBruno.AI.Decisions.Ollama;

namespace SmallModels.SpaceGame;

internal static class Program
{
    private static readonly Uri OllamaEndpoint = new("http://localhost:11434/");
    private const string DecisionModel = "nimble:latest";

    private static async Task<int> Main()
    {
        // Step 1: Connect the game's movement choices to the local Ollama model.
        var decisionClient = new OllamaDecisionClient(new OllamaDecisionOptions
        {
            Endpoint = OllamaEndpoint,
            Model = DecisionModel,
            Timeout = TimeSpan.FromSeconds(60),
        });

        // Step 2: Start the local speech path and open the game window.
        using var shutdown = new CancellationTokenSource();
        var voiceCommands = new VoiceCommandService(shutdown.Token);
        var session = new GameSession(decisionClient);
        var window = new SpaceGameWindow(session, voiceCommands);

        try
        {
            window.Run();
            return 0;
        }
        finally
        {
            shutdown.Cancel();
            await session.DisposeAsync();
            await voiceCommands.DisposeAsync();
            window.Dispose();
        }
    }
}
