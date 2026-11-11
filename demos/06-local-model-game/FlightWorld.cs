// Purpose: Own movement, bug behavior, combat, pickups, and mission progress.
// Demo role: Keep every game rule in C# while a local model chooses only bounded movement.
// Requirements: A fixed-step caller and keyboard or validated model movement.
// Expected result: A four-wave flight with animated bugs, power-ups, and a Segfault boss finale.
using System.Text.Json;
using Microsoft.Xna.Framework;

namespace SmallModels.SpaceGame;

public enum FlightAction
{
    Hold,
    Up,
    Down,
    Left,
    Right,
}

public enum PlayerPose
{
    Idle,
    Up,
    Down,
    Thrust,
}

public enum BugKind
{
    Null,
    Race,
    Leak,
    OffByOne,
    Segfault,
}

public enum PowerUpKind
{
    Speed,
    Shield,
    Score,
    Repair,
}

public enum ProjectileKind
{
    Laser,
    Plasma,
    Orb,
    Braces,
    Spark,
}

public readonly record struct FlightInput(float Horizontal, float Vertical);

public sealed class EnemyState(BugKind kind, float x, float y, float health = 1)
{
    public BugKind Kind { get; } = kind;
    public float X { get; set; } = x;
    public float Y { get; set; } = y;
    public Vector2 PreviousPosition { get; set; } = new(x, y);
    public float Health { get; set; } = health;
    public float MaximumHealth { get; } = health;
    public float AnimationSeconds { get; set; }
    public float PatternSeconds { get; set; }
    public float Scale { get; set; } = kind == BugKind.Segfault ? 1.45f : 0.72f;
    public int Lane { get; set; }
    public bool IsVisible => Kind != BugKind.Null || (int)(AnimationSeconds / 0.42f) % 4 != 0;
    public int Frame => (int)(AnimationSeconds / 0.18f) % 2;
}

public sealed class PowerUpState(PowerUpKind kind, float x, float y)
{
    public PowerUpKind Kind { get; } = kind;
    public float X { get; set; } = x;
    public float Y { get; set; } = y;
    public float AnimationSeconds { get; set; }
}

public sealed class ProjectileState(
    ProjectileKind kind, float x, float y, Vector2 velocity, float damage, bool isHostile = false)
{
    public ProjectileKind Kind { get; } = kind;
    public float X { get; set; } = x;
    public float Y { get; set; } = y;
    public Vector2 Velocity { get; } = velocity;
    public float Damage { get; } = damage;
    public bool IsHostile { get; } = isHostile;
}

public sealed class ExplosionState(float x, float y)
{
    public float X { get; } = x;
    public float Y { get; } = y;
    public float AgeSeconds { get; set; }
    public int Frame => Math.Min(3, (int)(AgeSeconds / 0.11f));
}

public sealed class FlightWorld(SpriteGeometry sprites)
{
    public const float ScreenWidth = 880;
    public const float ScreenHeight = 720;
    public const float PlayerMargin = 42;
    public const float PlayerMaximumX = ScreenWidth / 2;
    public const int MaximumWaves = 4;

    private static readonly BugKind[] BugOrder =
    [
        BugKind.Null,
        BugKind.Race,
        BugKind.Leak,
        BugKind.OffByOne,
    ];

    private static readonly PowerUpKind[] PowerUpOrder =
    [
        PowerUpKind.Speed,
        PowerUpKind.Shield,
        PowerUpKind.Score,
        PowerUpKind.Repair,
    ];

    private static readonly ProjectileKind[] ShotOrder =
    [
        ProjectileKind.Laser,
        ProjectileKind.Plasma,
        ProjectileKind.Orb,
        ProjectileKind.Braces,
        ProjectileKind.Spark,
    ];

    private const int BugsPerWave = 6;
    private const float ForwardSpeed = 135;
    private const float PlayerSpeed = 250;
    private const float BugSpawnSeconds = 1.2f;
    private const float BossShotSeconds = 2.4f;
    private readonly List<EnemyState> _enemies = [];
    private readonly List<PowerUpState> _powerUps = [];
    private readonly List<ProjectileState> _projectiles = [];
    private readonly List<ExplosionState> _explosions = [];
    private float _nextBugIn = 0.8f;
    private float _shotTimer;
    private float _bossShotTimer = BossShotSeconds;
    private float _invulnerabilitySeconds;
    private float _speedSeconds;
    private float _shieldSeconds;
    private float _scoreBoostSeconds;
    private float _actionSeconds;
    private float _helperY = ScreenHeight / 2;
    private FlightAction _activeAction;
    private int _bugsSpawned;
    private int _dropCount;
    private int _shotsFired;
    private int _baseScore;

