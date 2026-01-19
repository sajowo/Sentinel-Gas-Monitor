using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace MonitorGazuApp;

// DZIEDZICZENIE: MainWindow dziedziczy po klasie Window (klasa bazowa z Avalonia)
// Przejmuje wszystkie funkcjonalności okna (minimalizacja, zamykanie, etc.)
public partial class MainWindow : Window
{
    // HERMETYZACJA: pola prywatne (private) - ukryte przed światem zewnętrznym
    // Dostęp tylko przez metody tej klasy
    private readonly Dictionary<string, ObservableCollection<PomiarGazu>> _historiaPerSensor = new();
    private string _wybranySensorId = "S1";
    private TextBlock _lblTytulStezenia;
    private bool _demoDziala = false;

    // Kolekcje do zarządzania sensorami na mapie
    private List<Ellipse> _wszystkieSensory = new List<Ellipse>();
    private Dictionary<Control, Control> _mapaPodpisow = new Dictionary<Control, Control>();
    private Dictionary<Control, string> _mapaSensorId = new Dictionary<Control, string>();
    
    // Słownik połączeń IoT dla każdego sensora
    private Dictionary<string, IoTConnection> _iotConnections = new Dictionary<string, IoTConnection>();

    // Referencje do głównego sensora S1
    private Ellipse _mainDot;
    private TextBlock _mainLabel;

    // Zmienne do obsługi przeciągania sensorów
    private bool _jestPrzeciagany = false;
    private Control _przeciaganyObiekt = null;
    private Point _punktZaczepienia;

    // KONSTRUKTOR: specjalna metoda wywoływana przy tworzeniu obiektu MainWindow
    // Inicjalizuje wszystkie komponenty i ustawia początkowy stan aplikacji
    public MainWindow()
    {
        InitializeComponent();

        // Tworzymy początkową historię dla sensora S1
        _historiaPerSensor["S1"] = new ObservableCollection<PomiarGazu>();
        this.FindControl<ListBox>("ListaHistorii").ItemsSource = _historiaPerSensor["S1"];

        _lblTytulStezenia = this.FindControl<TextBlock>("LblTytulStezenia");

        // Pobieramy referencje do elementów wizualnych sensora S1
        _mainDot = this.FindControl<Ellipse>("CzujnikGlowny");
        _mainLabel = this.FindControl<TextBlock>("EtykietaGlowna");
        _mainDot.Tag = "S1";

        // Rejestrujemy S1 w systemie
        _wszystkieSensory.Add(_mainDot);
        _mapaPodpisow.Add(_mainDot, _mainLabel);
        _mapaSensorId[_mainDot] = "S1";
        WlaczPrzeciaganie(_mainDot);

        // Inicjalizujemy pola X i Y z początkową pozycją S1
        double startX = Canvas.GetLeft(_mainDot);
        double startY = Canvas.GetTop(_mainDot);
        this.FindControl<TextBox>("TxtX").Text = startX.ToString("0");
        this.FindControl<TextBox>("TxtY").Text = startY.ToString("0");

        // Podpinamy obsługę przycisków
        this.FindControl<Button>("BtnOtworzPolaczenie").Click += OtworzOknoIoT;
        this.FindControl<Button>("BtnRozlaczIoT").Click += RozlaczIoT;
        this.FindControl<Button>("BtnSymulacja").Click += StartDemo;
        this.FindControl<Button>("BtnStop").Click += (s, e) => _demoDziala = false;

        this.FindControl<Button>("BtnUstaw").Click += (s, e) =>
        {
            try
            {
                double x = double.Parse(this.FindControl<TextBox>("TxtX").Text);
                double y = double.Parse(this.FindControl<TextBox>("TxtY").Text);
                PrzesunWizualnieS1(x, y);
            }
            catch { }
        };

        // Dodawanie nowych sensorów dwuklikiem na mapie
        var canvas = this.FindControl<Canvas>("MapaCanvas");
        canvas.DoubleTapped += DodajSensorDwuklikiem;
    }

