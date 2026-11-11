// Purpose: Load the character art and render the side-scroller, HUD, and voice status.
// Demo role: Show the Hear, Decide, and Play stages while C# owns every visible game effect.
// Requirements: MonoGame DesktopGL, copied sprite PNGs, and an initialized game session.
// Expected result: An 880-by-720 playable flight beside a live local-model and voice panel.
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace SmallModels.SpaceGame;

internal sealed class SpaceGameWindow : Microsoft.Xna.Framework.Game
{
    private static readonly Color Navy = new(34, 32, 58);
    private static readonly Color Purple = new(81, 43, 212);
    private static readonly Color Magenta = new(212, 0, 240);
    private static readonly Color Pink = new(255, 138, 232);
    private static readonly Color Blue = new(46, 140, 230);
    private static readonly Color Lavender = new(155, 143, 232);
    private static readonly string[] SpriteNames =
    [
        "bg-asteroids",
        "bg-moon-magenta",
        "bg-planet-blue",
        "bg-planet-purple",
        "boss-segfault",
        "bug-leak-1",
        "bug-leak-2",
        "bug-null-1",
        "bug-null-2",
        "bug-offbyone-1",
        "bug-offbyone-2",
        "bug-race-1",
        "bug-race-2",
        "explosion-1",
        "explosion-2",
        "explosion-3",
        "explosion-4",
        "helper-1",
        "helper-2",
        "player-down",
        "player-idle",
        "player-thrust",
        "player-up",
        "powerup-repair",
        "powerup-score",
        "powerup-shield",
        "powerup-speed",
        "shot-braces",
        "shot-laser",
        "shot-orb",
        "shot-plasma",
        "shot-spark",
    ];

    private readonly GraphicsDeviceManager _graphics;
    private readonly GameSession _session;
    private readonly VoiceCommandService _voiceCommands;
    private readonly Dictionary<string, Texture2D> _sprites = [];
    private SpriteBatch _spriteBatch = null!;
    private Texture2D _pixel = null!;
    private KeyboardState _previousKeyboard;
    private string _lastVoiceResult = "NO VOICE COMMAND SUBMITTED";

