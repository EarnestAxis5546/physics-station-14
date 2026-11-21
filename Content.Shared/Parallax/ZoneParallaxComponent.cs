using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class ZoneParallaxComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<ParallaxZone> Zones = new();
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class ParallaxZone
{
    [DataField(required: true)] public string Parallax = "Default";
    [DataField] public Vector2 Center = Vector2.Zero; // map-local coords
    [DataField] public float InnerRadius;             // full alpha inside
    [DataField] public float OuterRadius = 500f;      // zero alpha outside
}
