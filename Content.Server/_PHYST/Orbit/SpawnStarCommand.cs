using System.Numerics;
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Orbit;
using Robust.Shared.Console;
using Robust.Shared.Map;

namespace Content.Server.Orbit;

[AdminCommand(AdminFlags.Debug)]
public sealed class SpawnStarCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entManager = default!;

    public string Command => "spawnstar";
    public string Description => "Spawns a star entity on the player's current map.";
    public string Help => "spawnstar [hazardRadius] [x y] — defaults to radius 40 at (0,0), or player coords if x y given without values.";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player || player.AttachedEntity is not { } attached)
        {
            shell.WriteError("This command must be run by a player with an attached entity.");
            return;
        }

        if (!_entManager.TryGetComponent(attached, out TransformComponent? xform))
        {
            shell.WriteError("Player entity has no transform.");
            return;
        }

        var hazardRadius = 40f;
        if (args.Length >= 1 && !float.TryParse(args[0], out hazardRadius))
        {
            shell.WriteError($"'{args[0]}' is not a valid hazard radius.");
            return;
        }

        MapCoordinates coords;
        if (args.Length >= 3)
        {
            if (!float.TryParse(args[1], out var x) || !float.TryParse(args[2], out var y))
            {
                shell.WriteError("Invalid coordinates.");
                return;
            }
            coords = new MapCoordinates(new Vector2(x, y), xform.MapID);
        }
        else
        {
            // Default: map origin on the player's current map.
            coords = new MapCoordinates(Vector2.Zero, xform.MapID);
        }

        var star = _entManager.SpawnEntity("StarSystemCenter", coords);

        if (hazardRadius != 40f &&
            _entManager.TryGetComponent(star, out StarSystemCenterComponent? comp))
        {
            comp.HazardRadius = hazardRadius;
            _entManager.Dirty(star, comp);
        }

        shell.WriteLine($"Spawned star {_entManager.GetNetEntity(star)} at {coords} " +
                        $"with hazard radius {hazardRadius}.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHint("<hazardRadius>"),
            2 => CompletionResult.FromHint("<x>"),
            3 => CompletionResult.FromHint("<y>"),
            _ => CompletionResult.Empty,
        };
    }
}
