using System.Reactive.Subjects;
using System.Text.Json;

namespace XComfortDotNet;

/// <summary>
/// Bridge state
/// </summary>
public enum State
{
    Uninitialized = 0,
    Initializing = 1,
    Ready = 2,
    Closing = 10
}

/// <summary>
/// Room controller mode
/// </summary>
public enum RctMode
{
    Cool = 1,
    Eco = 2,
    Comfort = 3
}

/// <summary>
/// Room controller state
/// </summary>
public enum RctState
{
    Idle = 0,
    Active = 2
}

/// <summary>
/// Temperature range for a mode
/// </summary>
public class RctModeRange
{
    public double Min { get; }
    public double Max { get; }

    public RctModeRange(double min, double max)
    {
        Min = min;
        Max = max;
    }
}

/// <summary>
/// Component state
/// </summary>
public class CompState
{
    public JsonElement Raw { get; }

    public CompState(JsonElement raw)
    {
        Raw = raw;
    }

    public override string ToString() => $"CompState({Raw})";
}

/// <summary>
/// Component (group of devices)
/// </summary>
public class Comp
{
    private readonly Bridge _bridge;
    
    public int CompId { get; }
    public int CompType { get; }
    public string Name { get; }
    public BehaviorSubject<CompState?> State { get; }

    public Comp(Bridge bridge, int compId, int compType, string name)
    {
        _bridge = bridge;
        CompId = compId;
        CompType = compType;
        Name = name;
        State = new BehaviorSubject<CompState?>(null);
    }

    public void HandleState(JsonElement payload)
    {
        State.OnNext(new CompState(payload));
    }

    public override string ToString() => $"Comp({CompId}, \"{Name}\", CompType: {CompType})";
}

/// <summary>
/// Room state
/// </summary>
public class RoomState
{
    public double? Setpoint { get; }
    public double? Temperature { get; }
    public double? Humidity { get; }
    public double Power { get; }
    public RctMode Mode { get; }
    public RctState RctState { get; }
    public JsonElement Raw { get; }

    public RoomState(double? setpoint, double? temperature, double? humidity, 
        double power, RctMode mode, RctState rctState, JsonElement raw)
    {
        Setpoint = setpoint;
        Temperature = temperature;
        Humidity = humidity;
        Power = power;
        Mode = mode;
        RctState = rctState;
        Raw = raw;
    }

    public override string ToString() => 
        $"RoomState(Setpoint: {Setpoint}, Temp: {Temperature}, Humidity: {Humidity}, Mode: {Mode}, State: {RctState}, Power: {Power})";
}

/// <summary>
/// Room with heating control
/// </summary>
public class Room
{
    private readonly Bridge _bridge;
    
    public int RoomId { get; }
    public string Name { get; }
    public BehaviorSubject<RoomState?> State { get; }
    public Dictionary<RctMode, double> ModeSetpoints { get; }

    public Room(Bridge bridge, int roomId, string name)
    {
        _bridge = bridge;
        RoomId = roomId;
        Name = name;
        State = new BehaviorSubject<RoomState?>(null);
        ModeSetpoints = new Dictionary<RctMode, double>();
    }

    public void HandleState(JsonElement payload)
    {
        var oldState = State.Value;
        JsonElement mergedPayload = payload;

        if (oldState != null)
        {
            // Merge with old state - in a real implementation, you'd need proper JSON merging
            mergedPayload = payload;
        }

        var setpoint = mergedPayload.TryGetProperty("setpoint", out var sp) ? (double?)sp.GetDouble() : null;
        var temperature = mergedPayload.TryGetProperty("temp", out var temp) ? (double?)temp.GetDouble() : null;
        var humidity = mergedPayload.TryGetProperty("humidity", out var hum) ? (double?)hum.GetDouble() : null;
        var power = mergedPayload.TryGetProperty("power", out var pow) ? pow.GetDouble() : 0.0;
        
        var mode = RctMode.Eco;
        if (mergedPayload.TryGetProperty("currentMode", out var currentMode))
        {
            mode = (RctMode)currentMode.GetInt32();
        }
        else if (mergedPayload.TryGetProperty("mode", out var modeProperty))
        {
            mode = (RctMode)modeProperty.GetInt32();
        }

        // Store mode setpoints if available
        if (mergedPayload.TryGetProperty("modes", out var modes))
        {
            foreach (var modeItem in modes.EnumerateArray())
            {
                var modeValue = (RctMode)modeItem.GetProperty("mode").GetInt32();
                var setValue = modeItem.GetProperty("value").GetDouble();
                ModeSetpoints[modeValue] = setValue;
            }
        }

        var currentState = mergedPayload.TryGetProperty("state", out var st) 
            ? (RctState)st.GetInt32() 
            : RctState.Idle;

        State.OnNext(new RoomState(setpoint, temperature, humidity, power, mode, currentState, mergedPayload));
    }