    // ABSTRAKCJA: metoda ukrywa szczegóły pobierania ID sensora
    // Użytkownik metody nie musi wiedzieć, że sprawdzamy mapę i Tag
    private string GetSensorId(Control sensor)
    {
        if (_mapaSensorId.TryGetValue(sensor, out var id)) return id;
        if (sensor.Tag is string tag && !string.IsNullOrWhiteSpace(tag)) return tag;
        return "S?";
    }

    // ABSTRAKCJA: ukrywamy logikę tworzenia historii - zwracamy gotową kolekcję
    private ObservableCollection<PomiarGazu> GetHistoriaSensora(string sensorId)
    {
        if (!_historiaPerSensor.TryGetValue(sensorId, out var historia))
        {
            historia = new ObservableCollection<PomiarGazu>();
            _historiaPerSensor[sensorId] = historia;
        }
        return historia;
    }

    // Zmienia aktualnie wybrany sensor i odświeża interfejs
    private void UstawWybranySensor(Control sensor)
    {
        var id = GetSensorId(sensor);
        _wybranySensorId = id;
        this.FindControl<ListBox>("ListaHistorii").ItemsSource = GetHistoriaSensora(id);

        if (_lblTytulStezenia != null)
            _lblTytulStezenia.Text = $"Aktualne Stężenie ({id}):";

        // Aktualizujemy pola X i Y z aktualną pozycją sensora
        double x = Canvas.GetLeft(sensor);
        double y = Canvas.GetTop(sensor);
        this.FindControl<TextBox>("TxtX").Text = x.ToString("0");
        this.FindControl<TextBox>("TxtY").Text = y.ToString("0");

        // Aktualizujemy przyciski IoT dla wybranego sensora
        var btnPolacz = this.FindControl<Button>("BtnOtworzPolaczenie");
        var btnRozlacz = this.FindControl<Button>("BtnRozlaczIoT");
        
        if (_iotConnections.ContainsKey(id))
        {
            btnPolacz.Content = $"✅ {id} POŁĄCZONY";
            btnPolacz.Background = Brushes.ForestGreen;
            btnRozlacz.IsVisible = true;
            btnRozlacz.Content = $"🔌 Rozłącz {id}";
        }
        else
        {
            btnPolacz.Content = $"📡 Połącz {id} IoT";
            btnPolacz.Background = new SolidColorBrush(Color.Parse("#007ACC"));
            btnRozlacz.IsVisible = false;
        }
        btnPolacz.IsEnabled = true;

        var historia = GetHistoriaSensora(id);
        if (historia.Count > 0) UstawPanelPomiaru(historia[0].WartoscPpm);
        else UstawPanelPomiaru(null);
    }

    // PRZECIĄŻANIE: metoda przyjmuje double? (nullable) - może być liczba lub null
    // Ta sama nazwa metody, ale różne zachowanie w zależności od wartości
    private void UstawPanelPomiaru(double? wartosc)
    {
        var lblWynik = this.FindControl<TextBlock>("LblWynik");
        var lblStatus = this.FindControl<TextBlock>("LblStatus");
        var bar = this.FindControl<ProgressBar>("PasekGazu");

        // Jeśli brak danych - wyświetl stan neutralny
        if (wartosc is null)
        {
            lblWynik.Text = "---";
            lblWynik.Foreground = Brushes.Gray;
            lblStatus.Text = "Brak danych";
            lblStatus.Foreground = Brushes.Gray;
            bar.Value = 0;
            bar.Foreground = Brushes.Gray;
            return;
        }

        // Określamy kolor i status na podstawie wartości
        IBrush kolor = Brushes.LimeGreen;
        string opis = "W Normie";
        if (wartosc > 150) { kolor = Brushes.Red; opis = "⚠️ ALARM GAZOWY!"; }
        else if (wartosc > 80) { kolor = Brushes.Orange; opis = "Ostrzeżenie"; }

        // Aktualizujemy interfejs
        lblWynik.Text = wartosc.Value.ToString("0.0");
        lblWynik.Foreground = kolor;
        lblStatus.Text = opis;
        lblStatus.Foreground = kolor;
        bar.Value = wartosc.Value;
        bar.Foreground = kolor;
    }