    public Vector2 PlayerPosition { get; private set; } = new(160, ScreenHeight / 2);
    public IReadOnlyList<EnemyState> Enemies => _enemies;
    public IReadOnlyList<PowerUpState> PowerUps => _powerUps;
    public IReadOnlyList<ProjectileState> Projectiles => _projectiles;
    public IReadOnlyList<ExplosionState> Explosions => _explosions;
    public EnemyState? Boss { get; private set; }
    public PlayerPose PlayerPose { get; private set; } = PlayerPose.Idle;
    public float Distance { get; private set; }
    public float HitFlashSeconds { get; private set; }
    public float ShieldSeconds => _shieldSeconds;
    public float SpeedSeconds => _speedSeconds;
    public float ScoreBoostSeconds => _scoreBoostSeconds;
    public int Lives { get; private set; } = 3;
    public int Wave { get; private set; } = 1;
    public int Score => _baseScore;
    public int BossHealth => Boss is null ? 0 : (int)Math.Ceiling(Boss.Health);
    public int BossMaximumHealth => Boss is null ? 0 : (int)Boss.MaximumHealth;
    public bool HasHelper => Wave >= 2;
    public float HelperY => _helperY;
    public int HelperFrame => (int)(Distance / 80) % 2;
    public string? Outcome { get; private set; }

    public IReadOnlyDictionary<string, string?> AllowedOptions()
    {
        var options = new Dictionary<string, string?>
        {
            ["hold"] = "Keep this firing lane when aligned with a target and no hostile shot is approaching.",
        };

        if (PlayerPosition.Y > PlayerMargin)
        {
            options["up"] = "Decrease ship Y to line up with a target above, or dodge an approaching threat.";
        }

        if (PlayerPosition.Y < ScreenHeight - PlayerMargin)
        {
            options["down"] = "Increase ship Y to line up with a target below, or dodge an approaching threat.";
        }

        if (PlayerPosition.X > PlayerMargin)
        {
            options["left"] = "Move left while staying inside the flight area.";
        }

        if (PlayerPosition.X < PlayerMaximumX)
        {
            options["right"] = "Move right within the left half of the flight area, staying behind the targets.";
        }

        return options;
    }

    public bool TryApplyDecision(string label)
    {
        if (Outcome is not null || !AllowedOptions().ContainsKey(label))
        {
            return false;
        }

        _activeAction = label switch
        {
            "up" => FlightAction.Up,
            "down" => FlightAction.Down,
            "left" => FlightAction.Left,
            "right" => FlightAction.Right,
            _ => FlightAction.Hold,
        };
        _actionSeconds = 0.55f;
        return true;
    }