    public SpaceGameWindow(GameSession session, VoiceCommandService voiceCommands)
    {
        _session = session;
        _voiceCommands = voiceCommands;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
        };
        Window.Title = "Small Models, Big .NET Energy";
        Window.AllowUserResizing = false;
        IsMouseVisible = true;
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);

        // Step 1: Load the checked-in transparent PNGs from the copied game assets.
        foreach (var name in SpriteNames)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "assets", "sprites", $"{name}.png");
            using var stream = File.OpenRead(path);
            var texture = Texture2D.FromStream(GraphicsDevice, stream);
            _sprites.Add(name, texture);
            var pixels = new Color[texture.Width * texture.Height];
            texture.GetData(pixels);
            _session.Sprites.Register(name, texture.Width, texture.Height, pixels);
        }
    }

    protected override void Update(GameTime gameTime)
    {
        var elapsedSeconds = (float)Math.Clamp(gameTime.ElapsedGameTime.TotalSeconds, 0, 0.1);
        _voiceCommands.Update(elapsedSeconds);

        while (_voiceCommands.TryDequeueTranscript(out var transcript))
        {
            if (VoiceCommandParser.TryParse(transcript, out var command))
            {
                _session.ApplyVoiceCommand(command);
                _lastVoiceResult = $"ACCEPTED: {command.ToString().ToUpperInvariant()}";
            }
            else
            {
                _lastVoiceResult = "REJECTED. UTTERANCE HIDDEN.";
            }
        }

        var keyboard = Keyboard.GetState();
        bool Pressed(Keys key) => keyboard.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key);

        if (Pressed(Keys.Q))
        {
            _session.Stop();
            Exit();
        }
        else if (Pressed(Keys.Enter))
        {
            _session.ApplyVoiceCommand(VoiceCommand.Start);
        }
        else if (Pressed(Keys.P))
        {
            _session.ApplyVoiceCommand(VoiceCommand.Pause);
        }
        else if (Pressed(Keys.Escape))
        {
            _session.ApplyVoiceCommand(VoiceCommand.Stop);
        }
        else if (Pressed(Keys.Tab))
        {
            _session.ToggleControlMode();
        }
        else if (Pressed(Keys.V))
        {
            _voiceCommands.ToggleListening();
        }

        var manualInput = new FlightInput(
            (keyboard.IsKeyDown(Keys.Right) || keyboard.IsKeyDown(Keys.D) ? 1 : 0) -
                (keyboard.IsKeyDown(Keys.Left) || keyboard.IsKeyDown(Keys.A) ? 1 : 0),
            (keyboard.IsKeyDown(Keys.Down) || keyboard.IsKeyDown(Keys.S) ? 1 : 0) -
                (keyboard.IsKeyDown(Keys.Up) || keyboard.IsKeyDown(Keys.W) ? 1 : 0));
        _session.Update(elapsedSeconds, manualInput);
        _previousKeyboard = keyboard;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Navy);
        _spriteBatch.Begin(blendState: BlendState.AlphaBlend, samplerState: SamplerState.PointClamp);
        DrawBackground();
        if (_session.State == MissionState.Home)
        {
            DrawWelcome();
        }
        else
        {
            DrawMission();
        }

        DrawSidePanel();
        _spriteBatch.End();
        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        foreach (var sprite in _sprites.Values)
        {
            sprite.Dispose();
        }

        _sprites.Clear();
        _pixel?.Dispose();
        _spriteBatch?.Dispose();
        base.UnloadContent();
    }

    private void DrawBackground()
    {
        var distance = _session.World.Distance;

        // Step 2: Let stars, rocks, and planets drift at different speeds for parallax.
        for (var index = 0; index < 70; index++)
        {
            var speed = 0.12f + index % 5 * 0.055f;
            var x = PositiveModulo(index * 151 - distance * speed, FlightWorld.ScreenWidth);
            var y = index * 97 % (int)FlightWorld.ScreenHeight;
            var size = index % 6 == 0 ? 3 : 2;
            var color = index % 3 == 0 ? Pink : new Color(210, 218, 255, 190);
            DrawRectangle(new Rectangle((int)x, y, size, size), color);
        }

        DrawParallaxSprite("bg-asteroids", 760, 195, 0.52f, 0.08f);
        DrawParallaxSprite("bg-planet-purple", 670, 145, 0.48f, 0.035f);
        DrawParallaxSprite("bg-moon-magenta", 540, 515, 0.42f, 0.055f);
        DrawParallaxSprite("bg-planet-blue", 770, 575, 0.38f, 0.025f);
        DrawRectangle(new Rectangle(0, 0, (int)FlightWorld.ScreenWidth, 3), new Color(Purple, 150));
        DrawRectangle(new Rectangle(0, 717, (int)FlightWorld.ScreenWidth, 3), new Color(Purple, 150));
    }

    private void DrawWelcome()
    {
        DrawSprite("player-idle", new Vector2(410, 395), 0.52f);
        DrawText("SMALL MODELS.", 42, 52, 4, Color.White);
        DrawText("BIG .NET ENERGY.", 42, 100, 4, Pink);
        DrawText("BUGS IN ORBIT. CLEAN CODE WINS.", 48, 177, 2, Lavender);
        DrawText("A LOCAL MODEL CHOOSES MOVEMENT.", 48, 214, 2, Color.White);
        DrawText("C# OWNS PHYSICS, SHOTS, AND COLLISIONS.", 48, 242, 2, Color.White);
        DrawText("ENTER: LAUNCH MISSION", 48, 562, 2, Pink);
        DrawText("ARROWS OR WASD: MOVE", 48, 596, 2, Color.White);
        DrawText("TAB: KEYBOARD MODE   V: VOICE", 48, 630, 2, Color.White);
    }

    private void DrawMission()
    {
        var world = _session.World;

        // Step 3: Draw effects first, then bugs, pickups, and the .NET Bots' ship.
        foreach (var explosion in world.Explosions)
        {
            DrawSprite($"explosion-{explosion.Frame + 1}", new Vector2(explosion.X, explosion.Y), 0.42f);
        }

        foreach (var powerUp in world.PowerUps)
        {
            DrawBody(SpriteGeometry.Pickup(powerUp.Kind), new Vector2(powerUp.X, powerUp.Y));
        }

        foreach (var shot in world.Projectiles)
        {
            DrawBody(SpriteGeometry.Shot(shot.Kind), new Vector2(shot.X, shot.Y),
                shot.IsHostile ? Pink : Color.White);
        }

        foreach (var enemy in world.Enemies)
        {
            if (enemy.IsVisible)
            {
                DrawBody(SpriteGeometry.Enemy(enemy), new Vector2(enemy.X, enemy.Y));
            }
        }

        if (world.Boss is not null)
        {
            DrawBody(SpriteGeometry.Enemy(world.Boss), new Vector2(world.Boss.X, world.Boss.Y));
            DrawBossBar();
        }

        if (world.HasHelper)
        {
            DrawSprite($"helper-{world.HelperFrame + 1}", world.PlayerPosition + new Vector2(-4, -62), 0.58f);
        }

        var playerTint = world.HitFlashSeconds > 0
            ? new Color(255, 100, 90)
            : Color.White;
        if (world.ShieldSeconds > 0)
        {
            DrawCircle(world.PlayerPosition, 48, new Color(46, 140, 230, 100), 3);
        }

        DrawBody(SpriteGeometry.Player(world.PlayerPose), world.PlayerPosition, playerTint);
        DrawHud(world);

        if (_session.State is MissionState.Paused or MissionState.Finished)
        {
            DrawRectangle(new Rectangle(125, 265, 630, 175), new Color(16, 13, 42, 235));
            DrawText(
                _session.State == MissionState.Paused
                    ? "PAUSED. ENTER TO RESUME."
                    : world.Outcome ?? "MISSION COMPLETE.",
                175,
                315,
                2,
                Color.White);
            if (_session.State == MissionState.Finished)
            {
                DrawText("ENTER: PLAY AGAIN", 270, 365, 2, Pink);
            }
        }
    }

    private void DrawHud(FlightWorld world)
    {
        DrawText($"SCORE {world.Score:000000}", 18, 18, 2, Color.White);
        DrawText($"LIVES {world.Lives}", 18, 49, 2, Pink);
        DrawText($"WAVE {world.Wave}/{FlightWorld.MaximumWaves}", 18, 80, 2, Lavender);
        DrawText($"CONTROL {(_session.ManualMode ? "KEYBOARD" : "LOCAL MODEL")}", 18, 111, 1, Color.White);
        if (world.Boss is not null)
        {
            DrawText("ALIGN TO FIRE. DODGE PINK SPARKS.", 18, 135, 1, Pink);
        }
    }

    private void DrawBossBar()
    {
        const int barX = 245;
        const int barWidth = 430;
        const int barY = 26;
        var ratio = worldBossRatio();
        DrawText("THE SEGFAULT", barX, 7, 1, Pink);
        DrawRectangle(new Rectangle(barX, barY, barWidth, 14), new Color(10, 9, 28, 230));
        DrawRectangle(new Rectangle(barX, barY, (int)(barWidth * ratio), 14), Magenta);

        float worldBossRatio() => _session.World.BossMaximumHealth == 0
            ? 0
            : Math.Clamp((float)_session.World.BossHealth / _session.World.BossMaximumHealth, 0, 1);
    }

    private void DrawSidePanel()
    {
        DrawRectangle(new Rectangle(900, 0, 380, 720), new Color(22, 20, 42, 246));
        DrawRectangle(new Rectangle(900, 0, 4, 720), Magenta);
        DrawText("LOCAL MISSION", 925, 22, 2, Pink);
        DrawText($"STATE: {_session.State}", 925, 64, 1, Color.White);
        DrawText($"CONTROL: {(_session.ManualMode ? "KEYBOARD" : "DECISIONS")}", 925, 88, 1, Color.White);
        DrawText("LAST 5 APPLIED OLLAMA DECISIONS", 925, 122, 1, Pink);
        DrawText("NIMBLE: LATEST / OLDEST FIRST", 925, 144, 1, Lavender);
        var history = _session.DecisionHistory;
        if (history.Count == 0)
        {
            DrawText("NO APPLIED DECISIONS YET.", 925, 176, 1, Color.White);
        }

        for (var index = 0; index < history.Count; index++)
        {
            var entry = history[index];
            var y = 176 + index * 54;
            DrawText($"#{entry.Number} {entry.Choice} {entry.Probability:P0} {entry.LatencyMilliseconds:F0} MS",
                925, y, 1, Color.White);
            DrawText(BitmapFont.WrapText(entry.Observation, 53), 925, y + 17, 1, Lavender);
        }

        DrawText(BitmapFont.WrapText(_session.Status, 53), 925, 455, 1, new Color(130, 245, 196));
        if (_session.DecisionError is not null)
        {
            DrawText("REQUEST FAILED. NOT AN APPLIED DECISION.", 925, 485, 1, new Color(255, 190, 120));
            DrawText(BitmapFont.WrapText(_session.DecisionError, 53), 925, 504, 1, new Color(255, 210, 175));
        }

        DrawText("VOICE: START / PAUSE / STOP", 925, 552, 1, Pink);
        DrawText(BitmapFont.WrapText(_voiceCommands.Status, 53), 925, 578, 1, Color.White);
        DrawText(BitmapFont.WrapText(_lastVoiceResult, 53), 925, 632, 1, new Color(130, 245, 196));
        DrawText("P: PAUSE   ESC: HOME   Q: QUIT", 925, 674, 1, Lavender);
    }

    private void DrawParallaxSprite(string name, float baseX, float baseY, float scale, float speed)
    {
        var texture = _sprites[name];
        var width = texture.Width * scale;
        var x = PositiveModulo(
            baseX - _session.World.Distance * speed + width / 2,
            FlightWorld.ScreenWidth + width) - width / 2;
        DrawSprite(name, new Vector2(x, baseY), scale, new Color(205, 198, 255, 165));
    }

    private void DrawCircle(Vector2 center, float radius, Color color, int thickness)
    {
        for (var angle = 0; angle < 360; angle += 8)
        {
            var radians = MathHelper.ToRadians(angle);
            var point = center + new Vector2(MathF.Cos(radians), MathF.Sin(radians)) * radius;
            DrawRectangle(new Rectangle((int)point.X, (int)point.Y, thickness, thickness), color);
        }
    }

    private void DrawSprite(string name, Vector2 center, float scale, Color? tint = null)
    {
        var texture = _sprites[name];
        _spriteBatch.Draw(
            texture,
            center,
            null,
            tint ?? Color.White,
            0,
            new Vector2(texture.Width / 2f, texture.Height / 2f),
            scale,
            SpriteEffects.None,
            0);
    }

    private void DrawRectangle(Rectangle rectangle, Color color) =>
        _spriteBatch.Draw(_pixel, rectangle, color);

    private void DrawText(string text, int x, int y, int scale, Color color) =>
        BitmapFont.DrawText(_spriteBatch, _pixel, text, x, y, scale, color);

    private void DrawBody(SpriteVisual visual, Vector2 center, Color? tint = null) =>
        DrawSprite(visual.Name, center, visual.Scale, tint);

    private static float PositiveModulo(float value, float modulus) =>
        (value % modulus + modulus) % modulus;
}
