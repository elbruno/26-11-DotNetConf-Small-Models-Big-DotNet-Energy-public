// Purpose: Route one short request to a bounded local decision.
// Demo role: Show native Ollama Decisions with a real probability distribution and latency.
// Requirements: .NET 11, Ollama 0.35 or later, and the nimble:latest decision model.
// Expected result: A selected supplied option, confidence, probabilities, and request time.
// In another PowerShell terminal, watch the running Ollama server without restarting it:
// Get-Content "$env:LOCALAPPDATA\Ollama\server.log" -Wait -Tail 30
// Logs show requests (POST /v1/systemone), timing and runtime/model activity, not necessarily input text.
// Inspect the selected model separately: ollama show nimble:latest

using ElBruno.AI.Decisions;
using ElBruno.AI.Decisions.Ollama;
using System.Diagnostics;
using System.Globalization;

namespace Demo05DecisionsOllama;

internal static class Program
{
    private static readonly Uri OllamaEndpoint = new("http://localhost:11434/");
    private const string ModelName = "nimble:latest";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    private static async Task<int> Main()
    {
        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        // Step 1: Define the complete set of choices the model is allowed to return.
        var options = new Dictionary<string, string?>
        {
            ["billing"] = "Invoices, charges, payments, or account balances",
            ["support"] = "Technical product problems and troubleshooting",
        };
        // Change only this literal and rerun. These are illustrative routing expectations, not measured results:
        // "Please refund the duplicate charge on my account." -> billing
        // "The app crashes every time I sign in." -> support
        // "I cannot open the billing page because the app crashes." -> mixed case; compare the probabilities.
        const string input = "Please correct my invoice because it was charged twice.";
        const string instructions = "Which team should handle this request?";

        // Step 2: Use Ollama's native decision endpoint through the Decisions library.
        using var client = new OllamaDecisionClient(new OllamaDecisionOptions
        {
            Endpoint = OllamaEndpoint,
            Model = ModelName,
            Timeout = RequestTimeout,
        });

        Console.WriteLine($"[MODEL] {ModelName}");
        Console.WriteLine("[INPUT] " + input);
        Console.WriteLine("[OPTIONS] " + string.Join(", ", options.Keys));

        // Step 3: Measure the actual local request, including any model loading time.
        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            var result = await client.ChooseAsync(
                input,
                instructions,
                options,
                shutdown.Token);
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);

            if (!options.ContainsKey(result.Choice))
            {
                throw new InvalidDataException(
                    $"The decision client returned an option that was not supplied: '{result.Choice}'.");
            }

            Console.WriteLine($"[CHOICE] {result.Choice}");
            Console.WriteLine($"[CONFIDENCE] {result.Confidence.ToString("P1", CultureInfo.InvariantCulture)}");
            Console.WriteLine("[PROBABILITIES]");
            foreach (var option in options.Keys)
            {
                if (!result.Probabilities.TryGetValue(option, out var probability))
                {
                    throw new InvalidDataException(
                        $"The decision client omitted the probability for supplied option '{option}'.");
                }

                Console.WriteLine(
                    $"  {option}: {probability.ToString("P1", CultureInfo.InvariantCulture)}");
            }

            Console.WriteLine($"[LATENCY] {elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)} ms");
            return 0;
        }
        catch (OperationCanceledException) when (!shutdown.IsCancellationRequested)
        {
            Console.Error.WriteLine(
                $"[ERROR] The local decision exceeded the {RequestTimeout.TotalSeconds:F0}-second timeout.");
            return 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("[ERROR] The local decision was cancelled.");
            return 130;
        }
        catch (DecisionException exception)
        {
            Console.Error.WriteLine($"[ERROR] Decision failed: {exception.Message}");
            return 1;
        }
        catch (HttpRequestException exception)
        {
            Console.Error.WriteLine($"[ERROR] Ollama request failed: {exception.Message}");
            return 1;
        }
    }
}
