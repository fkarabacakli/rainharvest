namespace RainHarvest.Contracts;

/// <summary>
/// Body of <c>rainwater/{deviceId}/telemetry</c>, published by the device every ~10 s.
/// All parameters without a default are required; unknown JSON fields are ignored.
/// </summary>
/// <param name="LevelPct">Tank level, 0–100 %.</param>
/// <param name="LevelCm">Distance-derived water height, cm.</param>
/// <param name="TurbidityNtu">Turbidity, NTU (≥ 0).</param>
/// <param name="Ph">pH, 0–14.</param>
/// <param name="Raining">Rain sensor wet.</param>
/// <param name="SoilPct">Planter soil moisture, 0–100 %.</param>
/// <param name="FlowLpm">Current outlet flow, L/min.</param>
/// <param name="FlowTotalL">Cumulative outlet volume since device boot, L.</param>
/// <param name="Rssi">Wi-Fi signal, dBm.</param>
/// <param name="Ts">Device clock, Unix seconds. Optional; ignored if the clock is not synced.</param>
/// <param name="WaterTempC">DS18B20 water temperature, °C. Optional sensor.</param>
public sealed record TelemetryPayload(
    double LevelPct,
    double LevelCm,
    double TurbidityNtu,
    double Ph,
    bool Raining,
    double SoilPct,
    double FlowLpm,
    double FlowTotalL,
    int Rssi,
    long? Ts = null,
    double? WaterTempC = null);
