using System.Numerics;
using Content.Shared.Orbit;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server.Orbit;

/// <summary>
/// Server-side driver for star-system orbital mechanics.
/// Planet markers are moved kinematically; grids are steered via physics
/// velocity so the solver keeps them smooth and clients interpolate.
/// </summary>
public sealed class OrbitalSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<StarSystemCenterComponent> _starQuery;

    /// <summary>
    /// Server-side tracking of which grids are inside which planet's entry
    /// zone. Not networked, not persisted.
    /// </summary>
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _insidePlanetZones = new();

    public override void Initialize()
    {
        base.Initialize();
        _xformQuery = GetEntityQuery<TransformComponent>();
        _starQuery = GetEntityQuery<StarSystemCenterComponent>();

        SubscribeLocalEvent<OrbitalPlanetMarkerComponent, MapInitEvent>(OnPlanetMapInit);
        SubscribeLocalEvent<OrbitalGridComponent, MapInitEvent>(OnGridMapInit);
        SubscribeLocalEvent<OrbitalGridComponent, EntityTerminatingEvent>(OnGridTerminating);
    }

    private void OnPlanetMapInit(Entity<OrbitalPlanetMarkerComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.Star ??= FindStarOnMap(ent);
    }

    private void OnGridMapInit(Entity<OrbitalGridComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.Star ??= FindStarOnMap(ent);
        ent.Comp.SettleTimer = ent.Comp.SettleTime;

        // Seed the orbital angle from the spawn position so eccentric orbits
        // start at the correct anomaly instead of jumping to angle 0.
        if (ent.Comp.Star is { } star &&
            _xformQuery.TryComp(star, out var starXform) &&
            _xformQuery.TryComp(ent, out var gridXform))
        {
            var toGrid = _xform.GetWorldPosition(gridXform) - _xform.GetWorldPosition(starXform);
            if (toGrid.LengthSquared() > 1e-6f)
                ent.Comp.Angle = MathF.Atan2(toGrid.Y, toGrid.X);
        }
    }

    private void OnGridTerminating(Entity<OrbitalGridComponent> ent, ref EntityTerminatingEvent args)
    {
        _insidePlanetZones.Remove(ent);
    }

    private EntityUid? FindStarOnMap(EntityUid uid)
    {
        if (!_xformQuery.TryComp(uid, out var xform) || xform.MapUid == null)
            return null;

        var query = EntityQueryEnumerator<StarSystemCenterComponent, TransformComponent>();
        while (query.MoveNext(out var starUid, out _, out var starXform))
        {
            if (starXform.MapUid == xform.MapUid && !HasComp<MapGridComponent>(starUid))
                return starUid;
        }
        return null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdatePlanetMarkers(frameTime);
        UpdateOrbitalGrids(frameTime);
        UpdateStarHazards();
    }

    private void UpdatePlanetMarkers(float frameTime)
    {
        var query = EntityQueryEnumerator<OrbitalPlanetMarkerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var planet, out var xform))
        {
            if (planet.Star is not { } star ||
                !_starQuery.TryComp(star, out var starComp) ||
                !_xformQuery.TryComp(star, out var starXform))
                continue;

            planet.Angle = (planet.Angle + planet.AngularVelocity * starComp.OrbitalSpeedMultiplier * frameTime)
                % MathF.Tau;

            var starPos = _xform.GetWorldPosition(starXform);
            var offset = new Vector2(MathF.Cos(planet.Angle), MathF.Sin(planet.Angle)) * planet.OrbitRadius;
            _xform.SetWorldPosition(uid, starPos + offset);
        }
    }

    private void UpdateOrbitalGrids(float frameTime)
    {
        var query = EntityQueryEnumerator<OrbitalGridComponent, TransformComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var orbit, out var xform, out var body))
        {
            var gridPos = _xform.GetWorldPosition(xform);

            // Free mode: no steering, just zone tracking.
            if (orbit.Mode == OrbitMode.Free)
            {
                CheckPlanetEntry(uid, gridPos, xform.MapID);
                continue;
            }

            if (orbit.Star is not { } star ||
                !_starQuery.TryComp(star, out var starComp) ||
                !_xformQuery.TryComp(star, out var starXform) ||
                xform.MapID != starXform.MapID)
                continue;

            var starPos = _xform.GetWorldPosition(starXform);
            var toGrid = gridPos - starPos;
            var dist = toGrid.Length();

            if (dist < 1e-3f)
                continue;

            // Settle ramp: 0 → 1 over SettleTime so spawns aren't yanked.
            var settle = 1f;
            if (orbit.SettleTimer > 0f)
            {
                orbit.SettleTimer = MathF.Max(0f, orbit.SettleTimer - frameTime);
                var t = 1f - orbit.SettleTimer / MathF.Max(orbit.SettleTime, 1e-3f);
                settle = t * t * (3f - 2f * t); // smoothstep
            }

            var radial = toGrid / dist;
            var angular = orbit.AngularVelocity * starComp.OrbitalSpeedMultiplier;

            // Advance the tracked anomaly (used for eccentric orbits).
            orbit.Angle = (orbit.Angle + angular * frameTime) % MathF.Tau;

            // Desired radius: circle, or ellipse via the orbit equation.
            var e = Math.Clamp(orbit.Eccentricity, 0f, 0.95f);
            var desiredRadius = e > 0f
                ? orbit.OrbitRadius * (1f - e * e) / (1f + e * MathF.Cos(orbit.Angle))
                : orbit.OrbitRadius;

            // Tangent direction follows the sign of angular velocity.
            var tangent = new Vector2(-radial.Y, radial.X) * MathF.Sign(angular);

            // Ideal tangential speed scales with desired radius, not current
            // distance — this is what stops far-away grids being flung.
            var tangentialVel = tangent * MathF.Abs(angular) * desiredRadius;

            // Gentle radial spring toward the desired radius.
            var radialError = desiredRadius - dist;
            var radialVel = radial * radialError * orbit.CorrectionStrength;

            var desired = tangentialVel + radialVel;

            // Exponential approach to desired velocity — frame-rate independent,
            // no error/frameTime blowups, and capped so Assist stays gentle.
            var damping = orbit.Mode == OrbitMode.Locked
                ? MathF.Max(orbit.VelocityDamping, 2f)
                : orbit.VelocityDamping;
            var blend = 1f - MathF.Exp(-damping * settle * frameTime);
            var newVel = Vector2.Lerp(body.LinearVelocity, desired, blend);

            _physics.SetLinearVelocity(uid, newVel, body: body);

            if (!MathHelper.CloseTo(orbit.SpinVelocity, 0f))
                _physics.SetAngularVelocity(uid, orbit.SpinVelocity, body: body);

            CheckPlanetEntry(uid, gridPos, xform.MapID);
        }
    }

    /// <summary>
    /// Checks every physics-enabled grid on each star's map against the star's
    /// hazard radius — including shuttles and other non-orbital grids.
    /// The star itself is never a grid, so it can't self-trigger.
    /// </summary>
    private void UpdateStarHazards()
    {
        var stars = new List<(EntityUid Uid, StarSystemCenterComponent Comp, Vector2 Pos, MapId Map)>();
        var starQuery = EntityQueryEnumerator<StarSystemCenterComponent, TransformComponent>();
        while (starQuery.MoveNext(out var starUid, out var starComp, out var starXform))
        {
            stars.Add((starUid, starComp, _xform.GetWorldPosition(starXform), starXform.MapID));
        }

        if (stars.Count == 0)
            return;

        var gridQuery = EntityQueryEnumerator<MapGridComponent, TransformComponent, PhysicsComponent>();
        while (gridQuery.MoveNext(out var gridUid, out _, out var gridXform, out _))
        {
            var gridPos = _xform.GetWorldPosition(gridXform);

            foreach (var (starUid, starComp, starPos, starMap) in stars)
            {
                if (gridXform.MapID != starMap)
                    continue;

                if ((gridPos - starPos).Length() < starComp.HazardRadius)
                    OnStarHazard(gridUid, starUid, starComp);
            }
        }
    }

    /// <summary>
    /// Hook: called when a grid enters a star's hazard radius.
    /// Stage 1 stub — replace with damage/destruction logic.
    /// </summary>
    private void OnStarHazard(EntityUid grid, EntityUid star, StarSystemCenterComponent starComp)
    {
        // TODO: destroy grid, apply heat damage, spawn debris, etc.
        Log.Warning($"Grid {ToPrettyString(grid)} entered hazard radius of {ToPrettyString(star)}");
    }

    private void CheckPlanetEntry(EntityUid grid, Vector2 gridPos, MapId gridMap)
    {
        if (!_insidePlanetZones.TryGetValue(grid, out var inside))
        {
            inside = new HashSet<EntityUid>();
            _insidePlanetZones[grid] = inside;
        }

        var query = EntityQueryEnumerator<OrbitalPlanetMarkerComponent, TransformComponent>();
        while (query.MoveNext(out var planetUid, out var planet, out var planetXform))
        {
            if (planetXform.MapID != gridMap)
                continue;

            var planetPos = _xform.GetWorldPosition(planetXform);
            var isInside = (gridPos - planetPos).Length() < planet.EntryRadius;

            if (isInside && inside.Add(planetUid))
                OnPlanetEntry(grid, planetUid, planet);
            else if (!isInside)
                inside.Remove(planetUid);
        }

        if (inside.Count == 0)
            _insidePlanetZones.Remove(grid);
    }

    /// <summary>
    /// Hook: called once when a grid enters a planet's entry radius.
    /// </summary>
    private void OnPlanetEntry(EntityUid grid, EntityUid planet, OrbitalPlanetMarkerComponent planetComp)
    {
        // TODO: trigger planet-zone transition.
        Log.Info($"Grid {ToPrettyString(grid)} entered zone of {ToPrettyString(planet)}");
    }
}
