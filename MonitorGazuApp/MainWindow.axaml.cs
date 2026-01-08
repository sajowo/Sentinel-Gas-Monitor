using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using MQTTnet;
using MQTTnet.Client;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using System.Collections.Generic;

namespace MonitorGazuApp;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, ObservableCollection<PomiarGazu>> _historiaPerSensor = new();
    private string _wybranySensorId = "S1";
    private TextBlock _lblTytulStezenia;
    private IMqttClient _mqttClient;
    private bool _demoDziala = false;

    // Lista sensorów i mapa podpisów
    private List<Ellipse> _wszystkieSensory = new List<Ellipse>();
    private Dictionary<Control, Control> _mapaPodpisow = new Dictionary<Control, Control>();
    private Dictionary<Control, string> _mapaSensorId = new Dictionary<Control, string>();

    // Referencje do głównego sensora S1
    private Ellipse _mainDot;
    private TextBlock _mainLabel;

    // --- ZMIENNE DO DRAG & DROP ---
    private bool _jestPrzeciagany = false;
    private Control _przeciaganyObiekt = null;
    private Point _punktZaczepienia;

    public MainWindow()
    {
        InitializeComponent();

        _historiaPerSensor["S1"] = new ObservableCollection<PomiarGazu>();
        this.FindControl<ListBox>("ListaHistorii").ItemsSource = _historiaPerSensor["S1"];

        _lblTytulStezenia = this.FindControl<TextBlock>("LblTytulStezenia");

        // Pobieramy S1
        _mainDot = this.FindControl<Ellipse>("CzujnikGlowny");
        _mainLabel = this.FindControl<TextBlock>("EtykietaGlowna");

        _mainDot.Tag = "S1";

        // Rejestrujemy S1 w systemie
        _wszystkieSensory.Add(_mainDot);
        _mapaPodpisow.Add(_mainDot, _mainLabel);
        _mapaSensorId[_mainDot] = "S1";
        WlaczPrzeciaganie(_mainDot); // Włączamy przesuwanie dla S1

        // --- PRZYCISKI ---
        this.FindControl<Button>("BtnOtworzPolaczenie").Click += PokazOknoPolaczenia;
        this.FindControl<Button>("BtnAnuluj").Click += (s, e) => ZmienOkno(false);
        this.FindControl<Button>("BtnPotwierdzPolacz").Click += PolaczMQTT;
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


        var canvas = this.FindControl<Canvas>("MapaCanvas");
        canvas.DoubleTapped += DodajSensorDwuklikiem;
    }

    private string GetSensorId(Control sensor)
    {
        if (_mapaSensorId.TryGetValue(sensor, out var id)) return id;
        if (sensor.Tag is string tag && !string.IsNullOrWhiteSpace(tag)) return tag;
        return "S?";
    }

    private ObservableCollection<PomiarGazu> GetHistoriaSensora(string sensorId)
    {
        if (!_historiaPerSensor.TryGetValue(sensorId, out var historia))
        {
            historia = new ObservableCollection<PomiarGazu>();
            _historiaPerSensor[sensorId] = historia;
        }
        return historia;
    }

    private void UstawWybranySensor(Control sensor)
    {
        var id = GetSensorId(sensor);
        _wybranySensorId = id;
        this.FindControl<ListBox>("ListaHistorii").ItemsSource = GetHistoriaSensora(id);

        if (_lblTytulStezenia != null)
            _lblTytulStezenia.Text = $"Aktualne Stężenie ({id}):";

        var historia = GetHistoriaSensora(id);
        if (historia.Count > 0) UstawPanelPomiaru(historia[0].WartoscPpm);
        else UstawPanelPomiaru(null);
    }

    private void UstawPanelPomiaru(double? wartosc)
    {
        var lblWynik = this.FindControl<TextBlock>("LblWynik");
        var lblStatus = this.FindControl<TextBlock>("LblStatus");
        var bar = this.FindControl<ProgressBar>("PasekGazu");

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

        IBrush kolor = Brushes.LimeGreen;
        string opis = "W Normie";
        if (wartosc > 150) { kolor = Brushes.Red; opis = "⚠️ ALARM GAZOWY!"; }
        else if (wartosc > 80) { kolor = Brushes.Orange; opis = "Ostrzeżenie"; }

        lblWynik.Text = wartosc.Value.ToString("0.0");
        lblWynik.Foreground = kolor;
        lblStatus.Text = opis;
        lblStatus.Foreground = kolor;
        bar.Value = wartosc.Value;
        bar.Foreground = kolor;
    }


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

        // Ograniczenia mapy
        if (noweX < 0) noweX = 0; if (noweY < 0) noweY = 0;
        if (noweX > canvas.Bounds.Width - 20) noweX = canvas.Bounds.Width - 20;
        if (noweY > canvas.Bounds.Height - 20) noweY = canvas.Bounds.Height - 20;

        // Przesunięcie kropki
        Canvas.SetLeft(_przeciaganyObiekt, noweX);
        Canvas.SetTop(_przeciaganyObiekt, noweY);

        // Przesunięcie podpisu
        if (_mapaPodpisow.TryGetValue(_przeciaganyObiekt, out var etykieta))
        {
            Canvas.SetLeft(etykieta, noweX + 5);
            Canvas.SetTop(etykieta, noweY + 25);
        }

        // Aktualizacja pól tekstowych dla S1
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


    private void DodajSensorDwuklikiem(object sender, TappedEventArgs e)
    {
        if (e.Source is Ellipse || e.Source is TextBlock) return;

        var canvas = (Canvas)sender;
        var p = e.GetPosition(canvas);

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

        kropka.Tag = sensorId;
        _wszystkieSensory.Add(kropka);
        _mapaPodpisow.Add(kropka, podpis);
        _mapaSensorId[kropka] = sensorId;
        WlaczPrzeciaganie(kropka);
    }


    private void PrzesunWizualnieS1(double x, double y)
    {
        Canvas.SetLeft(_mainDot, x); Canvas.SetTop(_mainDot, y);
        Canvas.SetLeft(_mainLabel, x + 5); Canvas.SetTop(_mainLabel, y + 25);
    }

    private void PokazOknoPolaczenia(object sender, RoutedEventArgs e)
    {
        var lista = this.FindControl<ListBox>("ListaUrzadzen");
        lista.ItemsSource = new[] {
            "🟢 ESP8266 - Hala Główna (Sygnał: 98%)",
            "🟢 Raspberry Pi - Serwerownia (Sygnał: 85%)",
            "🔴 Czujnik Magazyn (Offline)"
        };
        lista.SelectedIndex = 0;
        ZmienOkno(true);
    }
    private void ZmienOkno(bool widoczne) => this.FindControl<Grid>("Overlay").IsVisible = widoczne;

    private void AktualizujEkran(double wartosc)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Panel pomiarowy odświeżamy tylko, jeśli użytkownik ogląda S1
            if (_wybranySensorId == "S1")
                UstawPanelPomiaru(wartosc);

            // S1 zawsze aktualizuje swój wygląd
            IBrush kolor = Brushes.LimeGreen;
            if (wartosc > 150) kolor = Brushes.Red;
            else if (wartosc > 80) kolor = Brushes.Orange;

            _mainDot.Fill = kolor;
            _mainDot.Width = 20;
            _mainDot.Height = 20;

            double x = Canvas.GetLeft(_mainDot);
            double y = Canvas.GetTop(_mainDot);

            var pomiar = new PomiarGazu(wartosc, x, y, "S1");
            var historiaS1 = GetHistoriaSensora("S1");
            historiaS1.Insert(0, pomiar);
            if (historiaS1.Count > 100) historiaS1.RemoveAt(99);
        });
    }

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
                poziom += rnd.Next(-20, 30);
                if (poziom < 20) poziom = 20; if (poziom > 250) poziom = 250;
                AktualizujEkran(poziom);

                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var s in _wszystkieSensory)
                    {
                        if (s == _mainDot) continue;
                        int r = rnd.Next(0, 5);
                        s.Fill = r == 0 ? Brushes.Red : (r == 1 ? Brushes.Orange : Brushes.Gray);

                        // Dodajemy pomiar do historii danego sensora (żeby klik w S2/S3 coś pokazywał)
                        var id = GetSensorId(s);
                        var x = Canvas.GetLeft(s);
                        var y = Canvas.GetTop(s);
                        double ppm = r == 0
                            ? rnd.Next(160, 251)              // czerwony
                            : (r == 1 ? rnd.Next(90, 151)     // pomarańczowy
                                     : rnd.Next(20, 81));     // szary

                        var pomiar = new PomiarGazu(ppm, x, y, id);
                        var historia = GetHistoriaSensora(id);
                        historia.Insert(0, pomiar);
                        if (historia.Count > 100) historia.RemoveAt(99);

                        if (_wybranySensorId == id)
                            UstawPanelPomiaru(ppm);
                    }
                });
                await Task.Delay(500);
            }
        });
    }

    private async void PolaczMQTT(object sender, RoutedEventArgs e)
    {
        ZmienOkno(false);
        var btn = this.FindControl<Button>("BtnOtworzPolaczenie");
        btn.Content = "Łączenie...";

        var factory = new MqttFactory();
        _mqttClient = factory.CreateMqttClient();
        var opt = new MqttClientOptionsBuilder().WithTcpServer("test.mosquitto.org", 1883).Build();

        _mqttClient.ApplicationMessageReceivedAsync += msg =>
        {
            string txt = Encoding.UTF8.GetString(msg.ApplicationMessage.PayloadSegment);
            if (double.TryParse(txt.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                AktualizujEkran(val);
            return Task.CompletedTask;
        };

        try { await _mqttClient.ConnectAsync(opt); await _mqttClient.SubscribeAsync("mojprojekt/lab/gaz"); btn.Content = "✅ POŁĄCZONO"; btn.Background = Brushes.ForestGreen; }
        catch { btn.Content = "Błąd"; btn.Background = Brushes.Red; }
    }
}