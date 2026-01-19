using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Styling;
using MQTTnet;
using MQTTnet.Client;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;

namespace MonitorGazuApp;

// Klasa odpowiedzialna za połączenie IoT przez MQTT
// Oddzielona od głównego pliku MainWindow.axaml.cs
public class IoTConnection
{
    private IMqttClient? _mqttClient;
    private Action<double>? _onDataReceived;

    public IoTConnection(Action<double> onDataReceived)
    {
        _onDataReceived = onDataReceived;
    }

    public async Task<bool> ConnectAsync(string broker = "test.mosquitto.org", int port = 1883, string topic = "mojprojekt/lab/gaz")
    {
        try
        {
            var factory = new MqttFactory();
            _mqttClient = factory.CreateMqttClient();
            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(broker, port)
                .Build();

            _mqttClient.ApplicationMessageReceivedAsync += async msg =>
            {
                string text = Encoding.UTF8.GetString(msg.ApplicationMessage.PayloadSegment);
                if (double.TryParse(text.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
                {
                    _onDataReceived?.Invoke(value);
                }
                await Task.CompletedTask;
            };

            await _mqttClient.ConnectAsync(options);
            await _mqttClient.SubscribeAsync(topic);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_mqttClient != null && _mqttClient.IsConnected)
        {
            await _mqttClient.DisconnectAsync();
        }
    }
}

// Okno dialogowe do wyboru urządzenia IoT
public partial class IoTConnectionWindow : Window
{
    public IoTConnectionWindow()
    {
        Width = 500;
        Height = 420;
        Title = "Połączenie IoT";
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;

        var mainStack = new StackPanel
        {
            Margin = new Avalonia.Thickness(30),
            Spacing = 15
        };

        var title = new TextBlock
        {
            Text = "Wybierz urządzenie IoT",
            FontSize = 26,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Foreground = Brushes.Black
        };

        var subtitle = new TextBlock
        {
            Text = "Wykryte czujniki w sieci WiFi/MQTT:",
            Foreground = new SolidColorBrush(Color.Parse("#666666")),
            FontSize = 14,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 0, 0, 10)
        };

        var deviceList = new ListBox
        {
            Height = 160,
            Background = new SolidColorBrush(Color.Parse("#F8F8F8")),
            BorderBrush = new SolidColorBrush(Color.Parse("#CCCCCC")),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(8),
            Padding = new Avalonia.Thickness(5),
            ItemsSource = new[]
            {
                "🟢 ESP8266 - Hala Główna (Sygnał: 98%)",
                "🟢 Raspberry Pi - Serwerownia (Sygnał: 85%)",
                "🔴 Czujnik Magazyn (Offline)"
            },
            SelectedIndex = 0
        };

        // Style dla elementów listy
        var itemStyle = new Avalonia.Styling.Style(x => x.OfType<ListBoxItem>());
        itemStyle.Setters.Add(new Avalonia.Styling.Setter(ListBoxItem.ForegroundProperty, new SolidColorBrush(Color.Parse("#222222"))));
        itemStyle.Setters.Add(new Avalonia.Styling.Setter(ListBoxItem.FontSizeProperty, 15.0));
        itemStyle.Setters.Add(new Avalonia.Styling.Setter(ListBoxItem.PaddingProperty, new Avalonia.Thickness(12, 10)));
        itemStyle.Setters.Add(new Avalonia.Styling.Setter(ListBoxItem.MarginProperty, new Avalonia.Thickness(0, 3)));
        
        deviceList.Styles.Add(itemStyle);

        var connectBtn = new Button
        {
            Content = "POŁĄCZ TERAZ",
            Background = new SolidColorBrush(Color.Parse("#007ACC")),
            Foreground = Brushes.White,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Padding = new Avalonia.Thickness(15),
            FontWeight = FontWeight.Bold,
            FontSize = 16,
            Height = 50,
            CornerRadius = new Avalonia.CornerRadius(8),
            Margin = new Avalonia.Thickness(0, 15, 0, 0)
        };
        connectBtn.Click += (s, e) => Close(true);

        var cancelBtn = new Button
        {
            Content = "Anuluj",
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.Parse("#666666")),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            FontSize = 14,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        cancelBtn.Click += (s, e) => Close(false);

        mainStack.Children.Add(title);
        mainStack.Children.Add(subtitle);
        mainStack.Children.Add(deviceList);
        mainStack.Children.Add(connectBtn);
        mainStack.Children.Add(cancelBtn);

        Content = mainStack;
    }
}