    public void Step(float seconds, FlightInput manualInput)
    {
        if (Outcome is not null)
        {
            return;
        }

        var step = Math.Clamp(seconds, 0, 1f / 30f);
        Distance += ForwardSpeed * step;
        HitFlashSeconds = Math.Max(0, HitFlashSeconds - step);
        _invulnerabilitySeconds = Math.Max(0, _invulnerabilitySeconds - step);
        _speedSeconds = Math.Max(0, _speedSeconds - step);
        _shieldSeconds = Math.Max(0, _shieldSeconds - step);
        _scoreBoostSeconds = Math.Max(0, _scoreBoostSeconds - step);

        // Step 1: Apply keyboard movement or the last short, bounded model action.
        var previousPlayerPosition = PlayerPosition;
        var direction = new Vector2(
            Math.Clamp(manualInput.Horizontal, -1, 1),
            Math.Clamp(manualInput.Vertical, -1, 1));
        if (direction.LengthSquared() > 1)
        {
            direction.Normalize();
        }

        if (direction == Vector2.Zero && _actionSeconds > 0)
        {
            direction = _activeAction switch
            {
                FlightAction.Up => new Vector2(0, -1),
                FlightAction.Down => new Vector2(0, 1),
                FlightAction.Left => new Vector2(-1, 0),
                FlightAction.Right => new Vector2(1, 0),
                _ => Vector2.Zero,
            };
            _actionSeconds = Math.Max(0, _actionSeconds - step);
        }

        var speed = PlayerSpeed * (_speedSeconds > 0 ? 1.35f : 1);
        PlayerPosition = Vector2.Clamp(
            PlayerPosition + direction * speed * step,
            new Vector2(PlayerMargin),
            new Vector2(PlayerMaximumX, ScreenHeight - PlayerMargin));
        PlayerPose = direction.Y < -0.1f
            ? PlayerPose.Up
            : direction.Y > 0.1f
                ? PlayerPose.Down
                : direction.X != 0
                    ? PlayerPose.Thrust
                    : PlayerPose.Idle;

        // Step 2: Spawn this wave's bugs, then bring in its boss as the finale.
        _nextBugIn -= step;
        if (Boss is null && _bugsSpawned < BugsPerWave && _nextBugIn <= 0)
        {
            SpawnBug();
            _nextBugIn = BugSpawnSeconds;
        }

        if (Boss is null && _bugsSpawned == BugsPerWave && _enemies.Count == 0)
        {
            Boss = new EnemyState(BugKind.Segfault, ScreenWidth + 180, ScreenHeight / 2, 18);
            _bossShotTimer = BossShotSeconds;
        }

        // Step 3: Animate distinct bug patterns and resolve ship collisions in C#.
        for (var index = _enemies.Count - 1; index >= 0; index--)
        {
            var enemy = _enemies[index];
            enemy.PreviousPosition = new Vector2(enemy.X, enemy.Y);
            UpdateBug(enemy, step);
            if (enemy.IsVisible && sprites.FirstContact(
                SpriteGeometry.Player(PlayerPose), previousPlayerPosition, PlayerPosition,
                SpriteGeometry.Enemy(enemy), enemy.PreviousPosition, new Vector2(enemy.X, enemy.Y)) is not null)
            {
                DamagePlayer();
                _explosions.Add(new ExplosionState(enemy.X, enemy.Y));
                _enemies.RemoveAt(index);
                continue;
            }

            if (enemy.X < -100)
            {
                _enemies.RemoveAt(index);
            }
        }

        if (Boss is not null)
        {
            Boss.PreviousPosition = new Vector2(Boss.X, Boss.Y);
            UpdateBoss(Boss, step);
            if (sprites.FirstContact(
                SpriteGeometry.Player(PlayerPose), previousPlayerPosition, PlayerPosition,
                SpriteGeometry.Enemy(Boss), Boss.PreviousPosition, new Vector2(Boss.X, Boss.Y)) is not null)
            {
                DamagePlayer();
            }
        }

        // Step 4: Cycle through the .NET-themed shots and let C# own every hit.
        _shotTimer -= step;
        if (_shotTimer <= 0)
        {
            FireShot();
            _shotTimer = 0.32f;
        }

        UpdateProjectiles(step, previousPlayerPosition);
        UpdatePowerUps(step);
        UpdateExplosions(step);

        if (Lives <= 0)
        {
            Outcome = "HULL LOST";
        }
    }

    public string Snapshot()
    {
        var observation = new
        {
            wave = Wave,
            player = new { x = (int)PlayerPosition.X, y = (int)PlayerPosition.Y },
            lives = Lives,
            score = Score,
            combat = new
            {
                objective = "Destroy the boss to advance. Shots fire automatically to the right at the ship's Y.",
                coordinates = "Y increases downwards. Up decreases Y; down increases Y.",
                playerBounds = new { minX = PlayerMargin, maxX = PlayerMaximumX,
                    minY = PlayerMargin, maxY = ScreenHeight - PlayerMargin },
                actionSeconds = 0.55f,
                movementPerAction = PlayerSpeed * 0.55f * (_speedSeconds > 0 ? 1.35f : 1),
                shotSpeed = 520,
            },
            bugs = _enemies.Select(enemy => new
            {
                type = enemy.Kind.ToString().ToLowerInvariant(),
                x = (int)enemy.X,
                y = (int)enemy.Y,
                visible = enemy.IsVisible,
            }),
            boss = Boss is null ? null : new { x = (int)Boss.X, y = (int)Boss.Y, health = BossHealth },
            hostileShots = _projectiles.Where(shot => shot.IsHostile).Select(shot => new
            {
                x = (int)shot.X, y = (int)shot.Y,
                velocityX = (int)shot.Velocity.X, velocityY = (int)shot.Velocity.Y,
            }),
            powerUps = _powerUps.Select(powerUp => new
            {
                type = powerUp.Kind.ToString().ToLowerInvariant(),
                x = (int)powerUp.X,
                y = (int)powerUp.Y,
            }),
        };

        return JsonSerializer.Serialize(observation);
    }

