// Purpose: Validate local movement decisions and retain the five most recent applied results.
// Demo role: Let Ollama choose legal combat movement while C# owns physics, combat, and scoring.
// Requirements: Ollama's native Decisions endpoint and a compatible local model.
// Expected result: One legal movement action, or a visible error with no fallback.
using System.Diagnostics;
using ElBruno.AI.Decisions;

namespace SmallModels.SpaceGame;

internal sealed record ModelDecision(
    string Choice,
    IReadOnlyDictionary<string, double> Probabilities);

public sealed record DecisionLogEntry(
    int Number,
    string Observation,
    string Choice,
    double Probability,
    double LatencyMilliseconds);

internal delegate Task<ModelDecision> ChooseDecisionAsync(
    string snapshot,
    string instructions,
    IReadOnlyDictionary<string, string?> options,
    CancellationToken cancellationToken);

internal sealed class DecisionDriver : IAsyncDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);
    private const string Instructions =
        "Choose exactly one supplied legal movement action. Your goal is to defeat bugs and the boss, not just survive. " +
        "Your ship fires automatically RIGHT at its current Y. Y increases DOWN. " +
        "When a target is below the ship, choose down; when above, choose up. " +
        "Hold when roughly aligned, not when stranded at an edge far from the target's firing lane. " +
        "The boss patrols the full height and fires aimed magenta sparks. Dodge approaching hostileShots, then realign. " +
        "Collect nearby power-ups only when safe. The game controls physics, firing, collisions, score, and bounds.";

    private readonly ChooseDecisionAsync? _chooseDecision;
    private readonly IDisposable? _clientLifetime;
    private Task<DecisionAttempt>? _pending;
    private CancellationTokenSource? _requestCancellation;
    private IReadOnlyDictionary<string, string?> _requestOptions =
        new Dictionary<string, string?>();
    private int _generation;
    private int _requestGeneration;
    private string _requestObservation = "";
    private int _decisionNumber;
    private DecisionLogEntry[] _history = [];

    public DecisionDriver(IDecisionClient? client)
    {
        if (client is not null)
        {
            _chooseDecision = async (snapshot, instructions, options, cancellationToken) =>
            {
                var decision = await client.ChooseAsync(
                    snapshot,
                    instructions,
                    options,
                    cancellationToken);
                return new ModelDecision(decision.Choice, decision.Probabilities);
            };
            _clientLifetime = client as IDisposable;
        }
    }

    internal DecisionDriver(ChooseDecisionAsync chooseDecision)
    {
        _chooseDecision = chooseDecision;
    }

    public bool HasPendingRequest => _pending is not null;
    public bool Failed { get; private set; }
    public string Status { get; private set; } = "LOCAL DECISIONS READY";
    public string LastAction { get; private set; } = "NONE";
    public string LastProbabilitySummary { get; private set; } = "NO DECISION YET";
    public string? LastError { get; private set; }
    // Only the game thread publishes history. Readers receive an immutable record-array snapshot.
    public IReadOnlyList<DecisionLogEntry> History => Array.AsReadOnly(Volatile.Read(ref _history));

    public void StartRequest(FlightWorld world)
    {
        if (_chooseDecision is null)
        {
            Fail("No local Decisions client is configured.");
            return;
        }

        if (_pending is not null || Failed || world.Outcome is not null)
        {
            return;
        }

        // Step 1: Freeze an immutable observation and offer only actions legal at these bounds.
        _requestOptions = world.AllowedOptions();
        var snapshot = world.Snapshot();
        _requestObservation = DescribeObservation(world);
        _requestGeneration = _generation;
        _requestCancellation = new CancellationTokenSource(RequestTimeout);
        var cancellationToken = _requestCancellation.Token;
        Status = "THINKING. WORLD PAUSED.";
        _pending = RequestAsync(_chooseDecision, snapshot, _requestOptions, cancellationToken);
    }

    public bool Poll(FlightWorld world)
    {
        if (_pending is null)
        {
            return false;
        }

        if (!_pending.IsCompleted)
        {
            if (_requestCancellation?.IsCancellationRequested == true &&
                _requestGeneration == _generation)
            {
                Fail("The local decision timed out. Waiting for cancellation.");
            }

            return false;
        }

        DecisionAttempt attempt;
        try
        {
            attempt = _pending.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            attempt = new DecisionAttempt(
                null,
                $"Unexpected decision failure ({exception.GetType().Name}).",
                0);
        }
        finally
        {
            _pending = null;
            _requestCancellation?.Dispose();
            _requestCancellation = null;
        }

        // Step 2: Discard any response made stale by pause, stop, restart, or mode change.
        if (_requestGeneration != _generation)
        {
            Status = "STALE DECISION DISCARDED.";
            return false;
        }

        if (attempt.Error is not null || attempt.Decision is null)
        {
            Fail(attempt.Error ?? "The local decision returned no result.");
            return false;
        }

        // Step 3: Verify the choice and complete probability distribution before applying it.
        if (!IsValidDecision(attempt.Decision, _requestOptions, world))
        {
            Fail("The local decision returned an illegal or invalid result.");
            return false;
        }

        if (!world.TryApplyDecision(attempt.Decision.Choice))
        {
            Fail("The legal action changed before the decision could be applied.");
            return false;
        }

        LastAction = attempt.Decision.Choice.ToUpperInvariant();
        LastProbabilitySummary = string.Join(
            "  ",
            attempt.Decision.Probabilities
                .OrderByDescending(probability => probability.Value)
                .Select(probability => $"{probability.Key.ToUpperInvariant()} {probability.Value:P0}"));
        Status = $"APPLIED {LastAction}. {attempt.LatencyMilliseconds:F0} MS";
        var entry = new DecisionLogEntry(
            ++_decisionNumber,
            _requestObservation,
            LastAction,
            attempt.Decision.Probabilities[attempt.Decision.Choice],
            attempt.LatencyMilliseconds);
        Volatile.Write(ref _history, _history.Append(entry).TakeLast(5).ToArray());
        return true;
    }

    public void ClearHistory()
    {
        _decisionNumber = 0;
        Volatile.Write(ref _history, []);
    }

    private static string DescribeObservation(FlightWorld world)
    {
        var nearest = world.Enemies
            .Where(enemy => enemy.IsVisible)
            .OrderBy(enemy => Math.Abs(enemy.X - world.PlayerPosition.X) + Math.Abs(enemy.Y - world.PlayerPosition.Y))
            .FirstOrDefault() ?? world.Boss;
        var input = $"W{world.Wave} SHIP {(int)world.PlayerPosition.X},{(int)world.PlayerPosition.Y}";
        return nearest is null
            ? $"{input} NO VISIBLE BUG"
            : $"{input} {nearest.Kind.ToString().ToUpperInvariant()} {(int)nearest.X},{(int)nearest.Y}";
    }

    public static bool IsValidDecision(
        ModelDecision decision,
        IReadOnlyDictionary<string, string?> allowedOptions,
        FlightWorld world)
    {
        var probabilities = decision.Probabilities;
        if (!allowedOptions.ContainsKey(decision.Choice) ||
            probabilities.Count != allowedOptions.Count ||
            allowedOptions.Keys.Any(option => !probabilities.ContainsKey(option)) ||
            probabilities.Values.Any(value => !double.IsFinite(value) || value is < 0 or > 1) ||
            Math.Abs(probabilities.Values.Sum() - 1) > 0.01)
        {
            return false;
        }

        return world.AllowedOptions().ContainsKey(decision.Choice);
    }

    public void Cancel()
    {
        _generation++;
        _requestCancellation?.Cancel();
        if (_pending is not null)
        {
            Status = "CANCELLING LOCAL DECISION.";
        }
    }

    public void ResetFailureForExplicitRetry()
    {
        Failed = false;
        LastError = null;
        LastAction = "NONE";
        LastProbabilitySummary = "NO DECISION YET";
        Status = "LOCAL DECISIONS READY";
    }

    public async ValueTask DisposeAsync()
    {
        Cancel();
        if (_pending is not null)
        {
            await _pending;
            _pending = null;
        }

        _requestCancellation?.Dispose();
        _requestCancellation = null;
        _clientLifetime?.Dispose();
    }

    private static async Task<DecisionAttempt> RequestAsync(
        ChooseDecisionAsync chooseDecision,
        string snapshot,
        IReadOnlyDictionary<string, string?> options,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var decision = await chooseDecision(
                snapshot,
                Instructions,
                options,
                cancellationToken);
            return new DecisionAttempt(decision, null, stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            return new DecisionAttempt(null, "The local decision was cancelled or timed out.", stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (DecisionException exception)
        {
            return new DecisionAttempt(
                null,
                $"Ollama Decisions failed ({exception.GetType().Name}). Check the local runtime and model.",
                stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (HttpRequestException exception)
        {
            return new DecisionAttempt(
                null,
                $"Ollama request failed ({exception.GetType().Name}). Check the local service.",
                stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception exception)
        {
            return new DecisionAttempt(
                null,
                $"Unexpected local Decisions error ({exception.GetType().Name}).",
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private void Fail(string error)
    {
        Failed = true;
        LastError = error;
        Status = $"AI STOPPED: {error}";
    }

    private sealed record DecisionAttempt(
        ModelDecision? Decision,
        string? Error,
        double LatencyMilliseconds);
}
