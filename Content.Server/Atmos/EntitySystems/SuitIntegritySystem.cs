// Physics Station 14 -- Suit Integrity System
using Content.Server.Atmos.Components;
using Content.Server.Popups;
using Content.Server.Stack;
using Content.Server.Temperature.Systems;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Temperature.Components;
using Content.Shared.Tools.Components;
using Robust.Shared.Containers;

namespace Content.Server.Atmos.EntitySystems;


public sealed partial class SuitIntegritySystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private BarotraumaSystem _barotrauma = default!;
    [Dependency] private TemperatureSystem _temperature = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private StackSystem _stack = default!;

    private const float UpdateInterval = 1f;
    private float _accumulator;

    /// <summary>Fraction of wearer damage forwarded to the outer suit.</summary>
    private const float SuitDamageShare = 0.5f;

    /// <summary>Fraction of wearer damage forwarded to the helmet.</summary>
    private const float HelmetDamageShare = 0.25f;

    // Damage types that meaningfully tear a suit.
    private static readonly string[] BreachDamageTypes = { "Blunt", "Slash", "Piercing", "Heat", "Shock" };

    public override void Initialize()
    {
        SubscribeLocalEvent<SuitIntegrityComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SuitIntegrityComponent, GetPressureProtectionValuesEvent>(OnGetPressureValues);
        SubscribeLocalEvent<SuitIntegrityComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<SuitIntegrityComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<SuitIntegrityComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<SuitIntegrityComponent, GotUnequippedEvent>(OnUnequipped);

        // Damage lands on the WEARER's DamageableComponent, not the suit.
        SubscribeLocalEvent<InventoryComponent, DamageDealtEvent>(OnWearerDamageDealt);
    }

    private void OnMapInit(Entity<SuitIntegrityComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.CurrentIntegrity = ent.Comp.MaxIntegrity;
        UpdateBreachState(ent);
    }

    #region Damage & breach

    private void OnWearerDamageDealt(Entity<InventoryComponent> ent, ref DamageDealtEvent args)
    {
        var relevant = 0f;
        foreach (var type in BreachDamageTypes)
        {
            if (args.Damage.DamageDict.TryGetValue(type, out var amount) && amount > 0)
                relevant += amount.Float();
        }

        if (relevant <= 0f)
            return;

        var refreshed = false;

        if (_inventory.TryGetSlotEntity(ent.Owner, "outerClothing", out var suit, ent.Comp)
            && TryComp<SuitIntegrityComponent>(suit.Value, out var suitComp))
        {
            refreshed |= DegradeSuit((suit.Value, suitComp), relevant * SuitDamageShare, ent.Owner);
        }

        if (_inventory.TryGetSlotEntity(ent.Owner, "head", out var helmet, ent.Comp)
            && TryComp<SuitIntegrityComponent>(helmet.Value, out var helmetComp))
        {
            refreshed |= DegradeSuit((helmet.Value, helmetComp), relevant * HelmetDamageShare, ent.Owner);
        }

        // If neither slot degraded (no suit worn), nothing to refresh.
        if (refreshed && TryComp<BarotraumaComponent>(ent.Owner, out var barotrauma))
            _barotrauma.UpdateCachedResistances(ent.Owner, barotrauma);
    }

    private bool DegradeSuit(Entity<SuitIntegrityComponent> suit, float amount, EntityUid wearer)
    {
        var comp = suit.Comp;
        if (comp.CurrentIntegrity <= 0f)
            return false;

        var oldBreached = comp.IsBreached;
        comp.CurrentIntegrity = MathF.Max(0f, comp.CurrentIntegrity - amount * comp.IntegrityPerDamage);
        UpdateBreachState(suit);

        if (!oldBreached && comp.IsBreached)
            _popup.PopupEntity(Loc.GetString("suit-integrity-breach-warning"), wearer, wearer, PopupType.LargeCaution);

        return true;
    }

    private void OnGetPressureValues(Entity<SuitIntegrityComponent> ent, ref GetPressureProtectionValuesEvent args)
    {
        var ratio = ent.Comp.MaxIntegrity <= 0f ? 0f : ent.Comp.CurrentIntegrity / ent.Comp.MaxIntegrity;

        if (ratio <= ent.Comp.SevereBreachThreshold)
        {
            // Seal lost: suit no longer protects against vacuum or overpressure.
            args.LowPressureMultiplier = 1f;
            args.HighPressureMultiplier = 1f;
            args.LowPressureModifier = 0f;
            args.HighPressureModifier = 0f;
            return;
        }

        // Scale protection linearly between severe-breach and intact.
        var seal = (ratio - ent.Comp.SevereBreachThreshold) / (1f - ent.Comp.SevereBreachThreshold);

        // LowPressureMultiplier is huge (1000) when intact; lerp toward 1.
        args.LowPressureMultiplier = 1f + (args.LowPressureMultiplier - 1f) * seal;
        // HighPressureMultiplier is small (e.g. 0.02) when intact; lerp toward 1.
        args.HighPressureMultiplier = 1f - (1f - args.HighPressureMultiplier) * seal;
        args.LowPressureModifier *= seal;
        args.HighPressureModifier *= seal;
    }

    private void UpdateBreachState(Entity<SuitIntegrityComponent> ent)
    {
        ent.Comp.IsBreached = ent.Comp.CurrentIntegrity < ent.Comp.MaxIntegrity * ent.Comp.BreachThreshold;
    }

    private void OnEquipped(Entity<SuitIntegrityComponent> ent, ref GotEquippedEvent args)
        => RefreshWearerResistances(args.EquipTarget);

    private void OnUnequipped(Entity<SuitIntegrityComponent> ent, ref GotUnequippedEvent args)
        => RefreshWearerResistances(args.EquipTarget);

    private void RefreshWearerResistances(Entity<SuitIntegrityComponent> ent)
    {
        if (_container.TryGetContainingContainer(ent.Owner, out var container))
            RefreshWearerResistances(container.Owner);
    }

    private void RefreshWearerResistances(EntityUid wearer)
    {
        if (TryComp<BarotraumaComponent>(wearer, out var barotrauma))
            _barotrauma.UpdateCachedResistances(wearer, barotrauma);
    }

    #endregion

    #region Thermal simulation

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;
        var dt = _accumulator;
        _accumulator = 0f;

        var query = EntityQueryEnumerator<SuitIntegrityComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            SimulateThermal((uid, comp), dt);
        }
    }

    private void SimulateThermal(Entity<SuitIntegrityComponent> ent, float dt)
    {
        var comp = ent.Comp;

        // Only simulate when worn; find the wearer via the inventory slot container.
        if (!_container.TryGetContainingContainer(ent.Owner, out var container))
            return;
        var wearer = container.Owner;
        if (!HasComp<InventoryComponent>(wearer))
            return;

        // Ambient temperature from the wearer's tile (vacuum => radiative loss).
        var ambient = Atmospherics.TCMB; // deep space ~2.7K
        var inVacuum = true;
        if (_atmosphere.GetTileMixture(wearer) is { } mixture)
        {
            ambient = mixture.Temperature;
            inVacuum = mixture.Pressure < Atmospherics.HazardLowPressure;
        }

        // Insulation degrades with integrity.
        var ratio = comp.MaxIntegrity <= 0f ? 0f : comp.CurrentIntegrity / comp.MaxIntegrity;
        var insulation = comp.ThermalInsulation * ratio;

        // Conductive exchange with environment.
        var conductance = comp.EnvironmentalConductance * (1f - insulation);
        var envHeat = (ambient - comp.InternalTemperature) * conductance * dt;

        // Extra radiative loss in vacuum.
        if (inVacuum)
            envHeat += (Atmospherics.TCMB - comp.InternalTemperature) * comp.RadiativeLoss * (1f - insulation) * dt;

        // Wearer metabolic heat leaks into the suit interior.
        var wearerTemp = TryComp<TemperatureComponent>(wearer, out var wt) ? wt.Temperature : Atmospherics.T20C;
        var wearerHeat = (wearerTemp - comp.InternalTemperature) * comp.WearerConductance * 0.2f * dt;

        // Active thermal regulation pushes interior toward a comfortable setpoint.
        var setpoint = 293.15f;
        var regulation = MathHelper.Clamp((setpoint - comp.InternalTemperature) * comp.InternalHeatCapacity,
            -comp.CoolingCapacity * dt, comp.CoolingCapacity * dt);
        // A severely breached suit can't run its thermal loop.
        if (ratio <= comp.SevereBreachThreshold)
            regulation = 0f;

        comp.InternalTemperature += (envHeat + wearerHeat + regulation) / comp.InternalHeatCapacity;

        // Danger thresholds: dump heat into / pull heat from the wearer.
        var alert = 0;
        if (comp.InternalTemperature > comp.HyperthermiaThreshold)
        {
            var excess = (comp.InternalTemperature - comp.HyperthermiaThreshold) * comp.InternalHeatCapacity * 0.5f;
            _temperature.ChangeHeat(wearer, excess, ignoreHeatResistance: true);
            comp.InternalTemperature -= excess / comp.InternalHeatCapacity;
            alert = 2;
        }
        else if (comp.InternalTemperature < comp.HypothermiaThreshold)
        {
            var deficit = (comp.HypothermiaThreshold - comp.InternalTemperature) * comp.InternalHeatCapacity * 0.5f;
            _temperature.ChangeHeat(wearer, -deficit, ignoreHeatResistance: true);
            comp.InternalTemperature += deficit / comp.InternalHeatCapacity;
            alert = 1;
        }

        if (alert != comp.LastAlertLevel)
        {
            comp.LastAlertLevel = alert;
            switch (alert)
            {
                case 2:
                    _popup.PopupEntity(Loc.GetString("suit-integrity-overheat-warning"), wearer, wearer, PopupType.LargeCaution);
                    break;
                case 1:
                    _popup.PopupEntity(Loc.GetString("suit-integrity-freeze-warning"), wearer, wearer, PopupType.LargeCaution);
                    break;
            }
        }
    }

    #endregion

    #region Examine & repair

    private void OnExamined(Entity<SuitIntegrityComponent> ent, ref ExaminedEvent args)
    {
        var comp = ent.Comp;
        var pct = comp.MaxIntegrity <= 0f ? 0 : (int) (100f * comp.CurrentIntegrity / comp.MaxIntegrity);

        var integrityKey = pct switch
        {
            >= 90 => "suit-integrity-examine-integrity-good",
            >= 70 => "suit-integrity-examine-integrity-worn",
            >= 30 => "suit-integrity-examine-integrity-damaged",
            _ => "suit-integrity-examine-integrity-critical",
        };
        args.PushMarkup(Loc.GetString(integrityKey, ("percent", pct)));

        var sealKey = pct >= 70
            ? "suit-integrity-examine-seal-airtight"
            : pct >= 30
                ? "suit-integrity-examine-seal-leaking"
                : "suit-integrity-examine-seal-breached";
        args.PushMarkup(Loc.GetString(sealKey));

        var celsius = comp.InternalTemperature - 273.15f;
        var tempKey = comp.InternalTemperature > comp.HyperthermiaThreshold
            ? "suit-integrity-examine-temp-hot"
            : comp.InternalTemperature < comp.HypothermiaThreshold
                ? "suit-integrity-examine-temp-cold"
                : "suit-integrity-examine-temp-nominal";
        args.PushMarkup(Loc.GetString(tempKey,
            ("kelvin", (int) comp.InternalTemperature),
            ("celsius", (int) celsius)));
    }

    private void OnInteractUsing(Entity<SuitIntegrityComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var comp = ent.Comp;
        if (comp.CurrentIntegrity >= comp.MaxIntegrity)
            return;

        // Welder repair.
        if (TryComp<WelderComponent>(args.Used, out var welder) && welder.Enabled)
        {
            Repair(ent, comp.RepairAmount, args.User);
            args.Handled = true;
            return;
        }

        // Patch repair via cloth/plastic stack (duct tape, cloth, etc.).
        if (TryComp<StackComponent>(args.Used, out var stack) && stack.Count > 0)
        {
            if (!_stack.TryUse((args.Used, stack), 1))
                return;
            Repair(ent, comp.RepairAmount * 0.6f, args.User);
            args.Handled = true;
        }
    }

    private void Repair(Entity<SuitIntegrityComponent> ent, float amount, EntityUid user)
    {
        var comp = ent.Comp;
        comp.CurrentIntegrity = MathF.Min(comp.MaxIntegrity, comp.CurrentIntegrity + amount);
        UpdateBreachState(ent);
        RefreshWearerResistances(ent);
        _popup.PopupEntity(
            Loc.GetString("suit-integrity-repaired", ("percent", (int) (100f * comp.CurrentIntegrity / comp.MaxIntegrity))),
            ent.Owner, user);
    }

    #endregion
}