    private void SpawnBug()
    {
        var kind = BugOrder[(_bugsSpawned + Wave - 1) % BugOrder.Length];
        var lane = _bugsSpawned % 6;
        var y = 135 + lane * 90;
        var bug = new EnemyState(kind, ScreenWidth + 55, y)
        {
            Lane = lane,
        };
        _enemies.Add(bug);
        _bugsSpawned++;
    }

    private static void UpdateBug(EnemyState bug, float step)
    {
        bug.AnimationSeconds += step;
        bug.PatternSeconds += step;

        switch (bug.Kind)
        {
            case BugKind.Null:
                bug.X -= 115 * step;
                break;
            case BugKind.Race:
                bug.X -= 175 * step;
                if (bug.PatternSeconds >= 0.55f)
                {
                    bug.PatternSeconds = 0;
                    bug.Lane = bug.Lane < 3 ? bug.Lane + 3 : bug.Lane - 3;
                }

                bug.Y = MathHelper.Lerp(bug.Y, 135 + bug.Lane * 90, Math.Min(1, step * 9));
                break;
            case BugKind.Leak:
                bug.Scale = Math.Min(1.35f, bug.Scale + step * 0.1f);
                bug.X -= Math.Max(28, 125 - bug.Scale * 70) * step;
                break;
            case BugKind.OffByOne:
                bug.X -= 130 * step;
                if (bug.PatternSeconds >= 0.9f)
                {
                    bug.PatternSeconds = 0;
                    bug.Lane = (bug.Lane + 1) % 6;
                    bug.Y = 135 + bug.Lane * 90;
                    // This bug teleports by a lane, rather than colliding along an unseen path.
                    bug.PreviousPosition = new Vector2(bug.PreviousPosition.X, bug.Y);
                }

                break;
        }
    }

    private void UpdateBoss(EnemyState boss, float step)
    {
        boss.AnimationSeconds += step;
        boss.PatternSeconds += step;
        boss.X = MathHelper.Lerp(boss.X, ScreenWidth - 105, Math.Min(1, step * 2));
        // Patrol every legal firing lane, including both edges. A stationary ship is not a safe exploit.
        boss.Y = ScreenHeight / 2 +
            MathF.Sin(boss.PatternSeconds * 1.7f) * (ScreenHeight / 2 - PlayerMargin);
        _bossShotTimer -= step;
        if (_bossShotTimer <= 0)
        {
            var origin = new Vector2(boss.X - 65, boss.Y);
            var velocity = Vector2.Normalize(PlayerPosition - origin) * 220;
            _projectiles.Add(new ProjectileState(ProjectileKind.Spark, origin.X, origin.Y, velocity, 1, true));
            _bossShotTimer = BossShotSeconds;
        }
    }

    private void FireShot()
    {
        var kind = ShotOrder[_shotsFired % ShotOrder.Length];
        var damage = kind == ProjectileKind.Braces ? 1.5f : 1;
        _projectiles.Add(new ProjectileState(kind, PlayerPosition.X + 55, PlayerPosition.Y, new Vector2(520, 0), damage));
        _shotsFired++;

        if (HasHelper)
        {
            _helperY = MathHelper.Lerp(_helperY, PlayerPosition.Y, 0.08f);
            _projectiles.Add(new ProjectileState(ProjectileKind.Orb, PlayerPosition.X + 18, _helperY, new Vector2(460, 0), 0.5f));
        }
    }

