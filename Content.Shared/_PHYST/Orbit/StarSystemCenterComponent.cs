// Physics Station 1
using Robust.Shared.GameStates;

namespace Content.Shared.Orbit;

/// <summary>
/// Marks an entity as the gravitational center of a star system.
/// Orbiting grids and planet markers resolve against this entity.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class StarSystemCenterComponent : Component
{
    /// <summary>
    /// Global multiplier applied to all orbital angular velocities in this system.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float OrbitalSpeedMultiplier = 1f;

    /// <summary>
    /// World-space radius around the star that is lethal.
    /// Any grid inside gets passed to the hazard hook in OrbitalSystem.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float HazardRadius = 30f;
}
