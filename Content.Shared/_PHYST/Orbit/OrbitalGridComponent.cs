using Robust.Shared.GameStates;

namespace Content.Shared.Orbit;

/// <summary>
/// How strongly the grid is held to its configured orbit.
/// </summary>
public enum OrbitMode : byte
{
    /// <summary>No steering at all — marker/tracking only.</summary>
    Free,

    /// <summary>Gentle velocity nudge toward the orbit; physics can still win.</summary>
    Assist,

    /// <summary>Firmly steered onto the configured orbit.</summary>
    Locked,
}

/// <summary>
/// Attached to grids (stations, meteors, shuttles) that orbit a star.
/// Movement is driven through physics velocity, not transform teleports.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OrbitalGridComponent : Component
{
    /// <summary>The star to orbit. If null, auto-resolved on the same map.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Star;

    /// <summary>Desired orbital radius in meters (semi-major axis if eccentric).</summary>
    [DataField, AutoNetworkedField]
    public float OrbitRadius = 300f;

    /// <summary>
    /// Angular velocity in rad/s (before star multiplier).
    /// Sign controls direction: positive = counter-clockwise, negative = clockwise.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float AngularVelocity = 0.01f;

    /// <summary>
    /// Orbital eccentricity, 0 = circle, (0,1) = ellipse. The grid's current
    /// angle is treated as the anomaly; radius oscillates between
    /// OrbitRadius*(1-e) and OrbitRadius*(1+e).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Eccentricity;

    /// <summary>Optional self-rotation (rad/s).</summary>
    [DataField, AutoNetworkedField]
    public float SpinVelocity;

    /// <summary>How firmly the grid is held to its orbit.</summary>
    [DataField, AutoNetworkedField]
    public OrbitMode Mode = OrbitMode.Assist;

    /// <summary>
    /// Spring constant (1/s) pulling the grid toward its desired orbit radius.
    /// Keep small — this is deliberately NOT error/frameTime.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float CorrectionStrength = 0.15f;

    /// <summary>
    /// Fraction of "wrong" velocity removed per second (exponential damping
    /// toward the desired velocity). Higher = tighter tracking, lower = floatier.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float VelocityDamping = 0.8f;

    /// <summary>
    /// Seconds after MapInit during which steering ramps in from 0 to full,
    /// so freshly spawned grids aren't yanked.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float SettleTime = 5f;

    /// <summary>Current orbital angle in radians (server-updated, used for eccentric orbits).</summary>
    [DataField, AutoNetworkedField]
    public float Angle;

    /// <summary>Server-side settle timer. Not networked.</summary>
    [ViewVariables]
    public float SettleTimer;
}
