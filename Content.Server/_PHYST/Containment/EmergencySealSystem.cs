using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Power.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Containment;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Power.Components;
using Content.Shared.Timing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server.Containment;

public sealed class EmergencySealSystem : EntitySystem
{
    [Dependency] private readonly AirtightSystem _airtight = default!;
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly BatterySystem _battery = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly AtmosDirection[] CardinalDirections =
    [
        AtmosDirection.North,
        AtmosDirection.South,
        AtmosDirection.East,
        AtmosDirection.West
    ];

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EmergencySealComponent, ActivateInWorldEvent>(OnActivate);
    }

    private void OnActivate(Entity<EmergencySealComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (!Transform(ent).Anchored)
        {
            _popup.PopupEntity(Loc.GetString("emergency-seal-must-anchor"), ent, args.User);
            return;
        }

        if (ent.Comp.Deployed)
            Collapse(ent, args.User);
        else
            Deploy(ent, args.User);
    }

    private void Deploy(Entity<EmergencySealComponent> ent, EntityUid user)
    {
        ent.Comp.Deployed = true;
        Dirty(ent);

        if (TryComp<AirtightComponent>(ent, out var airtight))
            _airtight.SetAirblocked((ent, airtight), true);

        // Spawn a dissolving membrane on each adjacent tile that is space/leaking.
        var xform = Transform(ent);
        if (xform.GridUid is { } grid && TryComp<MapGridComponent>(grid, out var gridComp))
        {
            var tile = _xform.GetGridTilePositionOrDefault((ent, xform), gridComp);
            var map = xform.MapUid;

            GridAtmosphereComponent? gridAtmos = null;
            TryComp(grid, out gridAtmos);

            MapAtmosphereComponent? mapAtmos = null;
            if (map is { } mapUid)
                TryComp(mapUid, out mapAtmos);

            Entity<MapAtmosphereComponent?>? mapAtmosEntity = map is { } atmosphereMap
                ? (atmosphereMap, mapAtmos)
                : null;

            foreach (var dir in CardinalDirections)
            {
                var neighbor = tile.Offset(dir.ToDirection());
                if (_atmos.IsTileSpace((grid, gridAtmos), mapAtmosEntity, neighbor))
                {
                    var coords = _xform.ToCoordinates(grid, neighbor, gridComp);
                    var mem = Spawn(ent.Comp.MembranePrototype, coords);
                    var timed = EnsureComp<TimedDespawnComponent>(mem);
                    timed.Lifetime = ent.Comp.MembraneLifetime;
                }
            }
        }

        _audio.PlayPvs(ent.Comp.DeploySound, ent);
        _popup.PopupEntity(Loc.GetString("emergency-seal-deployed"), ent, user);
    }

    private void Collapse(Entity<EmergencySealComponent> ent, EntityUid? user)
    {
        ent.Comp.Deployed = false;
        Dirty(ent);

        if (TryComp<AirtightComponent>(ent, out var airtight))
            _airtight.SetAirblocked((ent, airtight), false);

        _audio.PlayPvs(ent.Comp.CollapseSound, ent);
        if (user is { } u)
            _popup.PopupEntity(Loc.GetString("emergency-seal-collapsed"), ent, u);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<EmergencySealComponent, BatteryComponent>();
        while (query.MoveNext(out var uid, out var seal, out var battery))
        {
            if (!seal.Deployed)
                continue;

            if (!_battery.TryUseCharge((uid, battery), seal.ChargeDrainPerSecond * frameTime))
                Collapse((uid, seal), null);
        }
    }
}
