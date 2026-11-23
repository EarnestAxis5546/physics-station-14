using System.Numerics;
using Content.Shared.Orbit;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

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
    /// Server-side tracking of which grids are currently inside which planet's
    /// entry zone. Key: grid uid, Value: set of planet uids it's inside.
    /// Kept outside component state — not networked, not persisted.
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
            if (starXform.MapUid == xform.MapUid)
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

            // Markers are kinematic map objects — a direct position set is fine
            // and cheap since they have no physics body.
            _xform.SetWorldPosition(uid, starPos + offset);
        }
    }

    private void UpdateOrbitalGrids(float frameTime)
    {
        var query = EntityQueryEnumerator<OrbitalGridComponent, TransformComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var orbit, out var xform, out var body))
        {
            if (orbit.Star is not { } star ||
                !_starQuery.TryComp(star, out var starComp) ||
                !_xformQuery.TryComp(star, out var starXform) ||
                xform.MapID != starXform.MapID)
                continue;

            var starPos = _xform.GetWorldPosition(starXform);
            var gridPos = _xform.GetWorldPosition(xform);
            var toGrid = gridPos - starPos;
            var dist = toGrid.Length();

            if (dist < 1e-3f)
                continue;

            var radial = toGrid / dist;
            var tangent = new Vector2(-radial.Y, radial.X); // counter-clockwise

            // Ideal tangential speed for the current angle rate.
            var angular = orbit.AngularVelocity * starComp.OrbitalSpeedMultiplier;
            var tangentialVel = tangent * angular * dist;

            // Gentle radial spring toward the desired orbit radius.
            // CorrectionStrength is a 1/s factor — no error/frameTime snapping.
            var radialError = orbit.OrbitRadius - dist;
            var radialVel = radial * radialError * orbit.CorrectionStrength;

            var desired = tangentialVel + radialVel;

            // Blend toward desired velocity so we don't fight the solver.
            var blend = MathF.Min(1f, orbit.CorrectionStrength * frameTime * 10f);
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
    /// </summary>
    private void UpdateStarHazards()
    {
        // Collect star positions per map once.
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

    /// <summary>
    /// Fires <see cref="OnPlanetEntry"/> once when a grid crosses into a
    /// planet's entry zone, and again only after it has fully left.
    /// Only planets on the same MapID are considered.
    /// </summary>
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
            {
                OnPlanetEntry(grid, planetUid, planet);
            }
            else if (!isInside)
            {
                inside.Remove(planetUid);
            }
        }

        if (inside.Count == 0)
            _insidePlanetZones.Remove(grid);
    }

    /// <summary>
    /// Hook: called once when a grid enters a planet's entry radius.
    /// Stage 1 stub — replace with FTL-to-planet-map / docking logic.
    /// </summary>
    private void OnPlanetEntry(EntityUid grid, EntityUid planet, OrbitalPlanetMarkerComponent planetComp)
    {
        // TODO: trigger planet-zone transition.
        Log.Info($"Grid {ToPrettyString(grid)} entered zone of {ToPrettyString(planet)}");
    }
}