    // === OBSŁUGA PRZECIĄGANIA SENSORÓW (DRAG & DROP) ===
    
    private void WlaczPrzeciaganie(Control element)
    {
        element.Cursor = new Cursor(StandardCursorType.Hand);
        element.PointerPressed += OnPointerPressed;
        element.PointerMoved += OnPointerMoved;
        element.PointerReleased += OnPointerReleased;
    }

    private void OnPointerPressed(object sender, PointerPressedEventArgs e)
    {
        var element = (Control)sender;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            UstawWybranySensor(element);
            _jestPrzeciagany = true;
            _przeciaganyObiekt = element;
            _punktZaczepienia = e.GetPosition(element);

            e.Pointer.Capture(element);
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object sender, PointerEventArgs e)
    {
        if (!_jestPrzeciagany || _przeciaganyObiekt == null) return;

        var canvas = this.FindControl<Canvas>("MapaCanvas");
        var p = e.GetPosition(canvas);

        double noweX = p.X - _punktZaczepienia.X;
        double noweY = p.Y - _punktZaczepienia.Y;

        // Ograniczamy pozycję do granic mapy
        if (noweX < 0) noweX = 0; if (noweY < 0) noweY = 0;
        if (noweX > canvas.Bounds.Width - 20) noweX = canvas.Bounds.Width - 20;
        if (noweY > canvas.Bounds.Height - 20) noweY = canvas.Bounds.Height - 20;

        // Przesuwamy kropkę sensora
        Canvas.SetLeft(_przeciaganyObiekt, noweX);
        Canvas.SetTop(_przeciaganyObiekt, noweY);

        // Przesuwamy etykietę razem z kropką
        if (_mapaPodpisow.TryGetValue(_przeciaganyObiekt, out var etykieta))
        {
            Canvas.SetLeft(etykieta, noweX + 5);
            Canvas.SetTop(etykieta, noweY + 25);
        }

        // Aktualizujemy pola tekstowe dla S1
        if (_przeciaganyObiekt == _mainDot)
        {
            this.FindControl<TextBox>("TxtX").Text = noweX.ToString("0");
            this.FindControl<TextBox>("TxtY").Text = noweY.ToString("0");
        }
    }

    private void OnPointerReleased(object sender, PointerReleasedEventArgs e)
    {
        _jestPrzeciagany = false;
        _przeciaganyObiekt = null;
        e.Pointer.Capture(null);
    }