    private void UpdateProjectiles(float step, Vector2 previousPlayerPosition)
    {
        for (var index = _projectiles.Count - 1; index >= 0; index--)
        {
            var shot = _projectiles[index];
            var start = new Vector2(shot.X, shot.Y);
            shot.X += shot.Velocity.X * step;
            shot.Y += shot.Velocity.Y * step;
            var end = new Vector2(shot.X, shot.Y);
            if (shot.IsHostile)
            {
                if (sprites.FirstContact(
                    SpriteGeometry.Shot(shot.Kind), start, end,
                    SpriteGeometry.Player(PlayerPose), previousPlayerPosition, PlayerPosition) is not null)
                {
                    DamagePlayer();
                    _projectiles.RemoveAt(index);
                }
                else if (IsOutsideFlightArea(shot))
                {
                    _projectiles.RemoveAt(index);
                }

                continue;
            }

            EnemyState? hitEnemy = null;
            var firstContact = float.PositiveInfinity;
            foreach (var enemy in _enemies.Concat(Boss is null ? [] : new[] { Boss }))
            {
                if (!enemy.IsVisible)
                {
                    continue;
                }

                var contact = sprites.FirstContact(
                    SpriteGeometry.Shot(shot.Kind), start, end,
                    SpriteGeometry.Enemy(enemy), enemy.PreviousPosition, new Vector2(enemy.X, enemy.Y));
                if (contact is not null && contact.Value < firstContact)
                {
                    firstContact = contact.Value;
                    hitEnemy = enemy;
                }
            }

            if (hitEnemy is not null && hitEnemy == Boss)
            {
                Boss.Health -= shot.Damage;
                _projectiles.RemoveAt(index);
                if (Boss.Health <= 0)
                {
                    _explosions.Add(new ExplosionState(Boss.X, Boss.Y));
                    _baseScore += 1000 * (_scoreBoostSeconds > 0 ? 2 : 1);
                    Boss = null;
                    Wave++;
                    _bugsSpawned = 0;
                    _nextBugIn = 1.5f;
                    if (Wave > MaximumWaves)
                    {
                        Outcome = "SEGFAULT DEFEATED";
                    }
                }

                continue;
            }

            if (hitEnemy is not null)
            {
                var enemy = hitEnemy;
                enemy.Health -= shot.Damage;
                _projectiles.RemoveAt(index);
                if (enemy.Health <= 0)
                {
                    _baseScore += 100 * (_scoreBoostSeconds > 0 ? 2 : 1);
                    _explosions.Add(new ExplosionState(enemy.X, enemy.Y));
                    DropPowerUp(enemy);
                    _enemies.Remove(enemy);
                }

                continue;
            }

            if (IsOutsideFlightArea(shot))
            {
                _projectiles.RemoveAt(index);
            }
        }
    }

    private static bool IsOutsideFlightArea(ProjectileState shot) =>
        shot.X < -80 || shot.X > ScreenWidth + 80 || shot.Y < -80 || shot.Y > ScreenHeight + 80;

    private void DropPowerUp(EnemyState bug)
    {
        _dropCount++;
        if (_dropCount % 2 == 0)
        {
            var kind = PowerUpOrder[(_dropCount / 2 - 1) % PowerUpOrder.Length];
            _powerUps.Add(new PowerUpState(kind, bug.X, bug.Y));
        }
    }

    private void UpdatePowerUps(float step)
    {
        for (var index = _powerUps.Count - 1; index >= 0; index--)
        {
            var powerUp = _powerUps[index];
            powerUp.X -= 88 * step;
            powerUp.AnimationSeconds += step;

            if (sprites.Intersects(SpriteGeometry.Player(PlayerPose), PlayerPosition,
                SpriteGeometry.Pickup(powerUp.Kind), new Vector2(powerUp.X, powerUp.Y)))
            {
                ApplyPowerUp(powerUp.Kind);
                _powerUps.RemoveAt(index);
            }
            else if (powerUp.X < -50)
            {
                _powerUps.RemoveAt(index);
            }
        }
    }

    private void ApplyPowerUp(PowerUpKind kind)
    {
        switch (kind)
        {
            case PowerUpKind.Speed:
                _speedSeconds = 7;
                break;
            case PowerUpKind.Shield:
                _shieldSeconds = 7;
                break;
            case PowerUpKind.Score:
                _scoreBoostSeconds = 8;
                break;
            case PowerUpKind.Repair:
                Lives = Math.Min(3, Lives + 1);
                break;
        }
    }

    private void DamagePlayer()
    {
        if (_invulnerabilitySeconds > 0)
        {
            return;
        }

        if (_shieldSeconds > 0)
        {
            _shieldSeconds = 0;
        }
        else
        {
            Lives--;
        }

        HitFlashSeconds = 1;
        _invulnerabilitySeconds = 1;
    }

    private void UpdateExplosions(float step)
    {
        for (var index = _explosions.Count - 1; index >= 0; index--)
        {
            _explosions[index].AgeSeconds += step;
            if (_explosions[index].AgeSeconds >= 0.45f)
            {
                _explosions.RemoveAt(index);
            }
        }
    }
}
