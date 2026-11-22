using System.Numerics;
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Orbit;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server.Orbit;

[AdminCommand(AdminFlags.Debug)]
public sealed class SpawnStarSystemCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _ent = default!;

    public string Command => "spawnstarsystem";
    public string Description => "Spawns a star, planet markers, and a test orbital grid on the current map.";
    public string Help => "spawnstarsystem [gridRadius] [planetRadius] [planetAngleDeg]\n" +
                          "Defaults: gridRadius=200 planetRadius=140 planetAngleDeg=45 — inside a 256m radar.";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var player = shell.Player;
        if (player?.AttachedEntity is not { } ent ||
            !_ent.TryGetComponent<TransformComponent>(ent, out var xform) ||
            xform.MapUid == null)
        {
            shell.WriteError("Must be run by a player on a map.");
            return;
        }

        var gridRadius = args.Length > 0 && float.TryParse(args[0], out var gr) ? gr : 200f;
        var planetRadius = args.Length > 1 && float.TryParse(args[1], out var pr) ? pr : 140f;
        var planetAngle = args.Length > 2 && float.TryParse(args[2], out var pa) ? pa * MathF.PI / 180f : MathF.PI / 4f;

        var mapUid = xform.MapUid.Value;
        var origin = new EntityCoordinates(mapUid, Vector2.Zero);

        var star = _ent.SpawnEntity("StarSystemCenter", origin);
        shell.WriteLine($"Star: {_ent.GetNetEntity(star)}");

        // Planet placed directly at its orbital position so it's visible immediately.
        var planetOffset = new Vector2(MathF.Cos(planetAngle), MathF.Sin(planetAngle)) * planetRadius;
        var planet = _ent.SpawnEntity("OrbitalPlanetMarker", new EntityCoordinates(mapUid, planetOffset));
        var planetComp = _ent.GetComponent<OrbitalPlanetMarkerComponent>(planet);
        planetComp.Star = star;
        planetComp.OrbitRadius = planetRadius;
        planetComp.Angle = planetAngle;
        shell.WriteLine($"Planet: {_ent.GetNetEntity(planet)}");

        // Test grid spawned on its orbit at (gridRadius, 0).
        var grid = _ent.CreateEntityUninitialized(null, new EntityCoordinates(mapUid, new Vector2(gridRadius, 0f)));
        _ent.AddComponent<MapGridComponent>(grid);
        // MapGridComponent auto-attaches PhysicsComponent — EnsureComponent, never AddComponent.
        var body = _ent.EnsureComponent<PhysicsComponent>(grid);
        _ent.InitializeEntity(grid);

        var physics = _ent.System<SharedPhysicsSystem>();
        physics.SetBodyType(grid, BodyType.Dynamic, body: body);
        physics.SetLinearDamping(grid, body, 0f);
        physics.SetAngularDamping(grid, body, 0f);
        physics.SetCanCollide(grid, true, body: body);
        physics.WakeBody(grid, body: body);

        var orbit = _ent.EnsureComponent<OrbitalGridComponent>(grid);
        orbit.Star = star;
        orbit.OrbitRadius = gridRadius;
        orbit.AngularVelocity = 0.02f;
        orbit.SpinVelocity = 0.05f;
        orbit.Mode = OrbitMode.Assist;

        shell.WriteLine($"Grid: {_ent.GetNetEntity(grid)}");
    }
}
