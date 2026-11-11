// Purpose: Coordinate game state, model requests, and explicit user controls.
// Demo role: Apply START, PAUSE, and STOP consistently across keyboard and voice input.
// Requirements: A decision driver and fixed-step updates from the game window.
// Expected result: Responsive state changes with no stale model action applied.
using ElBruno.AI.Decisions;

namespace SmallModels.SpaceGame;

public enum MissionState
{
    Home,
    Running,
    Paused,
    Finished,
}

public sealed class GameSession : IAsyncDisposable
{
    private const float FixedStepSeconds = 1f / 60f;
    private const float DecisionDelaySeconds = 0.7f;
    private readonly DecisionDriver _decisions;
    private float _simulationAccumulator;
    private float _decisionDelay;

    public GameSession(IDecisionClient? decisionClient)
    {
        _decisions = new DecisionDriver(decisionClient);
        World = new FlightWorld(Sprites);
    }

    public SpriteGeometry Sprites { get; } = new();
    public FlightWorld World { get; private set; }
    public MissionState State { get; private set; } = MissionState.Home;
    public bool ManualMode { get; private set; }
    public string Status => ManualMode ? "MANUAL KEYBOARD MODE" : _decisions.Status;
    public string LastDecision => _decisions.LastAction;
    public string ProbabilitySummary => _decisions.LastProbabilitySummary;
    public string? DecisionError => _decisions.LastError;
    public IReadOnlyList<DecisionLogEntry> DecisionHistory => _decisions.History;

    public void StartOrResume()
    {
        if (State is MissionState.Home or MissionState.Finished)
        {
            World = new FlightWorld(Sprites);
            State = MissionState.Running;
            _decisions.ResetFailureForExplicitRetry();
            _decisions.ClearHistory();
            _decisionDelay = 0;
        }
        else if (State == MissionState.Paused)
        {
            State = MissionState.Running;
            _decisionDelay = 0;
        }
    }

    public void Pause()
    {
        if (State != MissionState.Running)
        {
            return;
        }

        State = MissionState.Paused;
        _simulationAccumulator = 0;
        _decisions.Cancel();
    }

    public void Stop()
    {
        _decisions.Cancel();
        World = new FlightWorld(Sprites);
        State = MissionState.Home;
        _simulationAccumulator = 0;
        _decisionDelay = 0;
    }

    public void ToggleControlMode()
    {
        ManualMode = !ManualMode;
        _decisions.Cancel();
        _simulationAccumulator = 0;
        _decisionDelay = 0;
        if (!ManualMode)
        {
            _decisions.ResetFailureForExplicitRetry();
        }
    }

    public void ApplyVoiceCommand(VoiceCommand command)
    {
        switch (command)
        {
            case VoiceCommand.Start:
                StartOrResume();
                break;
            case VoiceCommand.Pause:
                Pause();
                break;
            case VoiceCommand.Stop:
                Stop();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, "Unsupported voice command.");
        }
    }

    public void Update(float elapsedSeconds, FlightInput manualInput)
    {
        var elapsed = Math.Clamp(elapsedSeconds, 0, 0.1f);
        var actionApplied = _decisions.Poll(World);
        if (actionApplied)
        {
            _decisionDelay = DecisionDelaySeconds;
        }

        if (State != MissionState.Running)
        {
            return;
        }

        if (!ManualMode)
        {
            if (_decisions.HasPendingRequest || _decisions.Failed)
            {
                return;
            }

            if (_decisionDelay <= 0)
            {
                _decisions.StartRequest(World);
                return;
            }

            _decisionDelay = Math.Max(0, _decisionDelay - elapsed);
        }

        // Step 1: Advance deterministic physics only while the game is actively running.
        _simulationAccumulator += elapsed;
        while (_simulationAccumulator >= FixedStepSeconds && State == MissionState.Running)
        {
            World.Step(FixedStepSeconds, ManualMode ? manualInput : default);
            _simulationAccumulator -= FixedStepSeconds;

            if (World.Outcome is not null)
            {
                State = MissionState.Finished;
                _decisions.Cancel();
            }
        }
    }

    public ValueTask DisposeAsync() => _decisions.DisposeAsync();
}