    public async Task SetTargetTemperatureAsync(double setpoint)
    {
        var currentState = State.Value;
        if (currentState == null) return;

        // Validate setpoint within allowed range
        var setpointRange = _bridge.RctSetpointAllowedValues[currentState.Mode];
        
        if (setpoint > setpointRange.Max)
            setpoint = setpointRange.Max;
        
        if (setpoint < setpointRange.Min)
            setpoint = setpointRange.Min;

        // Store new setpoint for current mode
        ModeSetpoints[currentState.Mode] = setpoint;

        await _bridge.SendMessageAsync(Messages.SET_HEATING_STATE, new Dictionary<string, object>
        {
            ["roomId"] = RoomId,
            ["mode"] = (int)currentState.Mode,
            ["state"] = (int)currentState.RctState,
            ["setpoint"] = setpoint,
            ["confirmed"] = false
        });
    }

    public async Task SetModeAsync(RctMode mode)
    {
        var currentState = State.Value;
        if (currentState == null) return;

        // Find setpoint for the mode we're setting
        var newSetpoint = ModeSetpoints.TryGetValue(mode, out var sp) ? sp : 20.0;

        await _bridge.SendMessageAsync(Messages.SET_HEATING_STATE, new Dictionary<string, object>
        {
            ["roomId"] = RoomId,
            ["mode"] = (int)mode,
            ["state"] = (int)currentState.RctState,
            ["setpoint"] = newSetpoint,
            ["confirmed"] = false
        });
    }

    public override string ToString() => $"Room({RoomId}, \"{Name}\")";
}

/// <summary>
/// Main Bridge class for connecting to and controlling xComfort Bridge
/// </summary>
public class Bridge : IDisposable
{
    private readonly string _ipAddress;
    private readonly string _authKey;
    private readonly Dictionary<int, Comp> _comps;
    private readonly Dictionary<int, BridgeDevice> _devices;
    private readonly Dictionary<int, Room> _rooms;
    private SecureBridgeConnection? _connection;
    private IDisposable? _connectionSubscription;
    private CancellationTokenSource? _runCancellation;

    public State State { get; private set; }
    public Dictionary<RctMode, RctModeRange> RctSetpointAllowedValues { get; }
    public Action<string>? Logger { get; set; }

    public Bridge(string ipAddress, string authKey)
    {
        _ipAddress = ipAddress;
        _authKey = authKey;
        _comps = new Dictionary<int, Comp>();
        _devices = new Dictionary<int, BridgeDevice>();
        _rooms = new Dictionary<int, Room>();
        State = State.Uninitialized;
        
        // Values determined from using setpoint slider in app
        RctSetpointAllowedValues = new Dictionary<RctMode, RctModeRange>
        {
            [RctMode.Cool] = new RctModeRange(5.0, 20.0),
            [RctMode.Eco] = new RctModeRange(10.0, 30.0),
            [RctMode.Comfort] = new RctModeRange(18.0, 40.0)
        };
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (State != State.Uninitialized)
        {
            throw new Exception("Run can only be called once at a time");
        }

        State = State.Initializing;
        _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        while (State != State.Closing && !_runCancellation.Token.IsCancellationRequested)
        {
            try
            {
                await ConnectAsync();
                await _connection!.PumpAsync(_runCancellation.Token);
            }
            catch (Exception e)
            {
                Logger?.Invoke($"Error: {e.Message}");
                await Task.Delay(5000, _runCancellation.Token);
            }

            _connectionSubscription?.Dispose();
        }

        State = State.Uninitialized;
    }

