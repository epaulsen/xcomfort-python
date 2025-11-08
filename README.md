# xcomfort-python
Unofficial python package for communicating with Eaton xComfort Bridge

## Versions

This repository contains two implementations:

- **Python** (in `/xcomfort/`) - Original Python library
- **.NET 9** (in `/xcomfort-dotnet/`) - C# translation with full feature parity

Both versions provide the same functionality for controlling xComfort Bridge devices.

## Python Usage
```python
import asyncio
from xcomfort import Bridge

def observe_device(device):
    device.state.subscribe(lambda state: print(f"Device state [{device.device_id}] '{device.name}': {state}"))

async def main():
    bridge = Bridge(<ip_address>, <auth_key>)

    runTask = asyncio.create_task(bridge.run())

    devices = await bridge.get_devices()

    for device in devices.values():
        observe_device(device)
        
    # Wait 50 seconds. Try flipping the light switch manually while you wait
    await asyncio.sleep(50) 

    # Turn off all the lights.
    # for device in devices.values():
    #     await device.switch(False)
    #
    # await asyncio.sleep(5)

    await bridge.close()
    await runTask

asyncio.run(main())

```

## .NET 9 Usage

```csharp
using XComfortDotNet;

var bridge = new Bridge("<ip_address>", "<auth_key>");
var runTask = Task.Run(() => bridge.RunAsync());

var devices = await bridge.GetDevicesAsync();

foreach (var device in devices.Values)
{
    device.State.Subscribe(state => 
        Console.WriteLine($"Device state [{device.DeviceId}] '{device.Name}': {state}"));
}

await Task.Delay(TimeSpan.FromSeconds(50));

await bridge.CloseAsync();
await runTask;
bridge.Dispose();
```

See [xcomfort-dotnet/README.md](xcomfort-dotnet/README.md) for complete .NET documentation.

## Tests

### Python
```python
python -m pytest
```

### .NET
```bash
cd xcomfort-dotnet
dotnet build
dotnet test  # (tests not yet implemented)
```
