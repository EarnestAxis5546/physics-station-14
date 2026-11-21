// Physics Station 14
using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Parallax;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class ZoneParallaxComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<ParallaxZone> Zones = new()
    {
        new() { Parallax = "Default", Center = Vector2.Zero, InnerRadius = 0f,    OuterRadius = 2500f },
        new() { Parallax = "ExoStation",   Center = Vector2.Zero, InnerRadius = 2500f, OuterRadius = 5000f },
        new() { Parallax = "KettleStation",  Center = Vector2.Zero, InnerRadius = 5000f, OuterRadius = 99999f },
    };
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class ParallaxZone
{
    [DataField(required: true)] public string Parallax = "Default";
    [DataField] public Vector2 Center = Vector2.Zero;
    [DataField] public float InnerRadius;        // full alpha inside this distance
    [DataField] public float OuterRadius = 500f; // fades to zero at this distance
}