    public async Task SwitchDeviceAsync(int deviceId, Dictionary<string, object> message)
    {
        var payload = new Dictionary<string, object> { ["deviceId"] = deviceId };
        foreach (var kvp in message)
        {
            payload[kvp.Key] = kvp.Value;
        }
        await SendMessageAsync(Messages.ACTION_SWITCH_DEVICE, payload);
    }

    public async Task SlideDeviceAsync(int deviceId, Dictionary<string, object> message)
    {
        var payload = new Dictionary<string, object> { ["deviceId"] = deviceId };
        foreach (var kvp in message)
        {
            payload[kvp.Key] = kvp.Value;
        }
        await SendMessageAsync(Messages.ACTION_SLIDE_DEVICE, payload);
    }

    public async Task SendMessageAsync(Messages messageType, object message)
    {
        if (_connection != null)
        {
            await _connection.SendMessageAsync(messageType, message);
        }
    }

    private void AddComp(Comp comp)
    {
        _comps[comp.CompId] = comp;
    }

    private void AddDevice(BridgeDevice device)
    {
        _devices[device.DeviceId] = device;
    }

    private void AddRoom(Room room)
    {
        _rooms[room.RoomId] = room;
    }

    private void HandleSetDeviceState(JsonElement payload)
    {
        try
        {
            var deviceId = payload.GetProperty("deviceId").GetInt32();
            if (_devices.TryGetValue(deviceId, out var device))
            {
                device.HandleState(payload);
            }
        }
        catch
        {
            // Device not found
        }
    }

    private void HandleSetStateInfo(JsonElement payload)
    {
        foreach (var item in payload.GetProperty("item").EnumerateArray())
        {
            if (item.TryGetProperty("deviceId", out var deviceIdProp))
            {
                var deviceId = deviceIdProp.GetInt32();
                if (_devices.TryGetValue(deviceId, out var device))
                {
                    device.HandleState(item);
                }
            }
            else if (item.TryGetProperty("roomId", out var roomIdProp))
            {
                var roomId = roomIdProp.GetInt32();
                if (_rooms.TryGetValue(roomId, out var room))
                {
                    room.HandleState(item);
                }
            }
            else if (item.TryGetProperty("compId", out var compIdProp))
            {
                var compId = compIdProp.GetInt32();
                if (_comps.TryGetValue(compId, out var comp))
                {
                    comp.HandleState(item);
                }
            }
            else
            {
                Logger?.Invoke($"Unknown state info: {item}");
            }
        }
    }

    private Comp CreateCompFromPayload(JsonElement payload)
    {
        var compId = payload.GetProperty("compId").GetInt32();
        var name = payload.GetProperty("name").GetString()!;
        var compType = payload.GetProperty("compType").GetInt32();

        return new Comp(this, compId, compType, name);
    }

    private BridgeDevice CreateDeviceFromPayload(JsonElement payload)
    {
        var deviceId = payload.GetProperty("deviceId").GetInt32();
        var name = payload.GetProperty("name").GetString()!;
        var devType = payload.GetProperty("devType").GetInt32();
        var compId = payload.TryGetProperty("compId", out var compIdProp) ? compIdProp.GetInt32() : 0;

        return devType switch
        {
            100 or 101 => new Light(this, deviceId, name, payload.GetProperty("dimmable").GetBoolean()),
            102 => new Shade(this, deviceId, name, compId),
            440 => new Heater(this, deviceId, name, compId),
            450 => new RcTouch(this, deviceId, name, compId),
            _ => new BridgeDevice(this, deviceId, name)
        };
    }

    private Room CreateRoomFromPayload(JsonElement payload)
    {
        var roomId = payload.GetProperty("roomId").GetInt32();
        var name = payload.GetProperty("name").GetString()!;

        return new Room(this, roomId, name);
    }

    private void HandleCompPayload(JsonElement payload)
    {
        var compId = payload.GetProperty("compId").GetInt32();

        if (!_comps.TryGetValue(compId, out var comp))
        {
            comp = CreateCompFromPayload(payload);
            AddComp(comp);
        }

        comp.HandleState(payload);
    }

    private void HandleDevicePayload(JsonElement payload)
    {
        var deviceId = payload.GetProperty("deviceId").GetInt32();

        if (!_devices.TryGetValue(deviceId, out var device))
        {
            device = CreateDeviceFromPayload(payload);
            AddDevice(device);
        }

        device.HandleState(payload);
    }

