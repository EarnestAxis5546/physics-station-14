// Physics Station 14
using Robust.Shared.GameStates;

namespace Content.Shared.Orbit;

/// <summary>
/// Lightweight map-level marker representing a planet orbiting a star.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OrbitalPlanetMarkerComponent : Component
{
    /// <summary>
    /// The star this planet orbits. If null, auto-resolved on the same map on init.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Star;

    /// <summary>
    /// Orbital radius in meters from the star's world position.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float OrbitRadius = 500f;

    /// <summary>
    /// Angular velocity in radians per second (before the star's multiplier).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float AngularVelocity = 0.002f;

    /// <summary>
    /// Current orbital angle in radians. Updated server-side.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Angle;

    /// <summary>
    /// Radius around the marker that counts as "entering the planet zone".
    /// </summary>
    [DataField, AutoNetworkedField]
    public float EntryRadius = 100f;
}
