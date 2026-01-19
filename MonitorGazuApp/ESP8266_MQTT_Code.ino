/*
 * ESP8266 - Monitor Gazu - MQTT
 * 
 * Ten kod zastępuje poprzedni kod z Azure IoT Central
 * Używa prostszego protokołu MQTT zamiast Azure
 * 
 * Zachowane z poprzedniego kodu:
 * - WiFi credentials (Nazwa_Sieci, haslo_Sieci)
 * - Pin czujnika MQ-2: A0
 * - Prędkość Serial: 9600
 * - Interwał wysyłania: 5 sekund
 * - ID urządzenia: 20cbkealzej
 */

#include <ESP8266WiFi.h>
#include <PubSubClient.h>

// ===== TWOJE DANE WiFi =====
const char* ssid = "Nazwa_Sieci";           // ← Twoja sieć WiFi
const char* password = "haslo_Sieci";       // ← Twoje hasło WiFi

// ===== KONFIGURACJA MQTT =====
const char* mqtt_server = "test.mosquitto.org";  // Darmowy broker MQTT
const int mqtt_port = 1883;
const char* mqtt_topic = "mojprojekt/lab/gaz";   // Topic do wysyłania danych

WiFiClient espClient;
PubSubClient client(espClient);

// ===== PIN CZUJNIKA MQ-2 =====
#define MQ2_PIN A0  // Twój czujnik MQ-2 na pinie A0

// Zmienne pomocnicze
unsigned long lastTick = 0;
const unsigned long sendInterval = 5000;  // Wysyłaj co 5 sekund (jak w Twoim kodzie)

void setup() {
  Serial.begin(9600);  // Twoja prędkość Serial
  pinMode(MQ2_PIN, INPUT);
  
  // ===== POŁĄCZENIE Z WiFi =====
  Serial.println();
  Serial.print("Łączenie z WiFi: ");
  Serial.println(ssid);
  
  WiFi.begin(ssid, password);
  
  while (WiFi.status() != WL_CONNECTED) {
    delay(500);
    Serial.print(".");
  }
  
  Serial.println();
  Serial.println("✅ Połączono z WiFi!");
  Serial.print("IP Address: ");
  Serial.println(WiFi.localIP());
  
  // ===== KONFIGURACJA MQTT =====
  client.setServer(mqtt_server, mqtt_port);
  
  Serial.println("Device initialized, attempting to connect...");
}

void reconnectMQTT() {
  while (!client.connected()) {
    Serial.print("Łączenie z MQTT broker...");
    
    // Unikalny ID klienta (jak w Twoim kodzie: 20cbkealzej)
    String clientId = "ESP8266-20cbkealzej";
    
    if (client.connect(clientId.c_str())) {
      Serial.println(" ✅ Połączono!");
    } else {
      Serial.print(" ❌ Błąd, rc=");
      Serial.print(client.state());
      Serial.println(" Próba ponowna za 5s...");
      delay(5000);
    }
  }
}

void loop() {
  // Sprawdź połączenie MQTT
  if (!client.connected()) {
    reconnectMQTT();
  }
  client.loop();
  
  unsigned long ms = millis();
  
  // ===== WYSYŁANIE CO 5 SEKUND (jak w Twoim kodzie) =====
  if (ms - lastTick > sendInterval) {
    lastTick = ms;
    
    // Odczyt z czujnika MQ-2
    int mq2_value = analogRead(MQ2_PIN);
    
    // Konwersja na ppm (0-1024 → 20-250 ppm)
    // Możesz dostosować te wartości do swojego czujnika
    float gasLevel = map(mq2_value, 0, 1024, 20, 250);
    
    // Format wiadomości (podobny do Twojego: {"GasLevel": value})
    char msg[64];
    snprintf(msg, sizeof(msg), "%.1f", gasLevel);
    
    // Wysłanie przez MQTT
    if (client.publish(mqtt_topic, msg)) {
      Serial.print("✅ Telemetry sent: GasLevel = ");
      Serial.print(gasLevel);
      Serial.print(" ppm (raw: ");
      Serial.print(mq2_value);
      Serial.println(")");
    } else {
      Serial.println("❌ Sending telemetry failed");
    }
  }
}
