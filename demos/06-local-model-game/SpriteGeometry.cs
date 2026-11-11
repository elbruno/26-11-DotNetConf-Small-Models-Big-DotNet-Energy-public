// Purpose: Share sprite names, scales, and visible-pixel geometry between drawing and collisions.
// Demo role: C# tests the same centered PNG artwork that the player sees, not tiny center points.
// Requirements: Register each sprite's decoded pixels once during content loading.
// Expected result: Visible-body hits, transparent-space misses, and swept movement checks.
using Microsoft.Xna.Framework;

namespace SmallModels.SpaceGame;

public readonly record struct SpriteVisual(string Name, float Scale);

public sealed class SpriteGeometry
{
    private readonly Dictionary<string, SpriteMask> _masks = [];

    public static SpriteVisual Player(PlayerPose pose) =>
        new($"player-{pose.ToString().ToLowerInvariant()}", 0.43f);

    public static SpriteVisual Enemy(EnemyState enemy) => enemy.Kind == BugKind.Segfault
        ? new("boss-segfault", 0.55f)
        : new($"bug-{enemy.Kind.ToString().ToLowerInvariant()}-{enemy.Frame + 1}", enemy.Scale);

    public static SpriteVisual Shot(ProjectileKind kind) =>
        new($"shot-{kind.ToString().ToLowerInvariant()}", 0.8f);

    public static SpriteVisual Pickup(PowerUpKind kind) =>
        new($"powerup-{kind.ToString().ToLowerInvariant()}", 0.58f);

    public void Register(string name, int width, int height, Color[] pixels) =>
        _masks.Add(name, new SpriteMask(width, height, pixels));

    public bool Intersects(
        SpriteVisual first, Vector2 firstPosition,
        SpriteVisual second, Vector2 secondPosition)
    {
        var a = _masks[first.Name];
        var b = _masks[second.Name];
        var aOrigin = firstPosition - new Vector2(a.Width, a.Height) * first.Scale / 2;
        var bOrigin = secondPosition - new Vector2(b.Width, b.Height) * second.Scale / 2;
        var left = Math.Max(aOrigin.X, bOrigin.X);
        var top = Math.Max(aOrigin.Y, bOrigin.Y);
        var right = Math.Min(aOrigin.X + a.Width * first.Scale, bOrigin.X + b.Width * second.Scale);
        var bottom = Math.Min(aOrigin.Y + a.Height * first.Scale, bOrigin.Y + b.Height * second.Scale);
        if (left >= right || top >= bottom)
        {
            return false;
        }

        // Step 1: Compare overlapping source-pixel squares, excluding transparent pixels and faint halos.
        var startX = Math.Max(0, (int)MathF.Floor((left - aOrigin.X) / first.Scale));
        var startY = Math.Max(0, (int)MathF.Floor((top - aOrigin.Y) / first.Scale));
        var endX = Math.Min(a.Width, (int)MathF.Ceiling((right - aOrigin.X) / first.Scale));
        var endY = Math.Min(a.Height, (int)MathF.Ceiling((bottom - aOrigin.Y) / first.Scale));
        for (var y = startY; y < endY; y++)
        {
            for (var x = startX; x < endX; x++)
            {
                if (!a.Solid(x, y))
                {
                    continue;
                }

                var bx = (aOrigin.X + x * first.Scale - bOrigin.X) / second.Scale;
                var by = (aOrigin.Y + y * first.Scale - bOrigin.Y) / second.Scale;
                var lastX = Math.Min(b.Width, (int)MathF.Ceiling(bx + first.Scale / second.Scale));
                var lastY = Math.Min(b.Height, (int)MathF.Ceiling(by + first.Scale / second.Scale));
                for (var targetY = Math.Max(0, (int)MathF.Floor(by)); targetY < lastY; targetY++)
                {
                    for (var targetX = Math.Max(0, (int)MathF.Floor(bx)); targetX < lastX; targetX++)
                    {
                        if (b.Solid(targetX, targetY))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    public float? FirstContact(
        SpriteVisual first, Vector2 firstStart, Vector2 firstEnd,
        SpriteVisual second, Vector2 secondStart, Vector2 secondEnd)
    {
        var relativeTravel = firstEnd - firstStart - (secondEnd - secondStart);
        var samples = Math.Max(1, (int)MathF.Ceiling(
            Math.Max(Math.Abs(relativeTravel.X), Math.Abs(relativeTravel.Y)) /
            Math.Min(first.Scale, second.Scale)));

        // Step 2: Sweep both bodies by at most one rendered pixel so fast shots cannot skip a bug.
        for (var sample = 0; sample <= samples; sample++)
        {
            var fraction = (float)sample / samples;
            if (Intersects(first, Vector2.Lerp(firstStart, firstEnd, fraction),
                second, Vector2.Lerp(secondStart, secondEnd, fraction)))
            {
                return fraction;
            }
        }

        return null;
    }

    private sealed class SpriteMask(int width, int height, Color[] pixels)
    {
        public int Width { get; } = width;
        public int Height { get; } = height;
        public bool Solid(int x, int y) => pixels[y * Width + x].A >= 32;
    }
}
