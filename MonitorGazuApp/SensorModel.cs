using System;

namespace MonitorGazuApp;

// ABSTRAKCJA: Klasa abstrakcyjna - szablon dla różnych typów czujników
// Nie można utworzyć obiektu CzujnikBazowy, służy tylko jako baza dla innych klas
public abstract class CzujnikBazowy
{
    // HERMETYZACJA: protected - dostępne tylko w tej klasie i klasach dziedziczących
    public DateTime CzasOdczytu { get; protected set; }
    
    // ABSTRAKCJA: metoda abstrakcyjna - każda klasa dziedzicząca MUSI ją zaimplementować
    public abstract string PobierzStatus();
}

// DZIEDZICZENIE: PomiarGazu dziedziczy po CzujnikBazowy
// Przejmuje pole CzasOdczytu i musi zaimplementować metodę PobierzStatus()
public class PomiarGazu : CzujnikBazowy
{
    // HERMETYZACJA: private set - wartości można ustawić tylko wewnątrz klasy
    // Z zewnątrz można tylko odczytać (get), ale nie zmienić
    public string SensorId { get; private set; }
    public double WartoscPpm { get; private set; }

    // Współrzędne na mapie (publiczne, można zmieniać)
    public double X { get; set; }
    public double Y { get; set; }

    // KONSTRUKTOR: inicjalizuje obiekt przy tworzeniu
    // Ustawia początkowe wartości pól
    public PomiarGazu(double wartosc, double x = 0, double y = 0, string sensorId = "S1")
    {
        SensorId = string.IsNullOrWhiteSpace(sensorId) ? "S1" : sensorId;
        WartoscPpm = wartosc;
        X = x;
        Y = y;
        CzasOdczytu = DateTime.Now;
    }

    // POLIMORFIZM: nadpisujemy (override) metodę abstrakcyjną z klasy bazowej
    // Ta sama nazwa metody, ale inne zachowanie niż w innych klasach dziedziczących
    public override string PobierzStatus()
    {
        if (WartoscPpm > 150) return "⚠️ KRYTYCZNE";
        if (WartoscPpm > 80) return "Ostrzeżenie";
        return "Norma";
    }

    // POLIMORFIZM: nadpisujemy metodę ToString() z klasy Object
    // Każdy obiekt w C# dziedziczy po Object i ma metodę ToString()
    public override string ToString()
    {
        return $"[{CzasOdczytu:HH:mm:ss}] {SensorId} {WartoscPpm:0.0} ppm (Lok: {X:0},{Y:0})";
    }
}