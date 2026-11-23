using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Orbit;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Orbit;

/// <summary>
/// Periodically checks physics-enabled grids against each star's hazard
/// radius, raises <see cref="OnStarHazard"/> on entry/exit, and applies
/// hazard effects (entity damage, ignition, tile destruction) on a
/// per-star tick timer while grids remain inside.
/// </summary>
public sealed class StarHazardSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private FlammableSystem _flammable = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;

    [Dependency] private EntityQuery<DamageableComponent> _damageableQuery = default!;
    [Dependency] private EntityQuery<FlammableComponent> _flammableQuery = default!;

    /// <summary>
    /// Grids currently inside at least one star's hazard zone.
    /// </summary>
    private readonly HashSet<EntityUid> _gridsInHazard = new();

    /// <summary>
    /// Next hazard-effect tick time per star.
    /// </summary>
    private readonly Dictionary<EntityUid, TimeSpan> _nextTick = new();

    /// <summary>
    /// Seconds between hazard zone membership checks.
    /// </summary>
    private const float CheckInterval = 0.5f;

    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StarSystemCenterComponent, EntityTerminatingEvent>(OnStarTerminating);
        SubscribeLocalEvent<MapGridComponent, EntityTerminatingEvent>(OnGridTerminating);
    }

    private void OnStarTerminating(Entity<StarSystemCenterComponent> ent, ref EntityTerminatingEvent args)
    {
        _nextTick.Remove(ent.Owner);
    }

    private void OnGridTerminating(Entity<MapGridComponent> ent, ref EntityTerminatingEvent args)
    {
        _gridsInHazard.Remove(ent.Owner);
    }

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
        var stars = new List<(EntityUid Uid, StarSystemCenterComponent Comp, Vector2 Pos, MapId Map)>();
        var starQuery = EntityQueryEnumerator<StarSystemCenterComponent, TransformComponent>();
        while (starQuery.MoveNext(out var uid, out var star, out var xform))
        {
            // A star must never be a grid or a physics body.
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
        while (gridQuery.MoveNext(out var gridUid, out var grid, out var xform, out var body))
        {
            if (!body.CanCollide || xform.MapID == MapId.Nullspace)
                continue;

            var gridPos = _transform.GetWorldPosition(xform);

            foreach (var (starUid, star, starPos, starMap) in stars)
            {
                if (xform.MapID != starMap)
                    continue;

                if ((gridPos - starPos).LengthSquared() > star.HazardRadius * star.HazardRadius)
                    continue;

                seenThisCheck.Add(gridUid);
                if (_gridsInHazard.Add(gridUid))
                    OnStarHazard(starUid, gridUid, entered: true);

                // Apply effects on the star's own tick cadence.
                var now = _timing.CurTime;
                if (!_nextTick.TryGetValue(starUid, out var next) || now >= next)
                {
                    _nextTick[starUid] = now + TimeSpan.FromSeconds(star.HazardTickInterval);
                    ApplyHazardTick(starUid, star, (gridUid, grid, xform));
                }

                break;
            }
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
    /// Applies one tick of hazard effects to a grid inside a star's corona:
    /// heat damage + ignition to entities on the grid, and progressive
    /// destruction of random tiles.
    /// </summary>
    private void ApplyHazardTick(
        EntityUid starUid,
        StarSystemCenterComponent star,
        Entity<MapGridComponent, TransformComponent> gridEnt)
    {
        var (gridUid, grid, xform) = gridEnt;

        if (star.DamageEntities)
            DamageEntitiesOnGrid(starUid, star, gridUid);

        if (star.TilesDestroyedPerTick > 0)
            DestroyRandomTiles(star, gridUid, grid);
    }

    /// <summary>
    /// Damages and ignites entities parented to the grid. Entities on a grid
    /// are children of the grid's transform, so the child enumerator covers
    /// everything aboard without a broadphase lookup.
    /// </summary>
    private void DamageEntitiesOnGrid(EntityUid starUid, StarSystemCenterComponent star, EntityUid gridUid)
    {
        var xform = Transform(gridUid);
        var enumerator = xform.ChildEnumerator;

        while (enumerator.MoveNext(out var child))
        {
            // Never touch the star itself, and tolerate mid-tick deletion.
            if (child == starUid || TerminatingOrDeleted(child))
                continue;

            if (_damageableQuery.TryComp(child, out var damageable))
            {
                _damageable.TryChangeDamage(
                    (child, damageable),
                    star.HazardDamage,
                    ignoreResistances: false,
                    interruptsDoAfters: false,
                    origin: starUid);
            }

            if (star.FireStacksPerTick > 0 &&
                !TerminatingOrDeleted(child) &&
                _flammableQuery.TryComp(child, out var flammable))
            {
                _flammable.AdjustFireStacks(child, star.FireStacksPerTick, flammable);
                _flammable.Ignite(child, starUid, flammable);
            }
        }
    }

    /// <summary>
    /// Removes up to <see cref="StarSystemCenterComponent.TilesDestroyedPerTick"/>
    /// random non-empty tiles from the grid in a single batched SetTiles call.
    /// </summary>
    private void DestroyRandomTiles(StarSystemCenterComponent star, EntityUid gridUid, MapGridComponent grid)
    {
        var aabb = grid.LocalAABB;
        var removed = new List<(Vector2i GridIndices, Tile Tile)>();

        // Bounded attempts so sparse grids don't loop forever.
        var attempts = star.TilesDestroyedPerTick * 8;
        for (var i = 0; i < attempts && removed.Count < star.TilesDestroyedPerTick; i++)
        {
            var minX = (int)MathF.Floor(aabb.Left);
            var maxX = (int)MathF.Ceiling(aabb.Right);
            var minY = (int)MathF.Floor(aabb.Bottom);
            var maxY = (int)MathF.Ceiling(aabb.Top);

            var indices = new Vector2i(
                _random.Next(minX, maxX),
                _random.Next(minY, maxY));

            if (!_mapSystem.TryGetTileRef(gridUid, grid, indices, out var tileRef) ||
                tileRef.Tile.IsEmpty)
                continue;

            removed.Add((indices, Tile.Empty));
        }

        if (removed.Count > 0)
            _mapSystem.SetTiles(gridUid, grid, removed);
    }

    /// <summary>
    /// Hook invoked when a grid enters or leaves a star's hazard zone.
    /// </summary>
    private void OnStarHazard(EntityUid starUid, EntityUid gridUid, bool entered)
    {
        if (entered)
            Log.Info($"Grid {ToPrettyString(gridUid)} entered hazard zone of star {ToPrettyString(starUid)}.");
        else
            Log.Info($"Grid {ToPrettyString(gridUid)} left all star hazard zones.");
    }
}
