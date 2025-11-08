# xComfort .NET

.NET 9 console application for communicating with Eaton xComfort Bridge. This is a translation of the [xcomfort-python](https://github.com/jankrib/xcomfort-python) library.

## Features

- Async/await pattern for all operations
- Reactive state management using System.Reactive (Rx.NET)
- Secure WebSocket communication with RSA + AES encryption
- Support for multiple device types:
  - **Lights** (dimmable and non-dimmable)
  - **Shades/Blinds** (up/down/stop control)
  - **Heaters**
  - **RcTouch** (temperature and humidity sensors)
- Room heating control with multiple modes (Cool, Eco, Comfort)
- Automatic reconnection on connection loss

## Architecture

The application consists of several key components:

### Core Classes

- **Bridge**: Main class for connecting to and controlling the xComfort Bridge
- **SecureBridgeConnection**: Handles WebSocket connection with encryption
- **Devices**: Device models (Light, Shade, Heater, RcTouch)
- **Room**: Room with heating control capabilities
- **Messages**: Enum of protocol message types

### Security

The connection uses a multi-layer security approach:
1. WebSocket connection establishment
2. RSA public key exchange
3. AES-256-CBC encryption for all messages
4. Authentication with hashed credentials (SHA-256)
5. Token-based session management

## Usage

### Basic Example

```csharp
using XComfortDotNet;

// Create bridge instance
var bridge = new Bridge("<ip_address>", "<auth_key>");

// Optional: Enable logging
bridge.Logger = Console.WriteLine;

// Start bridge connection in background
var runTask = Task.Run(() => bridge.RunAsync());

// Get all devices
var devices = await bridge.GetDevicesAsync();

// Subscribe to device state changes
foreach (var device in devices.Values)
{
    device.State.Subscribe(state => 
    {
        Console.WriteLine($"Device [{device.DeviceId}] '{device.Name}': {state}");
    });
}

// Wait and observe state changes
await Task.Delay(TimeSpan.FromSeconds(50));

// Close connection
await bridge.CloseAsync();
await runTask;
bridge.Dispose();
```

### Controlling Lights

```csharp
var devices = await bridge.GetDevicesAsync();

// Turn on a light
var light = devices.Values.OfType<Light>().First();
await light.SwitchAsync(true);

// Dim a light (0-99)
if (light.Dimmable)
{
    await light.DimmAsync(50);
}

// Turn off all lights
foreach (var l in devices.Values.OfType<Light>())
{
    await l.SwitchAsync(false);
}
```

### Controlling Shades

```csharp
var shade = devices.Values.OfType<Shade>().First();

// Move shade down
await shade.MoveDownAsync();

// Stop movement
await shade.MoveStopAsync();

// Move shade up
await shade.MoveUpAsync();
```

### Room Temperature Control

```csharp
var rooms = await bridge.GetRoomsAsync();
var room = rooms.Values.First();

// Subscribe to temperature changes
room.State.Subscribe(state =>
{
    Console.WriteLine($"Room '{room.Name}': {state.Temperature}°C, Setpoint: {state.Setpoint}°C");
});

// Set target temperature
await room.SetTargetTemperatureAsync(22.0);

// Change heating mode
await room.SetModeAsync(RctMode.Comfort);
```

## Building and Running

### Prerequisites

- .NET 9.0 SDK or later

### Build

```bash
cd xcomfort-dotnet
dotnet build
```

### Run

```bash
dotnet run
```

Before running, update `Program.cs` with your bridge IP address and authentication key:

```csharp
const string ipAddress = "<your_bridge_ip>";
const string authKey = "<your_auth_key>";
```

## Dependencies

- **System.Reactive** (6.1.0): Reactive Extensions for .NET
- **BouncyCastle.Cryptography** (2.6.2): Cryptography library for RSA operations

## Project Structure

```
xcomfort-dotnet/
├── Bridge.cs          - Main bridge class with device/room management
├── Connection.cs      - WebSocket connection with encryption
├── Devices.cs         - Device models and state classes
├── Messages.cs        - Protocol message type definitions
├── Program.cs         - Example usage
└── README.md          - This file
```

## Comparison with Python Version

This .NET implementation closely follows the Python library architecture:

| Python | .NET | Notes |
|--------|------|-------|
| `asyncio` | `async/await` | Native async patterns |
| `rx` (RxPY) | `System.Reactive` | Reactive extensions |
| `aiohttp` | `ClientWebSocket` | WebSocket client |
| `pycryptodome` | `BouncyCastle` | RSA encryption |
| `Crypto.Cipher.AES` | `System.Security.Cryptography.Aes` | AES encryption |
| `BehaviorSubject` | `BehaviorSubject<T>` | Same reactive pattern |

## Protocol Details

The xComfort Bridge uses a custom WebSocket-based protocol with:

1. **Message Format**: JSON messages with encryption
2. **Message Types**: 100+ different message types for various operations
3. **Encryption**: AES-256-CBC for message payload
4. **Authentication**: Double SHA-256 hash with salt
5. **State Management**: Push-based updates for device/room state changes

## License

This project follows the same license as the original Python library (MIT License).

## Credits

This is a .NET translation of the [xcomfort-python](https://github.com/jankrib/xcomfort-python) library by Jan Kristian Bjerke.

Original Python library:
- Author: Jan Kristian Bjerke
- Repository: https://github.com/jankrib/xcomfort-python

## Disclaimer

This is an unofficial implementation. Use at your own risk. The xComfort protocol and authentication mechanisms were reverse-engineered from the official mobile application.
