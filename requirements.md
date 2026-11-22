# Electronics list

## Controller and wiring

- 1 ESP32 DevKit (buy 2, so you have a spare)
- 1 or 2 breadboards, plus jumper wires (male-male and male-female)
- Screw terminal board or perfboard, for the final version so wires don't fall out
- Data-capable USB cable and a 5V USB adapter to power the ESP32

## Sensors

- Waterproof ultrasonic sensor (JSN-SR04T): tank level
- Turbidity sensor module: water clarity
- pH sensor module (PH-4502C type): acidity. Also buy pH 4 and pH 7 buffer powder for calibration.
- Rain sensor module: detects rainfall
- Capacitive soil moisture sensor: planter dryness
- Water flow sensor (YF-S201): liters collected and used
- DS18B20 waterproof temperature sensor (optional): water temperature, which helps the pH reading

## Outputs

- 2-channel 5V relay module: one channel for the pump, one for the valve
- 12V small water pump
- 12V solenoid valve (normally closed)
- 12V 2A power adapter, for the pump and valve only
- 2 diodes (1N4007): protect the relay from voltage spikes from the pump and valve

## Small parts

- Resistors, 10kΩ and 20kΩ (a few of each): to lower 5V signals to the 3.3V the ESP32 can read
- 0.96" OLED display (optional): shows the tank level on the device itself, which looks good in a demo
- Waterproof project box: keeps the ESP32 dry
- Tubing, connectors and containers: agree with the mechanical team who buys these

## Two things to know before you wire

- The ESP32 pins only handle 3.3V, but the pH sensor, turbidity sensor and ultrasonic sensor send 5V signals. Use the resistor pairs (voltage divider) on those.
- Wi-Fi interferes with some ESP32 analog pins. Use the pins GPIO32 to GPIO39 for your analog sensors.

# Software list

- **Firmware:** Arduino IDE with the ESP32 board package, and the libraries `PubSubClient`, `ArduinoJson`, `OneWire` and `DallasTemperature`
- **MQTT broker:** HiveMQ Cloud free account. For the complete version, use this instead of the public broker, because it has a username and password.
- **Dashboard and logic:** Node-RED with the Node-RED Dashboard add-on. It runs on a laptop, and the dashboard opens on any phone on the same Wi-Fi.
- **Database:** the SQLite node in Node-RED, to store all readings for later
- **Alerts:** a Telegram bot connected through Node-RED, so your phone gets a message like "Water quality bad, valve closed"
- **Analytics:** Python with `pandas` and `matplotlib`, reading the exported readings to make the water-savings charts
- **Documentation:** GitHub for the code, and draw.io (or Fritzing) for the wiring diagram
