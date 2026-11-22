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
    private static readonly Color StarColor = Color.Orange;
    private static readonly Color HazardFillColor = Color.Red.WithAlpha(0.12f);
    private static readonly Color HazardOutlineColor = Color.Red.WithAlpha(0.6f);
    private static readonly Color OrbitRingColor = Color.Gray.WithAlpha(0.4f);
    private static readonly Color PlanetColor = Color.LightSkyBlue;
    private static readonly Color EntryFillColor = Color.LightSkyBlue.WithAlpha(0.08f);
    private static readonly Color EntryOutlineColor = Color.LightSkyBlue.WithAlpha(0.35f);
    private static readonly Color VelocityColor = Color.Lime.WithAlpha(0.9f);

    /// <summary>
    /// World m/s of linear velocity that maps to a full-length arrow.
    /// </summary>
    private const float VelocityArrowScale = 30f;

    /// <summary>
    /// Maximum arrow length in world meters before scaling to view.
    /// </summary>
    private const float VelocityArrowMaxWorld = 40f;

    /// <summary>
    /// Draws the star, hazard zone, orbital rings, and planet markers.
    /// Call from Draw() after the grid pass.
    /// </summary>
    /// <param name="worldToView">worldToShuttle * shuttleToView</param>
    /// <param name="radarMapId">Map the radar is on.</param>
    /// <param name="viewAABB">World-space culling bounds.</param>
    private void DrawOrbitals(
        DrawingHandleScreen handle,
        Matrix3x2 worldToView,
        MapId radarMapId,
        Box2 viewAABB)
    {
        var xformQuery = EntManager.GetEntityQuery<TransformComponent>();
        var metaQuery = EntManager.GetEntityQuery<MetaDataComponent>();

        // --- Stars, hazard zones ---
        var starQuery = EntManager.EntityQueryEnumerator<StarSystemCenterComponent, TransformComponent>();
        while (starQuery.MoveNext(out var starUid, out var star, out var starXform))
        {
            if (starXform.MapID != radarMapId)
                continue;

            var starWorld = _transform.GetWorldPosition(starXform);

            // Single consistent culling check for the whole star marker:
            // skip everything if the hazard circle can't intersect the view.
            if (!viewAABB.Enlarged(star.HazardRadius).Contains(starWorld))
                continue;

            var starUi = Vector2.Transform(starWorld, worldToView);
            var hazardPx = star.HazardRadius * MinimapScale;

            handle.DrawCircle(starUi, hazardPx, HazardFillColor, true);
            handle.DrawCircle(starUi, hazardPx, HazardOutlineColor, false);

            // Star marker: filled dot + cross so it's visible at any zoom.
            handle.DrawCircle(starUi, MathF.Max(4f, 2f * MinimapScale), StarColor, true);
            handle.DrawLine(starUi + new Vector2(-8f, 0f), starUi + new Vector2(8f, 0f), StarColor);
            handle.DrawLine(starUi + new Vector2(0f, -8f), starUi + new Vector2(0f, 8f), StarColor);

            if (metaQuery.TryComp(starUid, out var meta) && !string.IsNullOrEmpty(meta.EntityName))
                handle.DrawString(Font, starUi + new Vector2(10f, -6f), meta.EntityName, 0.8f, StarColor);
        }

        // --- Planet markers, orbit rings, entry zones ---
        var planetQuery = EntManager.EntityQueryEnumerator<OrbitalPlanetMarkerComponent, TransformComponent>();
        while (planetQuery.MoveNext(out var planetUid, out var planet, out var planetXform))
        {
            if (planetXform.MapID != radarMapId)
                continue;

            // Orbit ring around the star (only if the star resolves on this map).
            if (planet.Star is { } star &&
                xformQuery.TryComp(star, out var ringStarXform) &&
                ringStarXform.MapID == radarMapId)
            {
                var starWorld = _transform.GetWorldPosition(ringStarXform);
                var ringBox = Box2.CenteredAround(starWorld, new Vector2(planet.OrbitRadius * 2f));

                // Draw the ring if its bounds intersect the view OR fully contain it
                // (we're inside the ring looking out).
                if (ringBox.Intersects(viewAABB) || ringBox.Contains(viewAABB.BottomLeft))
                {
                    var starUi = Vector2.Transform(starWorld, worldToView);
                    handle.DrawCircle(starUi, planet.OrbitRadius * MinimapScale, OrbitRingColor, false);
                }
            }

            var planetWorld = _transform.GetWorldPosition(planetXform);
            if (!viewAABB.Enlarged(planet.EntryRadius).Contains(planetWorld))
                continue;

            var planetUi = Vector2.Transform(planetWorld, worldToView);
            var entryPx = planet.EntryRadius * MinimapScale;

            handle.DrawCircle(planetUi, entryPx, EntryFillColor, true);
            handle.DrawCircle(planetUi, entryPx, EntryOutlineColor, false);
            handle.DrawCircle(planetUi, MathF.Max(3f, 1.5f * MinimapScale), PlanetColor, true);

            if (metaQuery.TryComp(planetUid, out var meta) && !string.IsNullOrEmpty(meta.EntityName))
                handle.DrawString(Font, planetUi + new Vector2(8f, -6f), meta.EntityName, 0.8f, PlanetColor);
        }
    }

    /// <summary>
    /// Draws a velocity vector arrow for a physics body at a world position.
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
        var dir = linearVelocity / speed; // safe: speed > 0 checked above
        var len = MathF.Min(speed / VelocityArrowScale, 1f) * VelocityArrowMaxWorld;

        var startUi = Vector2.Transform(worldPos, worldToView);
        var endUi = Vector2.Transform(worldPos + dir * len, worldToView);

        handle.DrawLine(startUi, endUi, color);

        // Arrowhead: two short lines fanned back from the tip.
        var arrowVec = endUi - startUi;
        var arrowLenSq = arrowVec.LengthSquared();
        if (arrowLenSq < 1e-4f)
            return;

        var arrowDir = arrowVec / MathF.Sqrt(arrowLenSq);
        var perp = new Vector2(-arrowDir.Y, arrowDir.X);
        const float headLen = 6f;
        const float headSpread = 0.5f;

        handle.DrawLine(endUi, endUi - arrowDir * headLen + perp * headLen * headSpread, color);
        handle.DrawLine(endUi, endUi - arrowDir * headLen - perp * headLen * headSpread, color);
    }
}
