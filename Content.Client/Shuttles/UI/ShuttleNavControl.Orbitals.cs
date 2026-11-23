// Physics Station 14
using System.Numerics;
using Content.Shared.Orbit;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleNavControl
{
    private static readonly Color StarColor = new(255, 200, 90);
    private static readonly Color StarGlowColor = new(255, 170, 60, 40);
    private static readonly Color HazardFillColor = new(255, 60, 60, 18);
    private static readonly Color HazardOutlineColor = new(255, 80, 80, 140);
    private static readonly Color OrbitRingColor = new(140, 160, 190, 70);
    private static readonly Color PlanetColor = new(120, 190, 255);
    private static readonly Color EntryFillColor = new(120, 190, 255, 14);
    private static readonly Color EntryOutlineColor = new(120, 190, 255, 80);
    private static readonly Color VelocityColor = new(120, 255, 140, 220);
    private static readonly Color EdgeTickColor = new(200, 210, 230, 160);

    /// <summary>World m/s that maps to a full-length arrow.</summary>
    private const float VelocityArrowScale = 30f;

    /// <summary>Arrow length bounds in UI pixels — zoom-independent.</summary>
    private const float ArrowMinPx = 10f;
    private const float ArrowMaxPx = 36f;

    /// <summary>Margin from the control edge for off-screen indicators.</summary>
    private const float EdgeMargin = 10f;

    /// <summary>
    /// Draws the star, hazard zone, orbital rings, planet markers, and
    /// off-screen edge ticks. Call from Draw() after the grid pass.
    /// </summary>
    private void DrawOrbitals(
        DrawingHandleScreen handle,
        Matrix3x2 worldToView,
        MapId radarMapId,
        Box2 viewAABB)
    {
        var xformQuery = EntManager.GetEntityQuery<TransformComponent>();
        var metaQuery = EntManager.GetEntityQuery<MetaDataComponent>();
        var viewCenter = MidPointVector;
        var viewRadius = MathF.Min(viewCenter.X, viewCenter.Y) - EdgeMargin;

        // --- Stars ---
        var starQuery = EntManager.EntityQueryEnumerator<StarSystemCenterComponent, TransformComponent>();
        while (starQuery.MoveNext(out var starUid, out var star, out var starXform))
        {
            if (starXform.MapID != radarMapId)
                continue;

            var starWorld = _transform.GetWorldPosition(starXform);
            var starUi = Vector2.Transform(starWorld, worldToView);
            var onScreen = viewAABB.Enlarged(star.HazardRadius).Contains(starWorld);

            if (!onScreen)
            {
                DrawEdgeTick(handle, viewCenter, viewRadius, starUi, StarColor);
                continue;
            }

            var hazardPx = star.HazardRadius * MinimapScale;
            handle.DrawCircle(starUi, hazardPx, HazardFillColor, true);
            handle.DrawCircle(starUi, hazardPx, HazardOutlineColor, false);

            // Star glyph: soft glow disc + 4-point sparkle.
            var glowPx = MathF.Max(10f, 6f * MinimapScale);
            handle.DrawCircle(starUi, glowPx, StarGlowColor, true);
            handle.DrawCircle(starUi, MathF.Max(3.5f, 1.5f * MinimapScale), StarColor, true);

            var arm = glowPx * 0.9f;
            handle.DrawLine(starUi + new Vector2(-arm, 0f), starUi + new Vector2(arm, 0f), StarColor);
            handle.DrawLine(starUi + new Vector2(0f, -arm), starUi + new Vector2(0f, arm), StarColor);

            if (metaQuery.TryComp(starUid, out var meta) && !string.IsNullOrEmpty(meta.EntityName))
            {
                var dims = handle.GetDimensions(Font, meta.EntityName, 0.8f);
                handle.DrawString(Font, starUi + new Vector2(-dims.X / 2f, glowPx + 2f), meta.EntityName, 0.8f, StarColor);
            }
        }

        // --- Planets, orbit rings, entry zones ---
        var planetQuery = EntManager.EntityQueryEnumerator<OrbitalPlanetMarkerComponent, TransformComponent>();
        while (planetQuery.MoveNext(out var planetUid, out var planet, out var planetXform))
        {
            if (planetXform.MapID != radarMapId)
                continue;

            // Orbit ring around the star.
            if (planet.Star is { } star &&
                xformQuery.TryComp(star, out var ringStarXform) &&
                ringStarXform.MapID == radarMapId)
            {
                var starWorld = _transform.GetWorldPosition(ringStarXform);
                var ringBox = Box2.CenteredAround(starWorld, new Vector2(planet.OrbitRadius * 2f));

                if (ringBox.Intersects(viewAABB) || ringBox.Contains(viewAABB.BottomLeft))
                {
                    var starUi = Vector2.Transform(starWorld, worldToView);
                    handle.DrawCircle(starUi, planet.OrbitRadius * MinimapScale, OrbitRingColor, false);
                }
            }

            var planetWorld = _transform.GetWorldPosition(planetXform);
            var planetUi = Vector2.Transform(planetWorld, worldToView);

            if (!viewAABB.Enlarged(planet.EntryRadius).Contains(planetWorld))
            {
                DrawEdgeTick(handle, viewCenter, viewRadius, planetUi, PlanetColor);
                continue;
            }

            var entryPx = planet.EntryRadius * MinimapScale;
            handle.DrawCircle(planetUi, entryPx, EntryFillColor, true);
            handle.DrawCircle(planetUi, entryPx, EntryOutlineColor, false);

            // Planet glyph: ringed dot — distinct from the star's sparkle.
            var dotPx = MathF.Max(3f, 1.2f * MinimapScale);
            handle.DrawCircle(planetUi, dotPx + 2.5f, PlanetColor.WithAlpha(0.5f), false);
            handle.DrawCircle(planetUi, dotPx, PlanetColor, true);

            if (metaQuery.TryComp(planetUid, out var meta) && !string.IsNullOrEmpty(meta.EntityName))
            {
                var dims = handle.GetDimensions(Font, meta.EntityName, 0.8f);
                // Centered below the marker, clamped inside the control.
                var labelPos = planetUi + new Vector2(-dims.X / 2f, dotPx + 6f);
                labelPos = Vector2.Clamp(labelPos, Vector2.Zero,
                    PixelSize - new Vector2(dims.X, dims.Y));
                handle.DrawString(Font, labelPos, meta.EntityName, 0.8f, PlanetColor);
            }
        }
    }

    /// <summary>
    /// Draws a small directional tick on the radar rim pointing toward an
    /// off-screen object, so distant stars/planets don't feel "missing".
    /// </summary>
    private void DrawEdgeTick(
        DrawingHandleScreen handle,
        Vector2 viewCenter,
        float viewRadius,
        Vector2 targetUi,
        Color color)
    {
        var toTarget = targetUi - viewCenter;
        var lenSq = toTarget.LengthSquared();
        if (lenSq < 1e-4f)
            return;

        var dir = toTarget / MathF.Sqrt(lenSq);
        var perp = new Vector2(-dir.Y, dir.X);

        var tip = viewCenter + dir * viewRadius;
        var baseCenter = tip - dir * 7f;
        const float halfWidth = 4f;

        var verts = new[]
        {
            tip,
            baseCenter + perp * halfWidth,
            baseCenter - perp * halfWidth,
        };

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, verts, color.WithAlpha(0.35f));
        handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, verts, color);
    }

    /// <summary>
    /// Draws a velocity vector arrow. Arrow length is computed in UI pixels
    /// (clamped to ArrowMinPx..ArrowMaxPx) so it stays readable at any zoom.
    /// </summary>
    private void DrawVelocityVector(
        DrawingHandleScreen handle,
        Matrix3x2 worldToView,
        Vector2 worldPos,
        Vector2 linearVelocity,
        Color color)
    {
        var speedSq = linearVelocity.LengthSquared();
        if (speedSq < 0.25f) // < 0.5 m/s, skip noise
            return;

        var speed = MathF.Sqrt(speedSq);
        var dir = linearVelocity / speed;

        // Pixel-space length: proportional to speed, clamped for readability.
        var lenPx = Math.Clamp(speed / VelocityArrowScale * ArrowMaxPx, ArrowMinPx, ArrowMaxPx);

        var startUi = Vector2.Transform(worldPos, worldToView);
        // Direction must be transformed through the linear part of the matrix
        // (rotation + Y-flip), not just appended in world space.
        var dirUi = Vector2.TransformNormal(dir, worldToView);
        var dirUiLenSq = dirUi.LengthSquared();
        if (dirUiLenSq < 1e-6f)
            return;

        dirUi /= MathF.Sqrt(dirUiLenSq);
        var endUi = startUi + dirUi * lenPx;

        handle.DrawLine(startUi, endUi, color);

        // Arrowhead.
        var perp = new Vector2(-dirUi.Y, dirUi.X);
        const float headLen = 6f;
        const float headSpread = 0.45f;

        handle.DrawLine(endUi, endUi - dirUi * headLen + perp * headLen * headSpread, color);
        handle.DrawLine(endUi, endUi - dirUi * headLen - perp * headLen * headSpread, color);
    }
}
