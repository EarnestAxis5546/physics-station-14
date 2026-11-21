// Physics Station 14 -- Suit Integrity Component
using Content.Server.Atmos.EntitySystems;

namespace Content.Server.Atmos.Components;

/// <summary>
/// Tracks structural integrity, breach state and internal thermal
/// environment of a pressure-protective suit or helmet.
/// </summary>
[RegisterComponent]
[Access(typeof(SuitIntegritySystem))]
public sealed partial class SuitIntegrityComponent : Component
{
    /// <summary>Максимальная структурная целостность.</summary>
    [DataField]
    public float MaxIntegrity = 100f;

    /// <summary>Текущая структурная целостность.</summary>
    [DataField]
    public float CurrentIntegrity = 100f;

    /// <summary>Внутренняя температура костюма в Кельвинах. Начинается с 20°C.</summary>
    [DataField]
    public float InternalTemperature = 293.15f;

    /// <summary>0-1, насколько хорошо костюм противостоит внешнему теплу/холоду.</summary>
    [DataField]
    public float ThermalInsulation = 0.85f;

    /// <summary>Ватты активного терморегулирования в секунду.</summary>
    [DataField]
    public float CoolingCapacity = 150f;

    /// <summary>Доля максимальной целостности, ниже которой костюм считается поврежденным.</summary>
    [DataField]
    public float BreachThreshold = 0.7f;

    /// <summary>Доля, ниже которой костюм полностью утрачивает герметичность.</summary>
    [DataField]
    public float SevereBreachThreshold = 0.3f;

    /// <summary>Утрата целостности за каждую единицу полученного урона, затрагивающего костюм.</summary>
    [DataField]
    public float IntegrityPerDamage = 1f;

    /// <summary>Целостность восстановлена ​​с помощью сварочного аппарата / ремонтного комплекта.</summary>
    [DataField]
    public float RepairAmount = 25f;

    /// <summary>Теплоемкость внутреннего пространства костюма (J/K).</summary>
    [DataField]
    public float InternalHeatCapacity = 4000f;

    /// <summary>Теплопроводность между внутренним пространством костюма и окружающей средой (W/K).</summary>
    [DataField]
    public float EnvironmentalConductance = 12f;

    /// <summary>Скорость радиационных теплопотерь в вакууме (Вт/К).</summary>
    [DataField]
    public float RadiativeLoss = 2.5f;

    /// <summary>Теплообмен между костюмом и носителем на каждый кельвин разницы (Вт/К).</summary>
    [DataField]
    public float WearerConductance = 30f;

    /// <summary>Порог гипертермии для внутреннего пространства костюма (K).</summary>
    [DataField]
    public float HyperthermiaThreshold = 315f;

    /// <summary>Порог гипотермии для внутреннего пространства костюма (K).</summary>
    [DataField]
    public float HypothermiaThreshold = 278f;

    [ViewVariables]
    public bool IsBreached;

    /// <summary>Отображается последний уровень оповещения — во избежание спама всплывающими окнами.</summary>
    [ViewVariables]
    public int LastAlertLevel;
}
