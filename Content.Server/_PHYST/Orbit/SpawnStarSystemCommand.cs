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
    public string Help => "spawnstarsystem";

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

        var mapUid = xform.MapUid.Value;
        var origin = new EntityCoordinates(mapUid, Vector2.Zero);

        var star = _ent.SpawnEntity("StarSystemCenter", origin);
        shell.WriteLine($"Star: {_ent.GetNetEntity(star)}");

        var planet = _ent.SpawnEntity("OrbitalPlanetMarker", origin);
        _ent.GetComponent<OrbitalPlanetMarkerComponent>(planet).Star = star;
        shell.WriteLine($"Planet: {_ent.GetNetEntity(planet)}");

        // Test grid: bare grid spawned at (OrbitRadius, 0) relative to the star
        // so it starts on its intended orbit rather than inside the hazard zone.
        const float orbitRadius = 300f;
        var gridCoords = new EntityCoordinates(mapUid, new Vector2(orbitRadius, 0f));

        var grid = _ent.CreateEntityUninitialized(null, gridCoords);
        _ent.AddComponent<MapGridComponent>(grid);
        var body = _ent.AddComponent<PhysicsComponent>(grid);
        _ent.InitializeEntity(grid);

        // Configure the body so velocity steering actually moves it:
        // dynamic, awake, and unaffected by damping/friction defaults.
        var physics = _ent.System<SharedPhysicsSystem>();
        physics.SetBodyType(grid, BodyType.Dynamic, body: body);
        physics.SetLinearDamping(grid, body, 0f);
        physics.SetAngularDamping(grid, body, 0f);
        physics.SetCanCollide(grid, true, body: body);
        physics.WakeBody(grid, body: body);

        var orbit = _ent.EnsureComponent<OrbitalGridComponent>(grid);
        orbit.Star = star;
        orbit.OrbitRadius = orbitRadius;
        orbit.AngularVelocity = 0.01f;
        orbit.SpinVelocity = 0.05f;

        shell.WriteLine($"Grid: {_ent.GetNetEntity(grid)}");
    }
}
