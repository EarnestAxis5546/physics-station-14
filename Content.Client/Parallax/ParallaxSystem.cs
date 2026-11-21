using System.Numerics;
using Content.Client.Parallax.Data;
using Content.Client.Parallax.Managers;
using Content.Shared.Parallax;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client.Parallax;

public sealed partial class ParallaxSystem : SharedParallaxSystem
{
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private IParallaxManager _parallax = default!;
    [Dependency] private SharedMapSystem _map = default!;

    private static readonly ProtoId<ParallaxPrototype> Fallback = "Default";

    public const int ParallaxZIndex = 0;

    public override void Initialize()
    {
        base.Initialize();
        _overlay.AddOverlay(new ParallaxOverlay());
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnReload);
        SubscribeLocalEvent<ParallaxComponent, AfterAutoHandleStateEvent>(OnAfterAutoHandleState);
        SubscribeLocalEvent<ZoneParallaxComponent, AfterAutoHandleStateEvent>(OnZoneAfterAutoHandleState);
    }

    private void OnReload(PrototypesReloadedEventArgs obj)
    {
        if (!obj.WasModified<ParallaxPrototype>())
            return;

        _parallax.UnloadParallax(Fallback);
        _parallax.LoadDefaultParallax();

        foreach (var comp in EntityQuery<ParallaxComponent>(true))
        {
            _parallax.UnloadParallax(comp.Parallax);
            _parallax.LoadParallaxByName(comp.Parallax);
        }

        foreach (var comp in EntityQuery<ZoneParallaxComponent>(true))
        {
            foreach (var zone in comp.Zones)
            {
                _parallax.UnloadParallax(zone.Parallax);
                _parallax.LoadParallaxByName(zone.Parallax);
            }
        }
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay<ParallaxOverlay>();
    }

    private void OnAfterAutoHandleState(EntityUid uid, ParallaxComponent component, ref AfterAutoHandleStateEvent args)
    {
        if (!_parallax.IsLoaded(component.Parallax))
        {
            _parallax.LoadParallaxByName(component.Parallax);
        }
    }

    private void OnZoneAfterAutoHandleState(EntityUid uid, ZoneParallaxComponent component, ref AfterAutoHandleStateEvent args)
    {
        foreach (var zone in component.Zones)
        {
            if (!_parallax.IsLoaded(zone.Parallax))
                _parallax.LoadParallaxByName(zone.Parallax);
        }
    }

    public ParallaxLayerPrepared[] GetParallaxLayers(MapId mapId)
    {
        return _parallax.GetParallaxLayers(GetParallax(_map.GetMapOrInvalid(mapId)));
    }

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / Math.Max(edge1 - edge0, 0.001f), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// Returns each parallax layer set with an alpha weight based on eye distance
    /// to the zones on the map, including an opaque base layer.
    /// </summary>
    public List<(ParallaxLayerPrepared[] Layers, float Alpha)> GetWeightedLayers(MapId mapId, Vector2 eyePos)
    {
        var result = new List<(ParallaxLayerPrepared[], float)>();

        // Гарантированный непрозрачный базовый слой, чтобы фон не становился черным
        result.Add((GetParallaxLayers(mapId), 1f));

        var mapUid = _map.GetMapOrInvalid(mapId);

        if (!TryComp<ZoneParallaxComponent>(mapUid, out var zoneComp) || zoneComp.Zones.Count == 0)
        {
            return result;
        }

        foreach (var zone in zoneComp.Zones)
        {
            var d = (eyePos - zone.Center).Length();

            var fadeIn = Smoothstep(zone.InnerRadius - zone.FadeWidth, zone.InnerRadius, d);
            var fadeOut = 1f - Smoothstep(zone.OuterRadius, zone.OuterRadius + zone.FadeWidth, d);
            var alpha = Math.Min(fadeIn, fadeOut);

            if (alpha > 0.001f)
                result.Add((_parallax.GetParallaxLayers(zone.Parallax), alpha));
        }

        return result;
    }

    public string GetParallax(MapId mapId)
    {
        return GetParallax(_map.GetMapOrInvalid(mapId));
    }

    public string GetParallax(EntityUid mapUid)
    {
        return TryComp<ParallaxComponent>(mapUid, out var parallax) ? parallax.Parallax : Fallback;
    }

    public void DrawParallax(
        DrawingHandleWorld worldHandle,
        Box2 worldAABB,
        Texture sprite,
        TimeSpan curTime,
        Vector2 position,
        Vector2 scrolling,
        float scale = 1f,
        float slowness = 0f,
        Color? modulate = null)
    {
        var size = sprite.Size / (float) EyeManager.PixelsPerMeter * scale;
        var scrolled = scrolling * (float) curTime.TotalSeconds;

        var originBL = position * slowness + scrolled;
        originBL -= size / 2;

        var flooredBL = worldAABB.BottomLeft - originBL;
        flooredBL = (flooredBL / size).Floored() * size;
        flooredBL += originBL;

        for (var x = flooredBL.X; x < worldAABB.Right; x += size.X)
        {
            for (var y = flooredBL.Y; y < worldAABB.Top; y += size.Y)
            {
                var box = Box2.FromDimensions(new Vector2(x, y), size);
                worldHandle.DrawTextureRect(sprite, box, modulate);
            }
        }
    }
}
