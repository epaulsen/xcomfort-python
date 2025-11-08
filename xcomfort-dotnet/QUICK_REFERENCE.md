# Quick Reference: Python vs .NET

This is a quick reference guide for developers familiar with the Python version.

## Installation & Setup

### Python
```bash
pip install xcomfort
```

### .NET
```bash
cd xcomfort-dotnet
dotnet restore
dotnet build
```

## Basic Usage

### Python
```python
import asyncio
from xcomfort import Bridge

async def main():
    bridge = Bridge("<ip>", "<key>")
    runTask = asyncio.create_task(bridge.run())
    devices = await bridge.get_devices()
    await bridge.close()
    await runTask

asyncio.run(main())
```

### .NET
```csharp
using XComfortDotNet;

var bridge = new Bridge("<ip>", "<key>");
var runTask = Task.Run(() => bridge.RunAsync());
var devices = await bridge.GetDevicesAsync();
await bridge.CloseAsync();
await runTask;
bridge.Dispose();
```

## API Reference

| Python | .NET | Notes |
|--------|------|-------|
| `Bridge(ip, key)` | `new Bridge(ip, key)` | Constructor |
| `bridge.run()` | `bridge.RunAsync()` | Start connection |
| `bridge.get_devices()` | `bridge.GetDevicesAsync()` | Get all devices |
| `bridge.get_rooms()` | `bridge.GetRoomsAsync()` | Get all rooms |
| `bridge.close()` | `bridge.CloseAsync()` | Close connection |
| N/A | `bridge.Dispose()` | Resource cleanup |
| `bridge.logger = func` | `bridge.Logger = action` | Set logger |

## Device Control

### Light Control

**Python:**
```python
await light.switch(True)
await light.dimm(50)
```

**.NET:**
```csharp
await light.SwitchAsync(true);
await light.DimmAsync(50);
```

### Shade Control

**Python:**
```python
await shade.move_down()
await shade.move_up()
await shade.move_stop()
```

**.NET:**
```csharp
await shade.MoveDownAsync();
await shade.MoveUpAsync();
await shade.MoveStopAsync();
```

### Room Temperature

**Python:**
```python
await room.set_target_temperature(22.0)
await room.set_mode(RctMode.Comfort)
```

**.NET:**
```csharp
await room.SetTargetTemperatureAsync(22.0);
await room.SetModeAsync(RctMode.Comfort);
```

## State Monitoring

### Python
```python
device.state.subscribe(lambda state: print(f"State: {state}"))
```

### .NET
```csharp
device.State.Subscribe(state => Console.WriteLine($"State: {state}"));
```

## Enumerations

| Python | .NET |
|--------|------|
| `State.Ready` | `State.Ready` |
| `RctMode.Comfort` | `RctMode.Comfort` |
| `Messages.SET_DEVICE_STATE` | `Messages.SET_DEVICE_STATE` |

## Device Types

| Type | Python | .NET |
|------|--------|------|
| Light | `Light` | `Light` |
| Shade | `Shade` | `Shade` |
| Heater | `Heater` | `Heater` |
| Sensor | `RcTouch` | `RcTouch` |

## Common Patterns

### Error Handling

**Python:**
```python
try:
    await bridge.get_devices()
except Exception as e:
    print(f"Error: {e}")
```

**.NET:**
```csharp
try
{
    await bridge.GetDevicesAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
```

### Resource Management

**Python:**
```python
# No explicit cleanup needed
# Connection closed with bridge.close()
```

**.NET:**
```csharp
using var bridge = new Bridge("<ip>", "<key>");
// Automatically disposed at end of scope
```

### Logging

**Python:**
```python
bridge.logger = lambda msg: print(msg)
```

**.NET:**
```csharp
bridge.Logger = msg => Console.WriteLine(msg);
```

## Type Conversions

| Python Type | .NET Type |
|-------------|-----------|
| `str` | `string` |
| `int` | `int` |
| `float` | `double` |
| `bool` | `bool` |
| `dict` | `Dictionary<TKey, TValue>` |
| `list` | `List<T>` or `IEnumerable<T>` |
| `None` | `null` |

## Async Patterns

| Python | .NET |
|--------|------|
| `async def func():` | `async Task FuncAsync()` |
| `await func()` | `await FuncAsync()` |
| `asyncio.create_task()` | `Task.Run()` |
| `asyncio.sleep(5)` | `Task.Delay(TimeSpan.FromSeconds(5))` |

## Dependencies

### Python
- aiohttp
- rx
- pycryptodome

### .NET
- System.Reactive
- BouncyCastle.Cryptography

## File Structure

```
Python:                    .NET:
xcomfort/                 xcomfort-dotnet/
├── __init__.py          ├── Program.cs
├── bridge.py            ├── Bridge.cs
├── connection.py        ├── Connection.cs
├── devices.py           ├── Devices.cs
├── messages.py          └── Messages.cs
└── ...
```

## Key Differences

1. **Naming**: Python uses snake_case, .NET uses PascalCase/camelCase
2. **Async**: Python uses `async`/`await`, .NET uses `async Task`/`await`
3. **Types**: Python is dynamically typed, .NET is statically typed
4. **Disposal**: .NET requires explicit disposal with `IDisposable`
5. **Namespaces**: .NET uses `namespace`, Python uses modules
6. **Properties**: .NET uses properties, Python uses attributes
