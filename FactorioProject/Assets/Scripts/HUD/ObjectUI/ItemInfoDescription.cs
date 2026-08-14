using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ItemInfoDescription : MonoBehaviour
{
    private const int DefaultConveyorInfoSlotCount = 2;
    private const int Belt2FInfoSlotCount = 6;
    private const string DefaultFluidItemName = "Water";
    private const string SteamFluidItemName = "Steam";
    private const string FireEnergyItemName = "Fire";
    private const string ElectricityItemName = "Electricity";
    private const float GaugeFillLerpSpeed = 12f;
    private const float GaugeFillSnapThreshold = 0.0025f;
    private static readonly Color FluidGaugeFillColor = new Color(0.08f, 0.55f, 1f, 1f);
    private static readonly Color ElectricGaugeFillColor = new Color(1f, 0.72f, 0.08f, 1f);
    private static readonly Color BurnEnergyGaugeFillColor = new Color(1f, 0.42f, 0.08f, 1f);
    private static readonly Color HealthGaugeFillColor = new Color(0.78f, 0.12f, 0.1f, 1f);
    private static readonly Color ProducingSignColor = new Color(0.1f, 0.8f, 0.1f, 1f);
    private static readonly Color StoppedSignColor = new Color(0.9f, 0.05f, 0.03f, 1f);
    private static readonly Dictionary<Image, float> GaugeFillTargets = new Dictionary<Image, float>();

    [SerializeField]
    private List<GameObject> defaultParent = new List<GameObject>();
    [SerializeField]
    private List<TextMeshProUGUI> defaultText = new List<TextMeshProUGUI>();
    [SerializeField]
    private List<Image> defaultSign = new List<Image>();
    [SerializeField]
    private GameObject energyGauge, workGauge, defaultGauge;
    [SerializeField]
    private Image energyFill, workFill, defaultFill;
    [SerializeField]
    private TextMeshProUGUI energyText, workText, defaultGaugeText;
    [SerializeField]
    private List<GameObject> defaultItem;
    [SerializeField]
    private GameObject energyItem, inputItem, outputItem;
    [SerializeField]
    private List<ItemSlot> defaultItemSlot;
    [SerializeField]
    private ItemSlot energyItemSlot, inputItemSlot, outputItemSlot;

    private readonly List<int> conveyorItemIds = new List<int>(2);
    private readonly List<int> defaultItemOriginalSiblingIndices = new List<int>();
    private int defaultStatusLineIndex;
    private bool defaultItemSiblingIndicesCaptured;
    private RobotArm liveGaugeRobotArm;
    private UtilityPole liveGaugeUtilityPole;
    private LightObject liveGaugeLightObject;
    private InputOutputModule liveGaugeModule;
    private RailHandcar liveGaugeRailHandcar;

    private void Awake()
    {
        ResolveDefaultItemSlotReferences();
        ResolveGaugeReferences();
    }

    private void OnValidate()
    {
        ResolveDefaultItemSlotReferences();
        ResolveGaugeReferences();
    }

    private void Update()
    {
        RefreshLiveGaugeTargets();
        UpdateGaugeFill(energyFill);
        UpdateGaugeFill(workFill);
        UpdateGaugeFill(defaultFill);
    }

    public void Clear()
    {
        ResolveDefaultItemSlotReferences();
        ResolveGaugeReferences();
        RestoreDefaultItemSiblingIndices();
        ClearLiveGaugeSource();
        defaultStatusLineIndex = 0;
        ClearDefaultLines();
        SetGauge(energyGauge, energyFill, energyText, false, 0f, Color.white, 0f, 0f);
        SetGauge(workGauge, workFill, workText, false, 0f, Color.white, 0f, 0f);
        SetGauge(defaultGauge, defaultFill, defaultGaugeText, false, 0f, Color.white, 0f, 0f);

        ClearItemSlots(defaultItem, defaultItemSlot);
        ClearItemSlot(energyItem, energyItemSlot);
        ClearItemSlot(inputItem, inputItemSlot);
        ClearItemSlot(outputItem, outputItemSlot);
    }

    public void ShowResourceReserves(int reserves)
    {
        Clear();
        SetResourceReservesLine(0, reserves);
    }

    public void ShowAnimal(Animal animal)
    {
        Clear();
        if (animal == null)
        {
            return;
        }

        SetDefaultText(0, $"Gender: {animal.Gender}", true);
        SetDefaultSign(0, false, Color.white);
        SetDefaultText(
            1,
            $"Age: {animal.Age.ToString("0.#", CultureInfo.InvariantCulture)}",
            true);
        SetDefaultSign(1, false, Color.white);

        if (!animal.IsAlive)
        {
            return;
        }

        float currentHealth = animal.CurrentHealth;
        float maxHealth = animal.MaxHealth;
        SetGauge(
            defaultGauge,
            defaultFill,
            defaultGaugeText,
            true,
            maxHealth > 0f ? currentHealth / maxHealth : 0f,
            HealthGaugeFillColor,
            currentHealth,
            maxHealth,
            false,
            $"HP: {currentHealth.ToString("0.#", CultureInfo.InvariantCulture)}"
            + $"/{maxHealth.ToString("0.#", CultureInfo.InvariantCulture)}");
    }

    public void ShowConveyorBelt(ConveyorBelt conveyorBelt, Resource underlyingResource = null)
    {
        BeginObjectDisplay(underlyingResource);

        conveyorItemIds.Clear();
        int slotCount = conveyorBelt is ConvayorBelt2F ? Belt2FInfoSlotCount : DefaultConveyorInfoSlotCount;
        conveyorBelt?.CopyObjectInfoItemIds(conveyorItemIds, slotCount);

        for (int i = 0; i < slotCount; i++)
        {
            SetDefaultItemSlot(i, conveyorItemIds.Count > i ? conveyorItemIds[i] : -1, true);
        }
    }

    public void ShowPipe(Pipe pipe, Resource underlyingResource = null)
    {
        BeginObjectDisplay(underlyingResource);

        if (pipe != null && pipe.TryGetObjectInfoFluidInfo(out int fluidItemId, out float temperatureCelsius))
        {
            SetDefaultText(
                defaultStatusLineIndex,
                $"Fluid: {ResolveItemDisplayName(fluidItemId, temperatureCelsius)}",
                true);
            SetDefaultSign(defaultStatusLineIndex, false, Color.white);
            SetDefaultItemSlot(0, fluidItemId, 1, 0, true, false, false, temperatureCelsius);
            return;
        }

        SetDefaultText(defaultStatusLineIndex, "Fluid: None", true);
        SetDefaultSign(defaultStatusLineIndex, false, Color.white);
    }

    public void ShowBoxObject(BoxObject boxObject, Resource underlyingResource = null)
    {
        BeginObjectDisplay(underlyingResource);

        if (boxObject != null
            && boxObject.TryGetObjectInfoItem(out int itemId, out int itemCount, out int capacity))
        {
            SetDefaultItemSlot(0, itemId, itemCount, capacity, true, true, true);
        }
        else
        {
            SetDefaultItemSlot(0, -1, 0, 0, true, true);
        }
    }

    public void ShowRobotArm(RobotArm robotArm, Resource underlyingResource = null)
    {
        BeginObjectDisplay(underlyingResource);
        liveGaugeRobotArm = robotArm;
        if (robotArm == null)
        {
            SetDefaultItemSlot(0, -1, true);
            return;
        }

        robotArm.GetObjectInfoStatus(out string statusText, out bool isWorking);
        SetDefaultStatus(statusText, isWorking);
        TrySetElectricPowerGauge(energyGauge, energyFill, energyText, robotArm);
        bool energyUseDisplayed = robotArm.TryGetElectricPowerRequirement(out float wattsPerSecond)
            && SetEnergyUseRateDefaultItemSlot(0, ItemDefinition.EnergyType.Electricity, wattsPerSecond, -1);
        SetDefaultItemSlot(energyUseDisplayed ? 1 : 0, robotArm.HeldItemId, true);
    }

    public void ShowUtilityPole(UtilityPole utilityPole, Resource underlyingResource = null)
    {
        BeginObjectDisplay(underlyingResource);
        liveGaugeUtilityPole = utilityPole;

        float productionKilowatts = 0f;
        float requiredKilowatts = 0f;
        if (utilityPole != null
            && utilityPole.TryGetObjectInfoNetworkPower(out float productionWatts, out float requiredWatts))
        {
            productionKilowatts = productionWatts / 1000f;
            requiredKilowatts = requiredWatts / 1000f;
        }

        float fillAmount = requiredKilowatts > 0.0001f
            ? Mathf.Clamp01(productionKilowatts / requiredKilowatts)
            : (productionKilowatts > 0.0001f ? 1f : 0f);
        SetGauge(
            energyGauge,
            energyFill,
            energyText,
            true,
            fillAmount,
            ElectricGaugeFillColor,
            productionKilowatts,
            requiredKilowatts);
        if (energyText != null)
        {
            energyText.text =
                $"{FormatKilowatts(productionKilowatts)} kW / {FormatKilowatts(requiredKilowatts)} kW";
        }

        SetGauge(workGauge, workFill, workText, false, 0f, Color.white, 0f, 0f);
    }

    public void ShowLightObject(LightObject lightObject, Resource underlyingResource = null)
    {
        BeginObjectDisplay(underlyingResource);
        liveGaugeLightObject = lightObject;
        RefreshLightObjectInfo(lightObject);

        bool energyUseDisplayed = lightObject != null
            && lightObject.TryGetElectricPowerRequirement(out float wattsPerSecond)
            && SetEnergyUseRateDefaultItemSlot(
                0,
                ItemDefinition.EnergyType.Electricity,
                wattsPerSecond,
                -1);
        if (!energyUseDisplayed)
        {
            SetDefaultItemSlot(0, -1, false);
        }
    }

    public void ShowRailHandcar(RailHandcar railHandcar, Resource underlyingResource = null)
    {
        BeginObjectDisplay(underlyingResource);
        liveGaugeRailHandcar = railHandcar;
        if (railHandcar is SteamTrain steamTrain)
        {
            SetSteamTrainBurnEnergyGauge(steamTrain);
            SetSteamTrainWaterGauge(workGauge, workFill, workText, steamTrain);
            SetRailHandcarSpeedGauge(defaultGauge, defaultFill, defaultGaugeText, railHandcar);
            SetFluidStorageDefaultItemSlot(0, steamTrain);
        }
        else
        {
            SetRailHandcarSpeedGauge(railHandcar);
            SetGauge(workGauge, workFill, workText, false, 0f, Color.white, 0f, 0f);
            SetGauge(defaultGauge, defaultFill, defaultGaugeText, false, 0f, Color.white, 0f, 0f);
        }
    }

    public void ShowInstallationObject(InstallationObject installationObject, Resource underlyingResource = null)
    {
        BeginObjectDisplay(underlyingResource);
        SetFluidStorageDefaultItemSlot(0, installationObject);
    }

    public void ShowTrainstation(Trainstation trainStation, Resource underlyingResource = null)
    {
        BeginObjectDisplay(underlyingResource);
        string stationName = trainStation != null ? trainStation.StationName : string.Empty;
        SetDefaultText(defaultStatusLineIndex, $"Station: {stationName}", !string.IsNullOrWhiteSpace(stationName));
        SetDefaultSign(defaultStatusLineIndex, false, Color.white);
    }

    public void ShowInputOutputModule(InputOutputModule module, Resource underlyingResource = null)
    {
        MiningMachine miningMachine = module as MiningMachine;
        if (miningMachine != null && miningMachine.TryGetObjectInfoResourceReserves(out int miningResourceReserves))
        {
            BeginObjectDisplay(miningResourceReserves);
        }
        else
        {
            BeginObjectDisplay(underlyingResource);
        }

        liveGaugeModule = module;
        Pump pump = module as Pump;
        bool showElectricPowerGauge = TrySetElectricPowerGauge(energyGauge, energyFill, energyText, module);
        if (pump != null)
        {
            pump.GetObjectInfoStatus(out string pumpStatusText, out bool isPumpProducing);
            SetDefaultStatus(pumpStatusText, isPumpProducing);
            bool energyUseDisplayed = SetEnergyUseRateDefaultItemSlot(0, pump, -1);
            SetPumpOutputRateDefaultItemSlot(energyUseDisplayed ? 1 : 0, pump);
            return;
        }

        SteamGenerator steamGenerator = module as SteamGenerator;
        Boiler boiler = module as Boiler;
        if (steamGenerator != null && module.CanStoreFluid)
        {
            if (showElectricPowerGauge)
            {
                SetFluidStorageGauge(workGauge, workFill, workText, module);
                SetGauge(defaultGauge, defaultFill, defaultGaugeText, false, 0f, Color.white, 0f, 0f);
            }
            else
            {
                SetFluidStorageGauge(module);
                SetGauge(workGauge, workFill, workText, false, 0f, Color.white, 0f, 0f);
            }
        }
        else if (boiler != null && module.CanStoreFluid)
        {
            if (showElectricPowerGauge)
            {
                SetFluidStorageGauge(workGauge, workFill, workText, module);
                SetBoilerTemperatureGauge(defaultGauge, defaultFill, defaultGaugeText, boiler);
            }
            else
            {
                SetFluidStorageGauge(module);
                SetBoilerTemperatureGauge(workGauge, workFill, workText, boiler);
            }
        }
        else
        {
            if (showElectricPowerGauge)
            {
                SetWorkProgressGauge(workGauge, workFill, workText, module);
                SetGauge(defaultGauge, defaultFill, defaultGaugeText, false, 0f, Color.white, 0f, 0f);
            }
            else
            {
                bool showEnergyGauge = !(module is ProductionMachine);
                SetGauge(
                    energyGauge,
                    energyFill,
                    energyText,
                    showEnergyGauge,
                    module != null ? module.ObjectInfoEnergyGaugeFillAmount : 0f,
                    module != null ? module.ObjectInfoEnergyGaugeFillColor : Color.white,
                    module != null ? module.ObjectInfoStoredEnergy : 0f,
                    module != null ? module.ObjectInfoEnergyGaugeCapacity : 0f,
                    true);

                SetWorkProgressGauge(workGauge, workFill, workText, module);
            }
        }

        if (module == null)
        {
            return;
        }

        module.GetObjectInfoStatus(out string statusText, out bool isProducing);
        SetDefaultStatus(statusText, isProducing);

        int energyInputItemId = -1;
        if (module.TryGetObjectInfoEnergyInput(
            out int energyItemId,
            out int energyAreaCount,
            out int energyAreaCapacity))
        {
            energyInputItemId = energyItemId;
            if (module.TryGetObjectInfoBurnEnergyInput(
                    out int burnEnergyAmount,
                    out int burnEnergyAreaCapacity))
            {
                SetBurnEnergyInputItemSlot(
                    energyItem,
                    energyItemSlot,
                    energyItemId,
                    energyAreaCount,
                    burnEnergyAmount,
                    burnEnergyAreaCapacity);
            }
            else
            {
                SetItemSlot(
                    energyItem,
                    energyItemSlot,
                    energyItemId,
                    energyAreaCount,
                    energyAreaCapacity,
                    true,
                    false,
                    ResolveModuleFluidTemperature(module, energyItemId));
            }
        }

        if (steamGenerator != null)
        {
            SetFluidStorageInputItemSlot(steamGenerator);
            if (!TrySetSteamGeneratorOutputRateItemSlot(outputItem, outputItemSlot, steamGenerator)
                && module.TryGetObjectInfoOutput(
                    out int steamOutputItemId,
                    out int steamOutputAreaCount,
                    out int steamOutputAreaCapacity,
                    out bool displayZeroSteamOutputItem))
            {
                SetItemSlot(
                    outputItem,
                    outputItemSlot,
                    steamOutputItemId,
                    steamOutputAreaCount,
                    steamOutputAreaCapacity,
                    true,
                    displayZeroSteamOutputItem,
                    ResolveModuleFluidTemperature(module, steamOutputItemId));
            }

            return;
        }

        bool energyUseRateDisplayed = SetEnergyUseRateDefaultItemSlot(0, module, energyInputItemId);
        SetFluidStorageDefaultItemSlot(energyUseRateDisplayed ? 1 : 0, module);

        ProductionMachine productionMachine = module as ProductionMachine;
        if (productionMachine != null
            && TrySetProductionMachineItemSlots(
                productionMachine,
                energyUseRateDisplayed ? 1 : 0))
        {
            return;
        }

        if (module.TryGetObjectInfoItemPair(
                out int inputItemId,
                out int inputAreaCount,
                out int inputAreaCapacity,
                out int outputItemId,
                out int outputAreaCount,
                out int outputAreaCapacity))
        {
            SetItemSlot(
                inputItem,
                inputItemSlot,
                inputItemId,
                inputAreaCount,
                inputAreaCapacity,
                true,
                true,
                ResolveModuleFluidTemperature(module, inputItemId));
            if (!TrySetSteamGeneratorOutputRateItemSlot(outputItem, outputItemSlot, steamGenerator)
                && !TrySetBoilerOutputRateItemSlot(outputItem, outputItemSlot, boiler))
            {
                SetItemSlot(
                    outputItem,
                    outputItemSlot,
                    outputItemId,
                    outputAreaCount,
                    outputAreaCapacity,
                    true,
                    true,
                    ResolveModuleFluidTemperature(module, outputItemId));
            }

            return;
        }

        if (module.TryGetObjectInfoOutput(
                out outputItemId,
                out outputAreaCount,
                out outputAreaCapacity,
                out bool displayZeroOutputItem))
        {
            if (!TrySetSteamGeneratorOutputRateItemSlot(outputItem, outputItemSlot, steamGenerator)
                && !TrySetBoilerOutputRateItemSlot(outputItem, outputItemSlot, boiler))
            {
                SetItemSlot(
                    outputItem,
                    outputItemSlot,
                    outputItemId,
                    outputAreaCount,
                    outputAreaCapacity,
                    true,
                    displayZeroOutputItem,
                    ResolveModuleFluidTemperature(module, outputItemId));
            }
        }
    }

    private bool TrySetProductionMachineItemSlots(ProductionMachine productionMachine, int defaultItemStartIndex)
    {
        if (productionMachine == null
            || !productionMachine.TryGetObjectInfoProductionIngredientCount(out int ingredientCount))
        {
            return false;
        }

        bool displayedAny = false;
        int nextDefaultItemIndex = Mathf.Max(0, defaultItemStartIndex);
        for (int i = 0; i < ingredientCount; i++)
        {
            if (!productionMachine.TryGetObjectInfoProductionIngredient(
                    i,
                    out int ingredientItemId,
                    out int ingredientRequiredCount,
                    out int ingredientAreaCount,
                    out int ingredientAreaCapacity))
            {
                continue;
            }

            if (i == 0)
            {
                SetProductionIngredientItemSlot(
                    inputItem,
                    inputItemSlot,
                    ingredientItemId,
                    ingredientRequiredCount,
                    ingredientAreaCount,
                    ingredientAreaCapacity,
                    ResolveModuleFluidTemperature(productionMachine, ingredientItemId));
            }
            else
            {
                MoveDefaultItemBelowInputItem(nextDefaultItemIndex, i);
                SetProductionIngredientDefaultItemSlot(
                    nextDefaultItemIndex,
                    ingredientItemId,
                    ingredientRequiredCount,
                    ingredientAreaCount,
                    ingredientAreaCapacity,
                    ResolveModuleFluidTemperature(productionMachine, ingredientItemId));
                nextDefaultItemIndex++;
            }

            displayedAny = true;
        }

        if (productionMachine.TryGetObjectInfoProductionOutput(
                out int outputItemId,
                out int outputAreaCount,
                out int outputAreaCapacity))
        {
            SetItemSlot(
                outputItem,
                outputItemSlot,
                outputItemId,
                outputAreaCount,
                outputAreaCapacity,
                true,
                true,
                ResolveModuleFluidTemperature(productionMachine, outputItemId));
            displayedAny = true;
        }

        return displayedAny;
    }

    private void SetDefaultStatus(string text, bool isProducing)
    {
        SetDefaultText(defaultStatusLineIndex, text, !string.IsNullOrEmpty(text));
        SetDefaultSign(defaultStatusLineIndex, !string.IsNullOrEmpty(text), isProducing ? ProducingSignColor : StoppedSignColor);
    }

    private void BeginObjectDisplay(Resource underlyingResource)
    {
        if (!IsDisplayableUnderlyingResource(underlyingResource))
        {
            BeginObjectDisplay(-1);
            return;
        }

        BeginObjectDisplay(underlyingResource.RemainingHarvestOutputCount);
    }

    private void BeginObjectDisplay(int resourceReserves)
    {
        Clear();
        if (resourceReserves < 0)
        {
            return;
        }

        SetResourceReservesLine(0, resourceReserves);
        defaultStatusLineIndex = 1;
    }

    private void ClearLiveGaugeSource()
    {
        liveGaugeRobotArm = null;
        liveGaugeUtilityPole = null;
        liveGaugeLightObject = null;
        liveGaugeModule = null;
        liveGaugeRailHandcar = null;
    }

    private void RefreshLiveGaugeTargets()
    {
        if (!Application.isPlaying || !gameObject.activeInHierarchy)
        {
            return;
        }

        if (liveGaugeModule != null && liveGaugeModule.gameObject.activeInHierarchy)
        {
            RefreshInputOutputModuleGaugeTargets(liveGaugeModule);
            return;
        }

        if (liveGaugeUtilityPole != null && liveGaugeUtilityPole.gameObject.activeInHierarchy)
        {
            RefreshUtilityPoleGaugeTarget(liveGaugeUtilityPole);
            return;
        }

        if (liveGaugeLightObject != null && liveGaugeLightObject.gameObject.activeInHierarchy)
        {
            RefreshLightObjectInfo(liveGaugeLightObject);
            return;
        }

        if (liveGaugeRailHandcar != null && liveGaugeRailHandcar.gameObject.activeInHierarchy)
        {
            if (liveGaugeRailHandcar is SteamTrain steamTrain)
            {
                SetSteamTrainBurnEnergyGauge(steamTrain);
                SetSteamTrainWaterGauge(workGauge, workFill, workText, steamTrain);
                SetRailHandcarSpeedGauge(defaultGauge, defaultFill, defaultGaugeText, liveGaugeRailHandcar);
                SetFluidStorageDefaultItemSlot(0, steamTrain);
            }
            else
            {
                SetRailHandcarSpeedGauge(liveGaugeRailHandcar);
                SetGauge(defaultGauge, defaultFill, defaultGaugeText, false, 0f, Color.white, 0f, 0f);
            }

            return;
        }

        if (liveGaugeRobotArm != null && liveGaugeRobotArm.gameObject.activeInHierarchy)
        {
            TrySetElectricPowerGauge(energyGauge, energyFill, energyText, liveGaugeRobotArm);
        }
    }

    private void RefreshLightObjectInfo(LightObject lightObject)
    {
        if (lightObject == null)
        {
            return;
        }

        lightObject.GetObjectInfoStatus(out string statusText, out bool isLit);
        SetDefaultStatus(statusText, isLit);
        TrySetElectricPowerGauge(energyGauge, energyFill, energyText, lightObject);
    }

    private void RefreshInputOutputModuleGaugeTargets(InputOutputModule module)
    {
        if (module == null)
        {
            return;
        }

        bool showElectricPowerGauge = TrySetElectricPowerGauge(energyGauge, energyFill, energyText, module);
        if (module is Pump)
        {
            return;
        }

        SteamGenerator steamGenerator = module as SteamGenerator;
        Boiler boiler = module as Boiler;
        if (steamGenerator != null && module.CanStoreFluid)
        {
            if (showElectricPowerGauge)
            {
                SetFluidStorageGauge(workGauge, workFill, workText, module);
                SetGauge(defaultGauge, defaultFill, defaultGaugeText, false, 0f, Color.white, 0f, 0f);
            }
            else
            {
                SetFluidStorageGauge(module);
                SetGauge(workGauge, workFill, workText, false, 0f, Color.white, 0f, 0f);
            }

            return;
        }

        if (boiler != null && module.CanStoreFluid)
        {
            if (showElectricPowerGauge)
            {
                SetFluidStorageGauge(workGauge, workFill, workText, module);
                SetBoilerTemperatureGauge(defaultGauge, defaultFill, defaultGaugeText, boiler);
            }
            else
            {
                SetFluidStorageGauge(module);
                SetBoilerTemperatureGauge(workGauge, workFill, workText, boiler);
            }

            return;
        }

        if (showElectricPowerGauge)
        {
            SetWorkProgressGauge(workGauge, workFill, workText, module);
            SetGauge(defaultGauge, defaultFill, defaultGaugeText, false, 0f, Color.white, 0f, 0f);
            return;
        }

        bool showEnergyGauge = !(module is ProductionMachine);
        SetGauge(
            energyGauge,
            energyFill,
            energyText,
            showEnergyGauge,
            module.ObjectInfoEnergyGaugeFillAmount,
            module.ObjectInfoEnergyGaugeFillColor,
            module.ObjectInfoStoredEnergy,
            module.ObjectInfoEnergyGaugeCapacity,
            true);

        SetWorkProgressGauge(workGauge, workFill, workText, module);
    }

    private void SetRailHandcarSpeedGauge(RailHandcar railHandcar)
    {
        SetRailHandcarSpeedGauge(energyGauge, energyFill, energyText, railHandcar);
    }

    private void SetRailHandcarSpeedGauge(
        GameObject root,
        Image fill,
        TextMeshProUGUI text,
        RailHandcar railHandcar)
    {
        float currentSpeed = railHandcar != null ? railHandcar.CurrentVehicleSpeed : 0f;
        float maxSpeed = railHandcar != null ? railHandcar.EffectiveVehicleMaxSpeed : 0f;
        float fillAmount = maxSpeed > 0.0001f
            ? Mathf.Clamp01(currentSpeed / maxSpeed)
            : 0f;

        SetGauge(
            root,
            fill,
            text,
            true,
            fillAmount,
            ElectricGaugeFillColor,
            currentSpeed,
            maxSpeed,
            true,
            $"{FormatMetersPerSecond(currentSpeed)} m/s / {FormatMetersPerSecond(maxSpeed)} m/s");
    }

    private void SetSteamTrainBurnEnergyGauge(SteamTrain steamTrain)
    {
        float storedEnergy = steamTrain != null ? steamTrain.ObjectInfoStoredBurnEnergy : 0f;
        float gaugeCapacity = steamTrain != null ? steamTrain.ObjectInfoBurnEnergyGaugeCapacity : 0f;
        SetGauge(
            energyGauge,
            energyFill,
            energyText,
            true,
            steamTrain != null ? steamTrain.ObjectInfoBurnEnergyGaugeFillAmount : 0f,
            BurnEnergyGaugeFillColor,
            storedEnergy,
            gaugeCapacity,
            true,
            steamTrain != null
                ? $"{FormatGaugeNumber(storedEnergy, true)} / {FormatGaugeNumber(gaugeCapacity, true)} ({FormatGaugeNumber(steamTrain.ObjectInfoBurnEnergyUseRatePerSecond, true)} / s)"
                : string.Empty);
    }

    private void SetSteamTrainWaterGauge(
        GameObject root,
        Image fill,
        TextMeshProUGUI text,
        SteamTrain steamTrain)
    {
        float storedLiters = steamTrain != null ? steamTrain.ObjectInfoStoredWaterLiters : 0f;
        float capacityLiters = steamTrain != null ? steamTrain.ObjectInfoWaterCapacityLiters : 0f;
        SetGauge(
            root,
            fill,
            text,
            true,
            steamTrain != null ? steamTrain.ObjectInfoWaterGaugeFillAmount : 0f,
            ResolveFluidGaugeFillColor(steamTrain),
            storedLiters,
            capacityLiters,
            true,
            steamTrain != null
                ? $"{FormatGaugeNumber(storedLiters, true)} L / {FormatGaugeNumber(capacityLiters, true)} L ({FormatLitersPerSecond(steamTrain.ObjectInfoWaterUseRatePerSecond)})"
                : string.Empty);
    }

    private void RefreshUtilityPoleGaugeTarget(UtilityPole utilityPole)
    {
        float productionKilowatts = 0f;
        float requiredKilowatts = 0f;
        if (utilityPole != null
            && utilityPole.TryGetObjectInfoNetworkPower(out float productionWatts, out float requiredWatts))
        {
            productionKilowatts = productionWatts / 1000f;
            requiredKilowatts = requiredWatts / 1000f;
        }

        float fillAmount = requiredKilowatts > 0.0001f
            ? Mathf.Clamp01(productionKilowatts / requiredKilowatts)
            : (productionKilowatts > 0.0001f ? 1f : 0f);
        SetGauge(
            energyGauge,
            energyFill,
            energyText,
            true,
            fillAmount,
            ElectricGaugeFillColor,
            productionKilowatts,
            requiredKilowatts);
        if (energyText != null)
        {
            energyText.text =
                $"{FormatKilowatts(productionKilowatts)} kW / {FormatKilowatts(requiredKilowatts)} kW";
        }
    }

    private void SetResourceReservesLine(int index, int reserves)
    {
        SetDefaultText(index, $"Reserves: {Mathf.Max(0, reserves)}", true);
        SetDefaultSign(index, false, Color.white);
    }

    private void SetPumpOutputRateDefaultItemSlot(int index, Pump pump)
    {
        GameObject root = defaultItem != null && index >= 0 && index < defaultItem.Count ? defaultItem[index] : null;
        ItemSlot slot = defaultItemSlot != null && index >= 0 && index < defaultItemSlot.Count ? defaultItemSlot[index] : null;
        SetActiveIfNeeded(root, true);
        if (slot == null)
        {
            return;
        }

        int outputItemId = -1;
        float litersPerSecond = 0f;
        if (pump != null)
        {
            pump.TryGetObjectInfoOutputRate(out outputItemId, out litersPerSecond);
        }

        ItemManager.ItemSet fluidItemSet = ResolveFluidItemSet(outputItemId);
        string displayName = ResolveFluidDisplayName(
            string.IsNullOrWhiteSpace(fluidItemSet.name) ? DefaultFluidItemName : fluidItemSet.name,
            pump != null
                ? pump.GetStoredFluidTemperatureCelsius(outputItemId)
                : MapClimate.CurrentWaterTemperatureCelsius);
        slot.SetCustomDisplay(
            outputItemId,
            fluidItemSet.icon,
            displayName,
            FormatLitersPerSecond(litersPerSecond));
    }

    private bool TrySetBoilerOutputRateItemSlot(GameObject root, ItemSlot slot, Boiler boiler)
    {
        if (boiler == null
            || !boiler.TryGetObjectInfoOutputRate(out int outputItemId, out float litersPerSecond))
        {
            return false;
        }

        SetActiveIfNeeded(root, true);
        if (slot == null)
        {
            return true;
        }

        ItemManager.ItemSet fluidItemSet = ResolveFluidItemSet(outputItemId);
        string displayName = ResolveFluidDisplayName(
            string.IsNullOrWhiteSpace(fluidItemSet.name) ? ResolveItemDisplayName(outputItemId) : fluidItemSet.name,
            boiler.GetStoredFluidTemperatureCelsius(outputItemId));
        slot.SetCustomDisplay(
            outputItemId,
            fluidItemSet.icon,
            displayName,
            FormatLitersPerSecond(litersPerSecond));
        return true;
    }

    private bool TrySetSteamGeneratorOutputRateItemSlot(GameObject root, ItemSlot slot, SteamGenerator steamGenerator)
    {
        if (steamGenerator == null
            || !steamGenerator.TryGetObjectInfoOutputRate(out int outputItemId, out float wattsPerSecond))
        {
            return false;
        }

        SetActiveIfNeeded(root, true);
        if (slot == null)
        {
            return true;
        }

        ItemManager.ItemSet itemSet = ResolveItemSet(outputItemId);
        string displayName = string.IsNullOrWhiteSpace(itemSet.name)
            ? ResolveItemDisplayName(outputItemId)
            : itemSet.name;
        float kilowatts = wattsPerSecond / 1000f;
        slot.SetCustomDisplay(
            outputItemId,
            itemSet.icon,
            displayName,
            $"{FormatKilowatts(kilowatts)} kW");
        return true;
    }

    private void SetFluidStorageDefaultItemSlot(int index, InstallationObject installationObject)
    {
        if (installationObject == null || !installationObject.CanStoreFluid)
        {
            return;
        }

        GameObject root = defaultItem != null && index >= 0 && index < defaultItem.Count ? defaultItem[index] : null;
        ItemSlot slot = defaultItemSlot != null && index >= 0 && index < defaultItemSlot.Count ? defaultItemSlot[index] : null;
        SetFluidStorageItemSlot(root, slot, installationObject);
    }

    private void SetFluidStorageInputItemSlot(InstallationObject installationObject)
    {
        SetFluidStorageItemSlot(inputItem, inputItemSlot, installationObject);
    }

    private void SetFluidStorageItemSlot(GameObject root, ItemSlot slot, InstallationObject installationObject)
    {
        if (installationObject == null || !installationObject.CanStoreFluid)
        {
            return;
        }

        SetActiveIfNeeded(root, true);
        if (slot == null)
        {
            return;
        }

        if (installationObject is SteamGenerator && installationObject.StoredFluidItemId < 0)
        {
            slot.SetCustomDisplay(-1, null, string.Empty, string.Empty);
            return;
        }

        float storedLiters = installationObject.StoredFluidLiters;
        float capacityLiters = installationObject.FluidStorageCapacityLiters;
        int fluidItemId = installationObject.StoredFluidItemId;
        if (fluidItemId < 0 && installationObject is SteamTrain steamTrain)
        {
            fluidItemId = steamTrain.ObjectInfoWaterItemId;
        }

        ItemManager.ItemSet fluidItemSet = ResolveFluidItemSet(fluidItemId);
        string displayName = ResolveFluidStorageDisplayName(installationObject, fluidItemSet);
        slot.SetCustomDisplay(
            fluidItemSet.id,
            fluidItemSet.icon,
            displayName,
            $"{FormatGaugeNumber(storedLiters, true)} / {FormatGaugeNumber(capacityLiters, true)} L");
    }

    private bool SetEnergyUseRateDefaultItemSlot(
        int index,
        InputOutputModule module,
        int preferredEnergyItemId)
    {
        if (module == null
            || !module.TryGetObjectInfoEnergyUseRate(
                out ItemDefinition.EnergyType energyType,
                out float amountPerSecond))
        {
            return false;
        }

        return SetEnergyUseRateDefaultItemSlot(index, energyType, amountPerSecond, preferredEnergyItemId);
    }

    private bool SetEnergyUseRateDefaultItemSlot(
        int index,
        ItemDefinition.EnergyType energyType,
        float amountPerSecond,
        int preferredEnergyItemId)
    {
        if (energyType == ItemDefinition.EnergyType.None || amountPerSecond <= 0.0001f)
        {
            return false;
        }

        GameObject root = defaultItem != null && index >= 0 && index < defaultItem.Count ? defaultItem[index] : null;
        ItemSlot slot = defaultItemSlot != null && index >= 0 && index < defaultItemSlot.Count ? defaultItemSlot[index] : null;
        SetActiveIfNeeded(root, true);
        if (slot == null)
        {
            return root != null;
        }

        int displayItemId = ResolveEnergyUseDisplayItemId(energyType, preferredEnergyItemId);
        ItemManager.ItemSet itemSet = ResolveItemSet(displayItemId);
        string displayName = energyType == ItemDefinition.EnergyType.Electricity
            && displayItemId >= 0
            && !string.IsNullOrWhiteSpace(itemSet.name)
                ? itemSet.name
                : ResolveEnergyTypeDisplayName(energyType);
        slot.SetCustomDisplay(
            displayItemId,
            itemSet.icon,
            displayName,
            FormatEnergyUseRate(energyType, amountPerSecond));
        return true;
    }

    private bool TrySetElectricPowerGauge(
        GameObject root,
        Image fill,
        TextMeshProUGUI text,
        InstallationObject consumer)
    {
        if (consumer == null
            || !UtilityPole.TryGetElectricPowerInfo(
                consumer,
                out float suppliedWatts,
                out float requiredWatts))
        {
            return false;
        }

        float suppliedKilowatts = suppliedWatts / 1000f;
        float requiredKilowatts = requiredWatts / 1000f;
        float fillAmount = requiredKilowatts > 0.0001f
            ? Mathf.Clamp01(suppliedKilowatts / requiredKilowatts)
            : 0f;
        SetGauge(
            root,
            fill,
            text,
            true,
            fillAmount,
            ElectricGaugeFillColor,
            suppliedKilowatts,
            requiredKilowatts);
        if (text != null)
        {
            text.text = $"{FormatKilowatts(suppliedKilowatts)} kW / {FormatKilowatts(requiredKilowatts)} kW";
        }

        return true;
    }

    private void SetFluidStorageGauge(InstallationObject installationObject)
    {
        SetFluidStorageGauge(energyGauge, energyFill, energyText, installationObject);
    }

    private void SetFluidStorageGauge(
        GameObject root,
        Image fill,
        TextMeshProUGUI text,
        InstallationObject installationObject)
    {
        float capacityLiters = installationObject != null ? installationObject.FluidStorageCapacityLiters : 0f;
        float storedLiters = installationObject != null ? installationObject.StoredFluidLiters : 0f;
        SetGauge(
            root,
            fill,
            text,
            true,
            capacityLiters > 0.0001f ? storedLiters / capacityLiters : 0f,
            ResolveFluidGaugeFillColor(installationObject),
            storedLiters,
            capacityLiters,
            true);
    }

    private void SetBoilerTemperatureGauge(
        GameObject root,
        Image fill,
        TextMeshProUGUI text,
        Boiler boiler)
    {
        SetGauge(
            root,
            fill,
            text,
            true,
            boiler != null ? boiler.ObjectInfoBoilerTemperatureFillAmount : 0f,
            boiler != null ? boiler.ObjectInfoBoilerTemperatureGaugeFillColor : Color.white,
            boiler != null ? boiler.WaterTemperatureCelsius : 0f,
            boiler != null ? boiler.MaxWaterTemperatureCelsius : 0f,
            true);
    }

    private void SetWorkProgressGauge(
        GameObject root,
        Image fill,
        TextMeshProUGUI text,
        InputOutputModule module)
    {
        float currentValue = module != null ? module.ObjectInfoCurrentUseEnergy : 0f;
        float completeValue = module != null ? module.ObjectInfoCompleteEnergy : 0f;
        bool showKilowatts = module != null
            && module.TryGetObjectInfoEnergyUseRate(out ItemDefinition.EnergyType energyType, out _)
            && energyType == ItemDefinition.EnergyType.Electricity;
        if (showKilowatts)
        {
            currentValue /= 1000f;
            completeValue /= 1000f;
        }

        SetGauge(
            root,
            fill,
            text,
            true,
            module != null ? module.ObjectInfoWorkGaugeFillAmount : 0f,
            module != null ? module.ObjectInfoWorkGaugeFillColor : Color.white,
            currentValue,
            completeValue,
            true);
        if (showKilowatts && text != null)
        {
            text.text = $"{FormatKilowatts(currentValue)} kW / {FormatKilowatts(completeValue)} kW";
        }
    }

    private static bool IsDisplayableUnderlyingResource(Resource resource)
    {
        return resource != null && resource.CanHarvest;
    }

    private void SetDefaultItemSlot(int index, int itemId, bool forceRootActive)
    {
        SetDefaultItemSlot(index, itemId, 1, 0, forceRootActive, false, false, null);
    }

    private void SetDefaultItemSlot(int index, int itemId, int count, int maxCount, bool forceRootActive, bool showCount)
    {
        SetDefaultItemSlot(index, itemId, count, maxCount, forceRootActive, showCount, false, null);
    }

    private void SetDefaultItemSlot(
        int index,
        int itemId,
        int count,
        int maxCount,
        bool forceRootActive,
        bool showCount,
        bool showEmptyCount)
    {
        SetDefaultItemSlot(index, itemId, count, maxCount, forceRootActive, showCount, showEmptyCount, null);
    }

    private void SetDefaultItemSlot(
        int index,
        int itemId,
        int count,
        int maxCount,
        bool forceRootActive,
        bool showCount,
        bool showEmptyCount,
        float? fluidTemperatureCelsius)
    {
        GameObject root = defaultItem != null && index >= 0 && index < defaultItem.Count ? defaultItem[index] : null;
        ItemSlot slot = defaultItemSlot != null && index >= 0 && index < defaultItemSlot.Count ? defaultItemSlot[index] : null;

        SetActiveIfNeeded(root, forceRootActive || itemId >= 0 || showEmptyCount);
        if (slot == null)
        {
            return;
        }

        if (itemId >= 0)
        {
            if (InputOutputModule.IsFluidItemId(itemId))
            {
                SetFluidItemSlotDisplay(
                    slot,
                    itemId,
                    Mathf.Max(0, count),
                    Mathf.Max(0, maxCount),
                    true,
                    showCount,
                    fluidTemperatureCelsius);
            }
            else
            {
                slot.SetItemDisplay(itemId, Mathf.Max(0, count), Mathf.Max(0, maxCount), true, showCount);
            }
        }
        else
        {
            if (showEmptyCount)
            {
                slot.SetEmptyCountDisplay(Mathf.Max(0, count), Mathf.Max(0, maxCount));
            }
            else
            {
                slot.Clear();
            }
        }
    }

    private void SetProductionIngredientDefaultItemSlot(
        int index,
        int itemId,
        int requiredCount,
        int count,
        int maxCount,
        float? fluidTemperatureCelsius)
    {
        GameObject root = defaultItem != null && index >= 0 && index < defaultItem.Count ? defaultItem[index] : null;
        ItemSlot slot = defaultItemSlot != null && index >= 0 && index < defaultItemSlot.Count ? defaultItemSlot[index] : null;
        SetProductionIngredientItemSlot(root, slot, itemId, requiredCount, count, maxCount, fluidTemperatureCelsius);
    }

    private void SetProductionIngredientItemSlot(
        GameObject root,
        ItemSlot slot,
        int itemId,
        int requiredCount,
        int count,
        int maxCount,
        float? fluidTemperatureCelsius)
    {
        SetActiveIfNeeded(root, true);
        if (slot == null)
        {
            return;
        }

        if (itemId < 0)
        {
            slot.SetEmptyCountDisplay(Mathf.Max(0, count), Mathf.Max(1, maxCount));
            return;
        }

        int displayCount = Mathf.Max(0, count);
        int displayMaxCount = Mathf.Max(1, maxCount, displayCount);
        int displayRequiredCount = Mathf.Max(1, requiredCount);
        ItemManager.ItemSet itemSet = ResolveItemSet(itemId);
        string displayName = string.IsNullOrWhiteSpace(itemSet.name)
            ? ResolveItemDisplayName(itemId)
            : itemSet.name;
        if (InputOutputModule.IsFluidItemId(itemId))
        {
            displayName = ResolveFluidDisplayName(
                displayName,
                fluidTemperatureCelsius ?? MapClimate.CurrentTemperatureCelsius);
        }

        slot.SetCustomDisplay(
            itemId,
            itemSet.icon,
            $"{displayName} [{displayRequiredCount}]",
            $"{displayCount} / {displayMaxCount}");
    }

    private void SetDefaultText(string text, bool visible)
    {
        SetDefaultText(0, text, visible);
    }

    private void SetDefaultText(int index, string text, bool visible)
    {
        TextMeshProUGUI targetText = GetListItem(defaultText, index);
        if (targetText != null)
        {
            targetText.text = visible ? text : string.Empty;
            SetActiveIfNeeded(targetText.gameObject, visible);
        }

        RefreshDefaultParentActive(index);
    }

    private void SetDefaultSign(bool visible, Color color)
    {
        SetDefaultSign(0, visible, color);
    }

    private void SetDefaultSign(int index, bool visible, Color color)
    {
        Image targetSign = GetListItem(defaultSign, index);
        if (targetSign != null)
        {
            targetSign.color = color;
            SetActiveIfNeeded(targetSign.gameObject, visible);
        }

        RefreshDefaultParentActive(index);
    }

    private void ClearDefaultLines()
    {
        int count = Mathf.Max(
            defaultParent != null ? defaultParent.Count : 0,
            defaultText != null ? defaultText.Count : 0,
            defaultSign != null ? defaultSign.Count : 0);

        for (int i = 0; i < count; i++)
        {
            TextMeshProUGUI text = GetListItem(defaultText, i);
            if (text != null)
            {
                text.text = string.Empty;
                SetActiveIfNeeded(text.gameObject, false);
            }

            Image sign = GetListItem(defaultSign, i);
            if (sign != null)
            {
                sign.color = Color.white;
                SetActiveIfNeeded(sign.gameObject, false);
            }

            SetActiveIfNeeded(GetListItem(defaultParent, i), false);
        }
    }

    private void RefreshDefaultParentActive(int index)
    {
        TextMeshProUGUI targetText = GetListItem(defaultText, index);
        Image targetSign = GetListItem(defaultSign, index);
        bool active = (targetText != null && targetText.gameObject.activeSelf)
            || (targetSign != null && targetSign.gameObject.activeSelf);
        SetActiveIfNeeded(GetListItem(defaultParent, index), active);
    }

    private static void ClearItemSlot(GameObject root, ItemSlot slot)
    {
        if (slot != null)
        {
            slot.Clear();
        }

        SetActiveIfNeeded(root, false);
    }

    private static void SetItemSlot(
        GameObject root,
        ItemSlot slot,
        int itemId,
        int count,
        int maxCount,
        bool forceRootActive = false,
        bool displayZeroCount = false,
        float? fluidTemperatureCelsius = null)
    {
        bool hasItem = itemId >= 0 && (displayZeroCount || count > 0);
        SetActiveIfNeeded(root, forceRootActive || hasItem);

        if (slot == null)
        {
            return;
        }

        if (hasItem)
        {
            int displayCount = Mathf.Max(0, count);
            int displayMaxCount = Mathf.Max(1, maxCount, displayCount);
            if (InputOutputModule.IsFluidItemId(itemId))
            {
                SetFluidItemSlotDisplay(
                    slot,
                    itemId,
                    displayCount,
                    displayMaxCount,
                    displayZeroCount,
                    true,
                    fluidTemperatureCelsius);
            }
            else
            {
                slot.SetItemDisplay(itemId, displayCount, displayMaxCount, displayZeroCount);
            }
        }
        else
        {
            slot.Clear();
        }
    }

    private void SetBurnEnergyInputItemSlot(
        GameObject root,
        ItemSlot slot,
        int energyItemId,
        int energyItemCount,
        int burnEnergyAmount,
        int energyAreaCapacity)
    {
        SetActiveIfNeeded(root, true);
        if (slot == null)
        {
            return;
        }

        ItemManager.ItemSet itemSet = ResolveItemSet(energyItemId);
        string displayName = energyItemId >= 0
            ? (string.IsNullOrWhiteSpace(itemSet.name) ? ResolveItemDisplayName(energyItemId) : itemSet.name)
            : string.Empty;
        slot.SetCustomDisplay(
            energyItemId,
            itemSet.icon,
            displayName,
            $"{Mathf.Max(0, energyItemCount)}[{Mathf.Max(0, burnEnergyAmount)}] / {Mathf.Max(1, energyAreaCapacity)}");
    }

    private static void SetFluidItemSlotDisplay(
        ItemSlot slot,
        int itemId,
        int count,
        int maxCount,
        bool allowZeroCount,
        bool showCount,
        float? fluidTemperatureCelsius)
    {
        if (slot == null)
        {
            return;
        }

        ItemManager.ItemSet itemSet = ResolveItemSet(itemId);
        string countText = showCount
            ? (maxCount > 0 ? $"{Mathf.Max(0, count)} / {Mathf.Max(1, maxCount)}" : Mathf.Max(0, count).ToString())
            : string.Empty;
        slot.SetCustomDisplay(
            itemId,
            itemSet.icon,
            ResolveFluidDisplayName(
                string.IsNullOrWhiteSpace(itemSet.name) ? ResolveItemDisplayName(itemId) : itemSet.name,
                fluidTemperatureCelsius ?? MapClimate.CurrentTemperatureCelsius),
            countText);
    }

    private static void ClearItemSlots(List<GameObject> roots, List<ItemSlot> slots)
    {
        if (slots != null)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                ItemSlot slot = slots[i];
                if (slot != null)
                {
                    slot.Clear();
                }
            }
        }

        if (roots == null)
        {
            return;
        }

        for (int i = 0; i < roots.Count; i++)
        {
            SetActiveIfNeeded(roots[i], false);
        }
    }

    private static ItemManager.ItemSet ResolveFluidItemSet()
    {
        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        if (itemManager != null && TryResolveItemSetByName(itemManager, DefaultFluidItemName, out ItemManager.ItemSet itemSet))
        {
            return itemSet;
        }

        return new ItemManager.ItemSet
        {
            id = -1,
            name = DefaultFluidItemName
        };
    }

    private static ItemManager.ItemSet ResolveFluidItemSet(int fluidItemId)
    {
        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        if (fluidItemId >= 0
            && itemManager != null
            && itemManager.TryGetItemSetById(fluidItemId, out ItemManager.ItemSet itemSet))
        {
            return itemSet;
        }

        return ResolveFluidItemSet();
    }

    private static Color ResolveFluidGaugeFillColor(InstallationObject installationObject)
    {
        int fluidItemId = ResolveFluidGaugeItemId(installationObject);
        ItemDefinition definition = InputOutputModule.ResolveItemDefinition(fluidItemId);
        if (InputOutputModule.IsFluidItemDefinition(definition))
        {
            Color color = definition.fluidDisplayColor;
            color.a = color.a > 0f ? color.a : 1f;
            return color;
        }

        return FluidGaugeFillColor;
    }

    private static int ResolveFluidGaugeItemId(InstallationObject installationObject)
    {
        if (installationObject == null)
        {
            return -1;
        }

        int storedFluidItemId = installationObject.StoredFluidItemId;
        if (storedFluidItemId >= 0)
        {
            return storedFluidItemId;
        }

        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        if (installationObject is SteamGenerator
            && TryResolveItemSetByName(itemManager, SteamFluidItemName, out ItemManager.ItemSet steamItemSet))
        {
            return steamItemSet.id;
        }

        if (installationObject is SteamTrain steamTrain)
        {
            return steamTrain.ObjectInfoWaterItemId;
        }

        return -1;
    }

    private static string ResolveFluidStorageDisplayName(
        InstallationObject installationObject,
        ItemManager.ItemSet fluidItemSet)
    {
        string displayName = string.IsNullOrWhiteSpace(fluidItemSet.name)
            ? DefaultFluidItemName
            : fluidItemSet.name;
        float temperatureCelsius = installationObject != null
            ? installationObject.GetStoredFluidTemperatureCelsius(fluidItemSet.id)
            : MapClimate.CurrentTemperatureCelsius;

        return ResolveFluidDisplayName(displayName, temperatureCelsius);
    }

    private static string ResolveFluidDisplayName(string displayName, float temperatureCelsius)
    {
        string resolvedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? DefaultFluidItemName
            : displayName;
        return $"{resolvedDisplayName} [{FormatTemperatureCelsius(temperatureCelsius)}]";
    }

    private static string FormatTemperatureCelsius(float value)
    {
        return $"{Mathf.RoundToInt(Mathf.Max(0f, value))} C";
    }

    private static bool TryResolveItemSetByName(ItemManager itemManager, string itemName, out ItemManager.ItemSet itemSet)
    {
        itemSet = default;
        if (itemManager == null || string.IsNullOrWhiteSpace(itemName))
        {
            return false;
        }

        List<ItemDefinition> definitions = itemManager.ItemDefinitions;
        if (definitions != null)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                ItemDefinition definition = definitions[i];
                if (definition == null
                    || (!NameMatches(definition.itemName, itemName) && !NameMatches(definition.name, itemName)))
                {
                    continue;
                }

                itemSet = new ItemManager.ItemSet
                {
                    id = definition.id,
                    name = string.IsNullOrWhiteSpace(definition.itemName) ? definition.name : definition.itemName,
                    portableMesh = definition.portableMesh,
                    portableMat = definition.portableMat,
                    icon = definition.icon,
                    size = (int)definition.size
                };
                return true;
            }
        }

        List<ItemManager.ItemSet> itemSets = itemManager.ItemSets;
        if (itemSets != null)
        {
            for (int i = 0; i < itemSets.Count; i++)
            {
                ItemManager.ItemSet candidate = itemSets[i];
                if (!NameMatches(candidate.name, itemName))
                {
                    continue;
                }

                itemSet = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool NameMatches(string value, string target)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !string.IsNullOrWhiteSpace(target)
            && string.Equals(value.Trim(), target.Trim(), System.StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveItemDisplayName(int itemId)
    {
        return ResolveItemDisplayName(itemId, null);
    }

    private static string ResolveItemDisplayName(int itemId, float? fluidTemperatureCelsius)
    {
        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        if (itemManager != null
            && itemManager.TryGetItemSetById(itemId, out ItemManager.ItemSet itemSet)
            && !string.IsNullOrWhiteSpace(itemSet.name))
        {
            return InputOutputModule.IsFluidItemId(itemId)
                ? ResolveFluidDisplayName(itemSet.name, fluidTemperatureCelsius ?? MapClimate.CurrentTemperatureCelsius)
                : itemSet.name;
        }

        string fallbackName = itemId >= 0 ? $"Item {itemId}" : "None";
        return InputOutputModule.IsFluidItemId(itemId)
            ? ResolveFluidDisplayName(fallbackName, fluidTemperatureCelsius ?? MapClimate.CurrentTemperatureCelsius)
            : fallbackName;
    }

    private static ItemManager.ItemSet ResolveItemSet(int itemId)
    {
        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        if (itemId >= 0
            && itemManager != null
            && itemManager.TryGetItemSetById(itemId, out ItemManager.ItemSet itemSet))
        {
            return itemSet;
        }

        return new ItemManager.ItemSet
        {
            id = itemId,
            name = itemId >= 0 ? $"Item {itemId}" : "None"
        };
    }

    private static float? ResolveModuleFluidTemperature(InputOutputModule module, int itemId)
    {
        return module != null && itemId >= 0 && InputOutputModule.IsFluidItemId(itemId)
            ? module.GetStoredFluidTemperatureCelsius(itemId)
            : (float?)null;
    }

    private static int ResolveEnergyUseDisplayItemId(
        ItemDefinition.EnergyType energyType,
        int preferredEnergyItemId)
    {
        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        if (energyType == ItemDefinition.EnergyType.Burn
            && itemManager != null
            && TryResolveItemSetByName(itemManager, FireEnergyItemName, out ItemManager.ItemSet fireItemSet))
        {
            return fireItemSet.id;
        }

        if (IsEnergyDisplayItem(preferredEnergyItemId, energyType))
        {
            return preferredEnergyItemId;
        }

        if (itemManager == null)
        {
            return -1;
        }

        if (energyType == ItemDefinition.EnergyType.Electricity
            && TryResolveItemSetByName(itemManager, ElectricityItemName, out ItemManager.ItemSet electricityItemSet))
        {
            return electricityItemSet.id;
        }

        List<ItemDefinition> definitions = itemManager.ItemDefinitions;
        if (definitions == null)
        {
            return -1;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null
                && definition.id >= 0
                && definition.energyType == energyType
                && definition.energyAmount > 0)
            {
                return definition.id;
            }
        }

        return -1;
    }

    private static bool IsEnergyDisplayItem(int itemId, ItemDefinition.EnergyType energyType)
    {
        if (itemId < 0 || energyType == ItemDefinition.EnergyType.None)
        {
            return false;
        }

        ItemDefinition definition = InputOutputModule.ResolveItemDefinition(itemId);
        if (definition == null)
        {
            return false;
        }

        if (energyType == ItemDefinition.EnergyType.Electricity)
        {
            return ItemDefinition.IsElectricityItemDefinition(definition);
        }

        return definition.energyType == energyType && definition.energyAmount > 0;
    }

    private static string ResolveEnergyTypeDisplayName(ItemDefinition.EnergyType energyType)
    {
        switch (energyType)
        {
            case ItemDefinition.EnergyType.Electricity:
                return "Electricity";
            case ItemDefinition.EnergyType.Burn:
                return "Burn Energy";
            default:
                return "Energy";
        }
    }

    private void ResolveDefaultItemSlotReferences()
    {
        if (defaultItemSlot == null)
        {
            defaultItemSlot = new List<ItemSlot>();
        }

        if (defaultItem == null)
        {
            return;
        }

        for (int i = 0; i < defaultItem.Count; i++)
        {
            while (defaultItemSlot.Count <= i)
            {
                defaultItemSlot.Add(null);
            }

            if (defaultItemSlot[i] != null || defaultItem[i] == null)
            {
                continue;
            }

            ItemSlot slot = defaultItem[i].GetComponent<ItemSlot>();
            if (slot == null)
            {
                slot = defaultItem[i].GetComponentInChildren<ItemSlot>(true);
            }

            defaultItemSlot[i] = slot;
        }

        CaptureDefaultItemSiblingIndices();
    }

    private void CaptureDefaultItemSiblingIndices()
    {
        if (defaultItemSiblingIndicesCaptured || defaultItem == null)
        {
            return;
        }

        defaultItemOriginalSiblingIndices.Clear();
        for (int i = 0; i < defaultItem.Count; i++)
        {
            GameObject root = defaultItem[i];
            defaultItemOriginalSiblingIndices.Add(root != null ? root.transform.GetSiblingIndex() : -1);
        }

        defaultItemSiblingIndicesCaptured = true;
    }

    private void RestoreDefaultItemSiblingIndices()
    {
        if (!defaultItemSiblingIndicesCaptured || defaultItem == null)
        {
            return;
        }

        for (int i = 0; i < defaultItem.Count; i++)
        {
            GameObject root = defaultItem[i];
            if (root == null || i >= defaultItemOriginalSiblingIndices.Count)
            {
                continue;
            }

            Transform parent = root.transform.parent;
            int siblingIndex = defaultItemOriginalSiblingIndices[i];
            if (parent == null || siblingIndex < 0)
            {
                continue;
            }

            root.transform.SetSiblingIndex(Mathf.Min(siblingIndex, parent.childCount - 1));
        }
    }

    private void MoveDefaultItemBelowInputItem(int defaultItemIndex, int orderBelowInput)
    {
        GameObject root = defaultItem != null
            && defaultItemIndex >= 0
            && defaultItemIndex < defaultItem.Count
            ? defaultItem[defaultItemIndex]
            : null;
        if (root == null || inputItem == null)
        {
            return;
        }

        Transform targetTransform = root.transform;
        Transform inputTransform = inputItem.transform;
        if (targetTransform.parent == null || targetTransform.parent != inputTransform.parent)
        {
            return;
        }

        int targetSiblingIndex = inputTransform.GetSiblingIndex() + Mathf.Max(1, orderBelowInput);
        if (targetTransform.GetSiblingIndex() < inputTransform.GetSiblingIndex())
        {
            targetSiblingIndex--;
        }

        targetTransform.SetSiblingIndex(Mathf.Min(targetSiblingIndex, targetTransform.parent.childCount - 1));
    }

    private void ResolveGaugeReferences()
    {
        ResolveGaugeReferences(defaultGauge, ref defaultFill, ref defaultGaugeText);
    }

    private static void ResolveGaugeReferences(
        GameObject gaugeRoot,
        ref Image fill,
        ref TextMeshProUGUI text)
    {
        if (gaugeRoot == null)
        {
            return;
        }

        if (fill == null || !fill.transform.IsChildOf(gaugeRoot.transform))
        {
            fill = ResolveChildComponent<Image>(gaugeRoot);
        }

        if (text == null || !text.transform.IsChildOf(gaugeRoot.transform))
        {
            text = ResolveChildComponent<TextMeshProUGUI>(gaugeRoot);
        }
    }

    private static T ResolveChildComponent<T>(GameObject root) where T : Component
    {
        if (root == null)
        {
            return null;
        }

        T[] components = root.GetComponentsInChildren<T>(true);
        for (int i = 0; i < components.Length; i++)
        {
            T component = components[i];
            if (component != null && component.transform != root.transform)
            {
                return component;
            }
        }

        return root.GetComponent<T>();
    }

    private static void SetActiveIfNeeded(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }

    private static void SetTextIfChanged(TextMeshProUGUI target, string value)
    {
        if (target == null)
        {
            return;
        }

        string resolvedValue = value ?? string.Empty;
        if (target.text != resolvedValue)
        {
            target.text = resolvedValue;
        }
    }

    private static T GetListItem<T>(List<T> list, int index) where T : class
    {
        return list != null && index >= 0 && index < list.Count ? list[index] : null;
    }

    private static void SetGauge(
        GameObject root,
        Image fill,
        TextMeshProUGUI text,
        bool active,
        float fillAmount,
        Color fillColor,
        float currentValue,
        float maxValue,
        bool alwaysShowOneDecimal = false,
        string textOverride = null)
    {
        bool wasActive = root != null ? root.activeSelf : fill != null && fill.gameObject.activeInHierarchy;
        SetActiveIfNeeded(root, active);
        if (fill != null)
        {
            float targetFillAmount = active ? Mathf.Clamp01(fillAmount) : 0f;
            ApplyGaugeFill(fill, targetFillAmount, !active || !wasActive, active);
            if (active && fill.color != fillColor)
            {
                fill.color = fillColor;
            }
        }

        if (text != null)
        {
            SetTextIfChanged(
                text,
                active
                    ? textOverride ?? FormatGaugeValue(currentValue, maxValue, alwaysShowOneDecimal)
                    : string.Empty);
        }
    }

    private static void ApplyGaugeFill(Image fill, float targetFillAmount, bool snap, bool active)
    {
        if (fill == null)
        {
            return;
        }

        targetFillAmount = Mathf.Clamp01(targetFillAmount);
        if (!active || !Application.isPlaying || snap)
        {
            fill.fillAmount = targetFillAmount;
            if (active && Application.isPlaying)
            {
                GaugeFillTargets[fill] = targetFillAmount;
            }
            else
            {
                GaugeFillTargets.Remove(fill);
            }

            return;
        }

        GaugeFillTargets[fill] = targetFillAmount;
    }

    private static void UpdateGaugeFill(Image fill)
    {
        if (fill == null)
        {
            return;
        }

        if (!GaugeFillTargets.TryGetValue(fill, out float targetFillAmount))
        {
            return;
        }

        if (!fill.gameObject.activeInHierarchy)
        {
            GaugeFillTargets.Remove(fill);
            fill.fillAmount = 0f;
            return;
        }

        float deltaTime = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.2f);
        float lerpAmount = 1f - Mathf.Exp(-GaugeFillLerpSpeed * deltaTime);
        float nextFillAmount = Mathf.Lerp(fill.fillAmount, targetFillAmount, lerpAmount);
        fill.fillAmount = Mathf.Abs(nextFillAmount - targetFillAmount) <= GaugeFillSnapThreshold
            ? targetFillAmount
            : nextFillAmount;
    }

    private static string FormatGaugeValue(float currentValue, float maxValue, bool alwaysShowOneDecimal)
    {
        return $"{FormatGaugeNumber(currentValue, alwaysShowOneDecimal)} / {FormatGaugeNumber(maxValue, alwaysShowOneDecimal)}";
    }

    private static string FormatGaugeNumber(float value, bool alwaysShowOneDecimal)
    {
        value = Mathf.Max(0f, value);
        if (alwaysShowOneDecimal)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        float rounded = Mathf.Round(value);
        if (Mathf.Abs(value - rounded) < 0.05f)
        {
            return Mathf.RoundToInt(rounded).ToString();
        }

        return value.ToString("0.#");
    }

    private static string FormatKilowatts(float kilowatts)
    {
        kilowatts = Mathf.Max(0f, kilowatts);
        if (kilowatts <= 0.0001f)
        {
            return "0";
        }

        if (kilowatts < 0.01f)
        {
            return kilowatts.ToString("0.###", CultureInfo.InvariantCulture);
        }

        if (kilowatts < 1f)
        {
            return kilowatts.ToString("0.##", CultureInfo.InvariantCulture);
        }

        return FormatGaugeNumber(kilowatts, false);
    }

    private static string FormatMetersPerSecond(float metersPerSecond)
    {
        return FormatGaugeNumber(metersPerSecond, true);
    }

    private static string FormatEnergyUseRate(ItemDefinition.EnergyType energyType, float amountPerSecond)
    {
        amountPerSecond = Mathf.Max(0f, amountPerSecond);
        if (energyType == ItemDefinition.EnergyType.Electricity)
        {
            return $"{FormatKilowatts(amountPerSecond / 1000f)}kW / s";
        }

        return $"{FormatGaugeNumber(amountPerSecond, false)} / s";
    }

    private static string FormatLitersPerSecond(float litersPerSecond)
    {
        return $"{FormatGaugeNumber(litersPerSecond, true)}L / s";
    }
}
