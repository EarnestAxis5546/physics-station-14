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

    /// <summary>
    /// Returns each parallax layer set with an alpha weight based on eye distance
    /// to the zones on the map. Falls back to the map's single parallax if no zones.
    /// </summary>
    public List<(ParallaxLayerPrepared[] Layers, float Alpha)> GetWeightedLayers(MapId mapId, Vector2 eyePos)
    {
        var result = new List<(ParallaxLayerPrepared[], float)>();
        var mapUid = _map.GetMapOrInvalid(mapId);

        if (!TryComp<ZoneParallaxComponent>(mapUid, out var zoneComp) || zoneComp.Zones.Count == 0)
        {
            result.Add((GetParallaxLayers(mapId), 1f));
            return result;
        }

        foreach (var zone in zoneComp.Zones)
        {
            var d = (eyePos - zone.Center).Length();
            var t = Math.Clamp((d - zone.InnerRadius) / Math.Max(zone.OuterRadius - zone.InnerRadius, 0.001f), 0f, 1f);
            var alpha = 1f - t * t * (3f - 2f * t); // smoothstep fade

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

    /// <summary>
    /// Draws a texture as parallax in the specified world handle.
    /// </summary>
    /// <param name="worldHandle"></param>
    /// <param name="worldAABB">WorldAABB to use</param>
    /// <param name="sprite">Sprite to draw</param>
    /// <param name="curTime">Current time, unused if scrolling not set</param>
    /// <param name="position">Current position of the parallax</param>
    /// <param name="scrolling">How much to scroll the parallax texture per second</param>
    /// <param name="scale">Scale of the texture</param>
    /// <param name="slowness">How slow the parallax moves compared to position</param>
    /// <param name="modulate">Color modulation applied to drawing the texture</param>
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
        // Size of the texture in world units.
        var size = sprite.Size / (float) EyeManager.PixelsPerMeter * scale;
        var scrolled = scrolling * (float) curTime.TotalSeconds;

        // Origin - start with the parallax shift itself.
        var originBL = position * slowness + scrolled;

        // Centre the image.
        originBL -= size / 2;

        // Remove offset so we can floor.
        var flooredBL = worldAABB.BottomLeft - originBL;

        // Floor to background size.
        flooredBL = (flooredBL / size).Floored() * size;

        // Re-offset.
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
