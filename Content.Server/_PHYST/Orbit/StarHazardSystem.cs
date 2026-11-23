using System.Numerics;
using Content.Shared.Orbit;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server.Orbit;

/// <summary>
/// Periodically checks physics-enabled grids against each star's hazard
/// radius and raises <see cref="OnStarHazard"/> on entry/exit.
/// </summary>
public sealed class StarHazardSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>
    /// Grids currently inside at least one star's hazard zone.
    /// Keyed by grid uid so entry/exit transitions are clean.
    /// </summary>
    private readonly HashSet<EntityUid> _gridsInHazard = new();

    /// <summary>
    /// Seconds between hazard checks. Distance checks are cheap but there's
    /// no need to run them every tick.
    /// </summary>
    private const float CheckInterval = 0.5f;

    private float _accumulator;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < CheckInterval)
            return;

        _accumulator = 0f;
        CheckHazardZones();
    }

    private void CheckHazardZones()
    {
        // Collect stars first so the grid query isn't nested inside it.
        var stars = new List<(EntityUid Uid, StarSystemCenterComponent Comp, Vector2 Pos, MapId Map)>();
        var starQuery = EntityQueryEnumerator<StarSystemCenterComponent, TransformComponent>();
        while (starQuery.MoveNext(out var uid, out var star, out var xform))
        {
            // Defense-in-depth: a star must never be a grid or a physics body.
            if (HasComp<MapGridComponent>(uid) || HasComp<PhysicsComponent>(uid))
            {
                Log.Warning($"Star {ToPrettyString(uid)} has grid/physics components; skipping.");
                continue;
            }

            if (xform.MapID == MapId.Nullspace)
                continue;

            stars.Add((uid, star, _transform.GetWorldPosition(xform), xform.MapID));
        }

        var seenThisCheck = new HashSet<EntityUid>();

        var gridQuery = EntityQueryEnumerator<MapGridComponent, TransformComponent, PhysicsComponent>();
        while (gridQuery.MoveNext(out var gridUid, out _, out var xform, out var body))
        {
            if (!body.CanCollide || xform.MapID == MapId.Nullspace)
                continue;

            var gridPos = _transform.GetWorldPosition(xform);
            var inside = false;

            foreach (var (starUid, star, starPos, starMap) in stars)
            {
                if (xform.MapID != starMap)
                    continue;

                if ((gridPos - starPos).LengthSquared() <= star.HazardRadius * star.HazardRadius)
                {
                    inside = true;
                    if (_gridsInHazard.Add(gridUid))
                        OnStarHazard(starUid, gridUid, entered: true);
                    break;
                }
            }

            if (inside)
                seenThisCheck.Add(gridUid);
        }

        // Anything still marked but no longer inside has exited.
        _gridsInHazard.RemoveWhere(uid =>
        {
            if (seenThisCheck.Contains(uid))
                return false;

            if (!Deleted(uid))
                OnStarHazard(EntityUid.Invalid, uid, entered: false);

            return true;
        });
    }

    /// <summary>
    /// Hook invoked when a grid enters or leaves a star's hazard zone.
    /// Stub for now — later steps can apply heat damage, alerts, etc.
    /// </summary>
    /// <param name="starUid">The star whose zone was entered, or
    /// <see cref="EntityUid.Invalid"/> on exit.</param>
    /// <param name="gridUid">The grid entering/exiting.</param>
    /// <param name="entered">True on entry, false on exit.</param>
    private void OnStarHazard(EntityUid starUid, EntityUid gridUid, bool entered)
    {
        if (entered)
            Log.Info($"Grid {ToPrettyString(gridUid)} entered hazard zone of star {ToPrettyString(starUid)}.");
        else
            Log.Info($"Grid {ToPrettyString(gridUid)} left all star hazard zones.");
    }
}
