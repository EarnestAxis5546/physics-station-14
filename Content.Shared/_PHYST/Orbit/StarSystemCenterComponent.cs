using Robust.Shared.GameStates;

namespace Content.Shared.Orbit;

/// <summary>
/// Marks an entity as the central star of a map. The star is a plain
/// map-level entity: it must never have MapGridComponent or PhysicsComponent,
/// so it can neither act as a grid nor trigger hazard checks on itself.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class StarSystemCenterComponent : Component
{
    /// <summary>
    /// Radius (in meters) around the star considered hazardous to grids.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float HazardRadius = 40f;

    /// <summary>
    /// Multiplier applied to orbital speeds of bodies orbiting this star.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float OrbitalSpeedMultiplier = 1f;
}
