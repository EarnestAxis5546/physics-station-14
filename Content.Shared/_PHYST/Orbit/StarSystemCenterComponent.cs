using Content.Shared.Damage;
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

    /// <summary>
    /// Seconds between hazard effect ticks while a grid is inside
    /// <see cref="HazardRadius"/>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float HazardTickInterval = 1.0f;

    /// <summary>
    /// Damage applied per hazard tick to every damageable entity on an
    /// affected grid. Defaults to heat damage.
    /// </summary>
    [DataField, AutoNetworkedField]
    public DamageSpecifier HazardDamage = new()
    {
        DamageDict = { ["Heat"] = 5 }
    };

    /// <summary>
    /// Fire stacks added to flammable entities per hazard tick.
    /// Set to 0 to disable ignition.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float FireStacksPerTick = 0.5f;

    /// <summary>
    /// Number of random grid tiles destroyed per hazard tick.
    /// Set to 0 to disable progressive grid destruction.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int TilesDestroyedPerTick = 2;

    /// <summary>
    /// If true, entities on affected grids take <see cref="HazardDamage"/>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool DamageEntities = true;
}
