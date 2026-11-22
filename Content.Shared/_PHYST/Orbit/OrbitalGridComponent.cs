using Robust.Shared.GameStates;

namespace Content.Shared.Orbit;

/// <summary>
/// Attached to grids (stations, meteors, shuttles) that orbit a star.
/// Movement is driven through physics velocity, not transform teleports.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OrbitalGridComponent : Component
{
    /// <summary>
    /// The star to orbit. If null, auto-resolved on the same map.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Star;

    /// <summary>
    /// Desired orbital radius in meters.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float OrbitRadius = 300f;

    /// <summary>
    /// Angular velocity in radians per second (before star multiplier).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float AngularVelocity = 0.01f;

    /// <summary>
    /// Optional self-rotation (rad/s) applied to the grid's angular velocity.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float SpinVelocity;

    /// <summary>
    /// How aggressively the grid corrects toward its ideal orbit position.
    /// Acts as a spring constant — keep small to avoid jitter/snapping.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float CorrectionStrength = 0.5f;
}
