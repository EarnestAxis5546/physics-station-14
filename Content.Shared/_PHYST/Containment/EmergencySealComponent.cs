using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Containment;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class EmergencySealComponent : Component
{
    [DataField, AutoNetworkedField] public bool Deployed;

    /// <summary>Membrane entity spawned on each adjacent space/breach tile.</summary>
    [DataField] public EntProtoId MembranePrototype = "EmergencySealMembrane";

    [DataField] public float MembraneLifetime = 120f;   // seconds before auto-dissolve
    [DataField] public float ChargeDrainPerSecond = 20f;

    [DataField] public SoundSpecifier DeploySound =
        new SoundPathSpecifier("/Audio/Effects/Flare/flare_on.ogg");
    [DataField] public SoundSpecifier CollapseSound =
        new SoundPathSpecifier("/Audio/Effects/balloon_pop.ogg");
}