    private void HandleRoomPayload(JsonElement payload)
    {
        var roomId = payload.GetProperty("roomId").GetInt32();

        if (!_rooms.TryGetValue(roomId, out var room))
        {
            room = CreateRoomFromPayload(payload);
            AddRoom(room);
        }

        room.HandleState(payload);
    }

    private void HandleSetAllData(JsonElement payload)
    {
        if (payload.TryGetProperty("lastItem", out _))
        {
            State = State.Ready;
        }

        if (payload.TryGetProperty("devices", out var devices))
        {
            foreach (var devicePayload in devices.EnumerateArray())
            {
                try
                {
                    HandleDevicePayload(devicePayload);
                }
                catch (Exception e)
                {
                    Logger?.Invoke($"Failed to handle device payload: {e.Message}");
                }
            }
        }

        if (payload.TryGetProperty("comps", out var comps))
        {
            foreach (var compPayload in comps.EnumerateArray())
            {
                try
                {
                    HandleCompPayload(compPayload);
                }
                catch (Exception e)
                {
                    Logger?.Invoke($"Failed to handle comp payload: {e.Message}");
                }
            }
        }

        if (payload.TryGetProperty("rooms", out var rooms))
        {
            foreach (var roomPayload in rooms.EnumerateArray())
            {
                try
                {
                    HandleRoomPayload(roomPayload);
                }
                catch (Exception e)
                {
                    Logger?.Invoke($"Failed to handle room payload: {e.Message}");
                }
            }
        }

        if (payload.TryGetProperty("roomHeating", out var roomHeating))
        {
            foreach (var roomPayload in roomHeating.EnumerateArray())
            {
                try
                {
                    HandleRoomPayload(roomPayload);
                }
                catch (Exception e)
                {
                    Logger?.Invoke($"Failed to handle room payload: {e.Message}");
                }
            }
        }
    }

    private void HandleUnknown(Messages messageType, JsonElement payload)
    {
        Logger?.Invoke($"Unhandled package [{messageType}]: {payload}");
    }

    private void OnMessage(JsonElement message)
    {
        if (!message.TryGetProperty("payload", out var payload))
        {
            Logger?.Invoke($"Not known: {message}");
            return;
        }

        var messageType = (Messages)message.GetProperty("type_int").GetInt32();

        try
        {
            switch (messageType)
            {
                case Messages.SET_DEVICE_STATE:
                    HandleSetDeviceState(payload);
                    break;
                case Messages.SET_STATE_INFO:
                    HandleSetStateInfo(payload);
                    break;
                case Messages.SET_ALL_DATA:
                    HandleSetAllData(payload);
                    break;
                default:
                    HandleUnknown(messageType, payload);
                    break;
            }
        }
        catch (Exception e)
        {
            Logger?.Invoke($"Unknown error with {messageType}: {e.Message}");
        }
    }

    private async Task ConnectAsync()
    {
        _connection = await ConnectionHelper.SetupSecureConnectionAsync(_ipAddress, _authKey);
        _connectionSubscription = _connection.Messages.Subscribe(OnMessage);
    }

    public async Task CloseAsync()
    {
        State = State.Closing;
        _runCancellation?.Cancel();

        if (_connection != null)
        {
            _connectionSubscription?.Dispose();
            await _connection.CloseAsync();
        }
    }

    public async Task WaitForInitializationAsync()
    {
        if (State == State.Uninitialized)
        {
            await Task.Delay(100);
        }

        while (State == State.Initializing)
        {
            await Task.Delay(100);
        }
    }

    public async Task<Dictionary<int, Comp>> GetCompsAsync()
    {
        await WaitForInitializationAsync();
        return _comps;
    }

    public async Task<Dictionary<int, BridgeDevice>> GetDevicesAsync()
    {
        await WaitForInitializationAsync();
        return _devices;
    }

    public async Task<Dictionary<int, Room>> GetRoomsAsync()
    {
        await WaitForInitializationAsync();
        return _rooms;
    }

    public void Dispose()
    {
        _connectionSubscription?.Dispose();
        _connection?.Dispose();
        _runCancellation?.Dispose();
    }
}
