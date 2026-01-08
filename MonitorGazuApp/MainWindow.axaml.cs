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
    private ObservableCollection<PomiarGazu> _historia;
    private IMqttClient _mqttClient;
    private bool _demoDziala = false;

    // Lista sensorów i mapa podpisów
    private List<Ellipse> _wszystkieSensory = new List<Ellipse>();
    private Dictionary<Control, Control> _mapaPodpisow = new Dictionary<Control, Control>();

    // Referencje do głównego sensora S1
    private Ellipse _mainDot;
    private TextBlock _mainLabel;

    // --- ZMIENNE DO DRAG & DROP ---
    private bool _jestPrzeciagany = false;
    private Control _przeciaganyObiekt = null;
    private Point _punktZaczepienia; // Tutaj był błąd, teraz dzięki "using Avalonia;" zadziała

    public MainWindow()
    {
        InitializeComponent();

        _historia = new ObservableCollection<PomiarGazu>();
        this.FindControl<ListBox>("ListaHistorii").ItemsSource = _historia;

        // Pobieramy S1
        _mainDot = this.FindControl<Ellipse>("CzujnikGlowny");
        _mainLabel = this.FindControl<TextBlock>("EtykietaGlowna");

        // Rejestrujemy S1 w systemie
        _wszystkieSensory.Add(_mainDot);
        _mapaPodpisow.Add(_mainDot, _mainLabel);
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
            Canvas.SetTop(etykieta, noweY + 35);
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

        var podpis = new TextBlock
        {
            Text = $"S{_wszystkieSensory.Count + 1}",
            Foreground = Brushes.LightGray,
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(podpis, p.X - 5);
        Canvas.SetTop(podpis, p.Y + 25);

        canvas.Children.Add(kropka);
        canvas.Children.Add(podpis);

        _wszystkieSensory.Add(kropka);
        _mapaPodpisow.Add(kropka, podpis);
        WlaczPrzeciaganie(kropka);
    }


    private void PrzesunWizualnieS1(double x, double y)
    {
        Canvas.SetLeft(_mainDot, x); Canvas.SetTop(_mainDot, y);
        Canvas.SetLeft(_mainLabel, x + 5); Canvas.SetTop(_mainLabel, y + 35);
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
            var lblWynik = this.FindControl<TextBlock>("LblWynik");
            var lblStatus = this.FindControl<TextBlock>("LblStatus");
            var bar = this.FindControl<ProgressBar>("PasekGazu");

            IBrush kolor = Brushes.LimeGreen;
            string opis = "W Normie";
            if (wartosc > 150) { kolor = Brushes.Red; opis = "⚠️ ALARM GAZOWY!"; }
            else if (wartosc > 80) { kolor = Brushes.Orange; opis = "Ostrzeżenie"; }

            lblWynik.Text = wartosc.ToString("0.0");
            lblWynik.Foreground = kolor;
            lblStatus.Text = opis;
            bar.Value = wartosc;
            bar.Foreground = kolor;

            _mainDot.Fill = kolor;
            if (wartosc > 150) { _mainDot.Width = 35; _mainDot.Height = 35; }
            else { _mainDot.Width = 30; _mainDot.Height = 30; }

            double x = Canvas.GetLeft(_mainDot);
            double y = Canvas.GetTop(_mainDot);
            var pomiar = new PomiarGazu(wartosc, x, y);

            _historia.Insert(0, pomiar);
            if (_historia.Count > 100) _historia.RemoveAt(99);
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