// Physics Station 14
using Content.Shared.Atmos;

namespace Content.Server.Atmos.Components;

/// <summary>
/// <see cref="EntitySystems.ExplosiveBarotraumaSystem"/> can compute dP/dt
/// </summary>
[RegisterComponent]
public sealed partial class PressureHistoryComponent : Component
{
    /// <summary>
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public float LastFeltPressure = Atmospherics.OneAtmosphere;

    /// <summary>
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public bool HasInitializedPressure;

    /// <summary>
    /// Лимит армстронга т.е 6.5 КПа
    /// эббулизм и ничего более
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public float EbullismTimer;

    /// <summary>
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public bool RupturedEardrums;
}
