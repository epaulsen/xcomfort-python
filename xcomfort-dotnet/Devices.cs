using System.Reactive.Subjects;
using System.Text.Json;

namespace XComfortDotNet;

/// <summary>
/// Base class for device state
/// </summary>
public class DeviceState
{
    public JsonElement Raw { get; }

    public DeviceState(JsonElement payload)
    {
        Raw = payload;
    }

    public override string ToString() => $"DeviceState({Raw})";
}

/// <summary>
/// State for light devices
/// </summary>
public class LightState : DeviceState
{
    public bool Switch { get; }
    public int DimmValue { get; }

    public LightState(bool switchValue, int dimmValue, JsonElement payload) : base(payload)
    {
        Switch = switchValue;
        DimmValue = dimmValue;
    }

    public override string ToString() => $"LightState(Switch: {Switch}, DimmValue: {DimmValue})";
}

/// <summary>
/// State for RcTouch temperature/humidity sensors
/// </summary>
public class RcTouchState : DeviceState
{
    public double Temperature { get; }
    public double Humidity { get; }

    public RcTouchState(double temperature, double humidity, JsonElement payload) : base(payload)
    {
        Temperature = temperature;
        Humidity = humidity;
    }

    public override string ToString() => $"RcTouchState(Temperature: {Temperature}, Humidity: {Humidity})";
}

/// <summary>
/// State for heater devices
/// </summary>
public class HeaterState : DeviceState
{
    public HeaterState(JsonElement payload) : base(payload)
    {
    }

    public override string ToString() => $"HeaterState({Raw})";
}

/// <summary>
/// Base class for all bridge devices
/// </summary>
public class BridgeDevice
{
    protected readonly Bridge _bridge;
    
    public int DeviceId { get; }
    public string Name { get; }
    public BehaviorSubject<DeviceState?> State { get; }

    public BridgeDevice(Bridge bridge, int deviceId, string name)
    {
        _bridge = bridge;
        DeviceId = deviceId;
        Name = name;
        State = new BehaviorSubject<DeviceState?>(null);
    }

    public virtual void HandleState(JsonElement payload)
    {
        State.OnNext(new DeviceState(payload));
    }

    public override string ToString() => $"BridgeDevice({DeviceId}, \"{Name}\")";
}

/// <summary>
/// Light device (can be dimmable or non-dimmable)
/// </summary>
public class Light : BridgeDevice
{
    public bool Dimmable { get; }

    public Light(Bridge bridge, int deviceId, string name, bool dimmable) 
        : base(bridge, deviceId, name)
    {
        Dimmable = dimmable;
    }

    private int InterpretDimmValueFromPayload(bool switchValue, JsonElement payload)
    {
        if (!Dimmable)
            return 99;

        if (!switchValue)
        {
            var currentState = State.Value as LightState;
            return currentState?.DimmValue ?? 99;
        }

        return payload.TryGetProperty("dimmvalue", out var dimmValueElement) 
            ? dimmValueElement.GetInt32() 
            : 99;
    }

    public override void HandleState(JsonElement payload)
    {
        var switchValue = payload.GetProperty("switch").GetBoolean();
        var dimmValue = InterpretDimmValueFromPayload(switchValue, payload);

        State.OnNext(new LightState(switchValue, dimmValue, payload));
    }

    public async Task SwitchAsync(bool switchValue)
    {
        await _bridge.SwitchDeviceAsync(DeviceId, new Dictionary<string, object>
        {
            ["switch"] = switchValue
        });
    }

    public async Task DimmAsync(int value)
    {
        value = Math.Max(0, Math.Min(99, value));
        await _bridge.SlideDeviceAsync(DeviceId, new Dictionary<string, object>
        {
            ["dimmvalue"] = value
        });
    }

    public override string ToString() => 
        $"Light({DeviceId}, \"{Name}\", Dimmable: {Dimmable}, State: {State.Value})";
}

/// <summary>
/// RcTouch temperature and humidity sensor
/// </summary>
public class RcTouch : BridgeDevice
{
    public int CompId { get; }

    public RcTouch(Bridge bridge, int deviceId, string name, int compId) 
        : base(bridge, deviceId, name)
    {
        CompId = compId;
    }

    public override void HandleState(JsonElement payload)
    {
        var temperature = 0.0;
        var humidity = 0.0;

        if (payload.TryGetProperty("info", out var infoArray))
        {
            foreach (var info in infoArray.EnumerateArray())
            {
                if (!info.TryGetProperty("text", out var textElement))
                    continue;

                var text = textElement.GetString();
                if (text == "1222" && info.TryGetProperty("value", out var tempValue))
                {
                    temperature = double.Parse(tempValue.GetString()!);
                }
                else if (text == "1223" && info.TryGetProperty("value", out var humValue))
                {
                    humidity = double.Parse(humValue.GetString()!);
                }
            }
        }

        State.OnNext(new RcTouchState(temperature, humidity, payload));
    }

    public override string ToString() => 
        $"RcTouch({DeviceId}, \"{Name}\", CompId: {CompId})";
}

/// <summary>
/// Heater device
/// </summary>
public class Heater : BridgeDevice
{
    public int CompId { get; }

    public Heater(Bridge bridge, int deviceId, string name, int compId) 
        : base(bridge, deviceId, name)
    {
        CompId = compId;
    }

    public override void HandleState(JsonElement payload)
    {
        State.OnNext(new HeaterState(payload));
    }

    public override string ToString() => 
        $"Heater({DeviceId}, \"{Name}\", CompId: {CompId})";
}

/// <summary>
/// Shade/blind device
/// </summary>
public class Shade : BridgeDevice
{
    public int CompId { get; }

    public Shade(Bridge bridge, int deviceId, string name, int compId) 
        : base(bridge, deviceId, name)
    {
        CompId = compId;
    }

    public async Task SendStateAsync(int state)
    {
        await _bridge.SendMessageAsync(Messages.SET_DEVICE_SHADING_STATE, new Dictionary<string, object>
        {
            ["deviceId"] = DeviceId,
            ["state"] = state
        });
    }

    public Task MoveDownAsync() => SendStateAsync(1);
    public Task MoveUpAsync() => SendStateAsync(3);
    public Task MoveStopAsync() => SendStateAsync(2);

    public override string ToString() => 
        $"Shade({DeviceId}, \"{Name}\", CompId: {CompId})";
}