    // Dodaje nowy sensor na mapie po dwukliku
    // OBIEKT: tworzymy nowe instancje klas Ellipse i TextBlock
    private void DodajSensorDwuklikiem(object sender, TappedEventArgs e)
    {
        if (e.Source is Ellipse || e.Source is TextBlock) return;

        var canvas = (Canvas)sender;
        var p = e.GetPosition(canvas);

        // Tworzymy wizualną reprezentację sensora (kropka)
        var kropka = new Ellipse
        {
            Width = 20,
            Height = 20,
            Fill = Brushes.Gray,
            Stroke = Brushes.White,
            StrokeThickness = 2
        };
        Canvas.SetLeft(kropka, p.X - 10);
        Canvas.SetTop(kropka, p.Y - 10);

        var sensorId = $"S{_wszystkieSensory.Count + 1}";

        // Tworzymy etykietę z ID sensora
        var podpis = new TextBlock
        {
            Text = sensorId,
            Foreground = Brushes.LightGray,
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(podpis, p.X - 5);
        Canvas.SetTop(podpis, p.Y + 25);

        canvas.Children.Add(kropka);
        canvas.Children.Add(podpis);

        // Rejestrujemy nowy sensor w systemie
        kropka.Tag = sensorId;
        _wszystkieSensory.Add(kropka);
        _mapaPodpisow.Add(kropka, podpis);
        _mapaSensorId[kropka] = sensorId;
        WlaczPrzeciaganie(kropka);
    }


    // Przesuwa sensor S1 na określone współrzędne
    private void PrzesunWizualnieS1(double x, double y)
    {
        Canvas.SetLeft(_mainDot, x); Canvas.SetTop(_mainDot, y);
        Canvas.SetLeft(_mainLabel, x + 5); Canvas.SetTop(_mainLabel, y + 25);
    }

    // Aktualizuje interfejs na podstawie nowego pomiaru
    private void AktualizujEkran(double wartosc)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Odświeżamy panel tylko dla aktualnie wybranego sensora
            if (_wybranySensorId == "S1")
                UstawPanelPomiaru(wartosc);

            // Zmieniamy kolor sensora S1 w zależności od wartości
            IBrush kolor = Brushes.LimeGreen;
            if (wartosc > 150) kolor = Brushes.Red;
            else if (wartosc > 80) kolor = Brushes.Orange;

            _mainDot.Fill = kolor;
            _mainDot.Width = 20;
            _mainDot.Height = 20;

            double x = Canvas.GetLeft(_mainDot);
            double y = Canvas.GetTop(_mainDot);

            // OBIEKT: tworzymy nową instancję klasy PomiarGazu
            var pomiar = new PomiarGazu(wartosc, x, y, "S1");
            var historiaS1 = GetHistoriaSensora("S1");
            historiaS1.Insert(0, pomiar);
            if (historiaS1.Count > 100) historiaS1.RemoveAt(99);
        });
    }

    // === SYMULACJA POMIARÓW ===
    // Generuje losowe wartości dla wszystkich sensorów
    private async void StartDemo(object sender, RoutedEventArgs e)
    {
        if (_demoDziala) return;
        _demoDziala = true;
        var rnd = new Random();
        double poziom = 50;

        await Task.Run(async () =>
        {
            while (_demoDziala)
            {
                // Losowa zmiana poziomu gazu dla S1
                poziom += rnd.Next(-20, 30);
                if (poziom < 20) poziom = 20; 
                if (poziom > 250) poziom = 250;
                AktualizujEkran(poziom);

                // Aktualizujemy pozostałe sensory
                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var s in _wszystkieSensory)
                    {
                        if (s == _mainDot) continue; // S1 już zaktualizowany
                        
                        int r = rnd.Next(0, 5);
                        s.Fill = r == 0 ? Brushes.Red : (r == 1 ? Brushes.Orange : Brushes.Gray);

                        // Tworzymy pomiar dla tego sensora
                        var id = GetSensorId(s);
                        var x = Canvas.GetLeft(s);
                        var y = Canvas.GetTop(s);
                        double ppm = r == 0
                            ? rnd.Next(160, 251)      // czerwony - alarm
                            : (r == 1 ? rnd.Next(90, 151)     // pomarańczowy - ostrzeżenie
                                     : rnd.Next(20, 81));     // szary - norma

                        // OBIEKT: tworzymy nową instancję PomiarGazu
                        var pomiar = new PomiarGazu(ppm, x, y, id);
                        var historia = GetHistoriaSensora(id);
                        historia.Insert(0, pomiar);
                        if (historia.Count > 100) historia.RemoveAt(99);

                        // Jeśli ten sensor jest wybrany, aktualizujemy panel
                        if (_wybranySensorId == id)
                            UstawPanelPomiaru(ppm);
                    }
                });
                await Task.Delay(500); // Odczekaj 0.5 sekundy
            }
        });
    }

    // === POŁĄCZENIE IoT ===
    // Otwiera okno dialogowe do wyboru urządzenia IoT dla aktualnie wybranego sensora
    private async void OtworzOknoIoT(object sender, RoutedEventArgs e)
    {
        var sensorId = _wybranySensorId;
        var okno = new IoTConnectionWindow();
        var wynik = await okno.ShowDialog<bool>(this);
        
        if (wynik)
        {
            var btnPolacz = this.FindControl<Button>("BtnOtworzPolaczenie");
            var btnRozlacz = this.FindControl<Button>("BtnRozlaczIoT");
            
            btnPolacz.Content = $"⏳ Łączenie {sensorId}...";
            btnPolacz.IsEnabled = false;
            btnRozlacz.IsVisible = false;

            // Tworzymy callback, który aktualizuje konkretny sensor
            Action<double> callback = (wartosc) => AktualizujSensor(sensorId, wartosc);
            
            var iotConnection = new IoTConnection(callback);
            bool polaczono = await iotConnection.ConnectAsync();

            if (polaczono)
            {
                // Zapisujemy połączenie dla tego sensora
                _iotConnections[sensorId] = iotConnection;
                
                btnPolacz.Content = $"✅ {sensorId} POŁĄCZONY";
                btnPolacz.Background = Brushes.ForestGreen;
                btnPolacz.IsEnabled = true;
                
                btnRozlacz.IsVisible = true;
                btnRozlacz.Content = $"🔌 Rozłącz {sensorId}";
            }
            else
            {
                btnPolacz.Content = "❌ Błąd połączenia";
                btnPolacz.Background = Brushes.Red;
                await Task.Delay(2000);
                btnPolacz.Content = $"📡 Połącz {sensorId} IoT";
                btnPolacz.Background = new SolidColorBrush(Color.Parse("#007ACC"));
                btnPolacz.IsEnabled = true;
            }
        }
    }

    // Rozłącza IoT dla aktualnie wybranego sensora
    private async void RozlaczIoT(object sender, RoutedEventArgs e)
    {
        var sensorId = _wybranySensorId;
        
        if (_iotConnections.ContainsKey(sensorId))
        {
            var connection = _iotConnections[sensorId];
            await connection.DisconnectAsync();
            _iotConnections.Remove(sensorId);
            
            var btnPolacz = this.FindControl<Button>("BtnOtworzPolaczenie");
            var btnRozlacz = this.FindControl<Button>("BtnRozlaczIoT");
            
            btnPolacz.Content = $"📡 Połącz {sensorId} IoT";
            btnPolacz.Background = new SolidColorBrush(Color.Parse("#007ACC"));
            btnRozlacz.IsVisible = false;
        }
    }

    // Aktualizuje konkretny sensor danymi z IoT
    private void AktualizujSensor(string sensorId, double wartosc)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Znajdź sensor o danym ID
            var sensor = _wszystkieSensory.FirstOrDefault(s => GetSensorId(s) == sensorId);
            if (sensor == null) return;

            // Odświeżamy panel tylko jeśli ten sensor jest wybrany
            if (_wybranySensorId == sensorId)
                UstawPanelPomiaru(wartosc);

            // Zmieniamy kolor sensora w zależności od wartości
            IBrush kolor = Brushes.LimeGreen;
            if (wartosc > 150) kolor = Brushes.Red;
            else if (wartosc > 80) kolor = Brushes.Orange;

            sensor.Fill = kolor;
            sensor.Width = 20;
            sensor.Height = 20;

            double x = Canvas.GetLeft(sensor);
            double y = Canvas.GetTop(sensor);

            // Dodajemy pomiar do historii
            var pomiar = new PomiarGazu(wartosc, x, y, sensorId);
            var historia = GetHistoriaSensora(sensorId);
            historia.Insert(0, pomiar);
            if (historia.Count > 100) historia.RemoveAt(99);
        });
    }
}
