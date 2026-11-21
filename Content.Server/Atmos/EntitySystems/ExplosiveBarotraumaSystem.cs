using Content.Server.Administration.Logs;
using Content.Server.Atmos.Components;
using Content.Server.Popups;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Stunnable;

namespace Content.Server.Atmos.EntitySystems;

/// <summary>
/// Хардкорная баротравма: моделирует взрывную декомпрессию (травмы от скорости изменения давления, dP/dt),
/// разрыв барабанных перепонок и эбуллизм (закипание жидкостей в организме) ниже предела Армстронга.
/// Работает на основе накопителя с шагом 0,2 с; без использования LINQ и без выделения памяти в каждом такте.
/// </summary>
public sealed partial class ExplosiveBarotraumaSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private BarotraumaSystem _barotraumaSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private BlindableSystem _blindableSystem = default!;
    [Dependency] private PopupSystem _popupSystem = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;

    private const float UpdateInterval = 0.2f;

    /// <summary>Скорость изменения ощущаемого давления (в кПа/с), вызывающая мгновенную травму.</summary>
    private const float TraumaDpDtThreshold = 50f;

    /// <summary>Предел Армстронга: вода закипает при температуре тела ниже этого значения (кПа).</summary>
    private const float ArmstrongLimit = 6.3f;

    /// <summary>Секунды воздействия эбуллизма перед параличом организма.</summary>
    private const float EbullismParalysisDelay = 2.0f;

    private float _accumulator;

    // Спецификаторы кэшируются, чтобы в «горячем» цикле не выполнялось выделение памяти.
    private static readonly DamageSpecifier TraumaDamage = new()
    {
        DamageDict = { ["Blunt"] = 55, ["Asphyxiation"] = 35 }
    };

    private readonly DamageSpecifier _ebullismDamage = new()
    {
        DamageDict = { ["Cellular"] = 0, ["Asphyxiation"] = 0 }
    };

    public override void Initialize()
    {
        SubscribeLocalEvent<PressureHistoryComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<PressureHistoryComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.LastFeltPressure = Atmospherics.OneAtmosphere;
        ent.Comp.HasInitializedPressure = false;
        ent.Comp.EbullismTimer = 0f;
        ent.Comp.RupturedEardrums = false;
    }

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        var dt = _accumulator;
        _accumulator = 0f;

        var enumerator = EntityQueryEnumerator<PressureHistoryComponent, BarotraumaComponent, DamageableComponent, TransformComponent>();
        while (enumerator.MoveNext(out var uid, out var history, out var barotrauma, out var damageable, out var xform))
        {
            // Dead mobs don't suffer new barotrauma.
            if (TryComp<MobStateComponent>(uid, out var mobState) && mobState.CurrentState == MobState.Dead)
                continue;

            // Давление среды в тайле; в качестве резервного варианта — использование содержащейся в нем смеси (шкафчики, криокамеры).
            var mixture = _atmosphereSystem.GetTileMixture((uid, xform))
                          ?? _atmosphereSystem.GetContainingMixture((uid, xform));

            var envPressure = mixture?.Pressure ?? 0f;

            // Учитываем наличие геркостюмов / защиты от давления / иммунитета, чтобы человек в герметичном снаряжении «ощущал» давление около 1 атм — независимо от внешней среды.
            var felt = envPressure <= Atmospherics.OneAtmosphere
                ? _barotraumaSystem.GetFeltLowPressure(uid, barotrauma, envPressure)
                : _barotraumaSystem.GetFeltHighPressure(uid, barotrauma, envPressure);

            // First sample after spawn: baseline only, never trauma.
            if (!history.HasInitializedPressure)
            {
                history.LastFeltPressure = felt;
                history.HasInitializedPressure = true;
                continue;
            }

            var dPdt = (felt - history.LastFeltPressure) / dt;
            history.LastFeltPressure = felt;

            // --- Instant dP/dt barotrauma (explosive decompression / shockwave) ---
            if (MathF.Abs(dPdt) >= TraumaDpDtThreshold)
            {
                _damageableSystem.TryChangeDamage(uid, TraumaDamage, ignoreResistances: true, interruptsDoAfters: false);

                var stun = TimeSpan.FromSeconds(3);
                _stunSystem.TryKnockdown(uid, stun, force: true);
                _stunSystem.TryUpdateParalyzeDuration(uid, stun);

                _blindableSystem.AdjustEyeDamage(uid, 2);
                history.RupturedEardrums = true;

                _popupSystem.PopupEntity(
                    Loc.GetString("barotrauma-explosive-decompression"),
                    uid, uid, PopupType.LargeCaution);

                _adminLogger.Add(LogType.Barotrauma, LogImpact.High,
                    $"{ToPrettyString(uid):entity} suffered explosive barotrauma (dP/dt {dPdt:F1} kPa/s)");
            }

            // --- Armstrong limit / ebullism ---
            if (felt < ArmstrongLimit)
            {
                history.EbullismTimer += dt;

                // Нарастающий урон: показатель DPS растёт линейно в зависимости от времени воздействия,
                // что позволяет уничтожить незащищённого энтити примерно за 5–8 секунд.
                var scale = 1f + history.EbullismTimer;
                _ebullismDamage.DamageDict["Cellular"] = 4f * scale * dt;
                _ebullismDamage.DamageDict["Asphyxiation"] = 6f * scale * dt;
                _damageableSystem.TryChangeDamage(uid, _ebullismDamage, ignoreResistances: true, interruptsDoAfters: false);

                // Intravascular gas bubbles -> collapse.
                if (history.EbullismTimer > EbullismParalysisDelay)
                {
                    _stunSystem.TryUpdateParalyzeDuration(uid, TimeSpan.FromSeconds(1));
                }
            }
            else if (history.EbullismTimer > 0f)
            {
                // Gradual recovery once pressure is restored.
                history.EbullismTimer = MathF.Max(0f, history.EbullismTimer - dt * 0.5f);
            }
        }
    }
}
