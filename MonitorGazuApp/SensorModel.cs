using System;

namespace MonitorGazuApp;

public abstract class CzujnikBazowy
{
    public DateTime CzasOdczytu { get; protected set; }
    public abstract string PobierzStatus();
}

public class PomiarGazu : CzujnikBazowy
{
    public string SensorId { get; private set; }
    public double WartoscPpm { get; private set; }

    // NOWOŚĆ: Współrzędne na mapie
    public double X { get; set; }
    public double Y { get; set; }

    public PomiarGazu(double wartosc, double x = 0, double y = 0, string sensorId = "S1")
    {
        SensorId = string.IsNullOrWhiteSpace(sensorId) ? "S1" : sensorId;
        WartoscPpm = wartosc;
        X = x;
        Y = y;
        CzasOdczytu = DateTime.Now;
    }

    public override string PobierzStatus()
    {
        if (WartoscPpm > 150) return "⚠️ KRYTYCZNE";
        if (WartoscPpm > 80) return "Ostrzeżenie";
        return "Norma";
    }

    public override string ToString()
    {
        // Wyświetlamy lokalizację w historii (wymóg labów)
        return $"[{CzasOdczytu:HH:mm:ss}] {SensorId} {WartoscPpm:0.0} ppm (Lok: {X:0},{Y:0})";
    }
}