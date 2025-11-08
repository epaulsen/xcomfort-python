using XComfortDotNet;

// Example usage of xComfort Bridge .NET client
// Replace with your bridge IP address and authentication key
const string ipAddress = "<your_bridge_ip>";
const string authKey = "<your_auth_key>";

Console.WriteLine("xComfort Bridge .NET Client");
Console.WriteLine("============================");
Console.WriteLine();

// Create bridge instance
var bridge = new Bridge(ipAddress, authKey);

// Optional: Enable logging
bridge.Logger = Console.WriteLine;

// Start bridge connection in background
var runTask = Task.Run(() => bridge.RunAsync());

try
{
    // Get all devices
    var devices = await bridge.GetDevicesAsync();

    Console.WriteLine($"Found {devices.Count} devices:");
    foreach (var device in devices.Values)
    {
        Console.WriteLine($"  - {device}");
        
        // Subscribe to state changes
        device.State.Subscribe(state => 
        {
            Console.WriteLine($"Device state changed [{device.DeviceId}] '{device.Name}': {state}");
        });
    }

    // Get all rooms
    var rooms = await bridge.GetRoomsAsync();
    
    Console.WriteLine();
    Console.WriteLine($"Found {rooms.Count} rooms:");
    foreach (var room in rooms.Values)
    {
        Console.WriteLine($"  - {room}");
        
        // Subscribe to room state changes
        room.State.Subscribe(state =>
        {
            Console.WriteLine($"Room state changed [{room.RoomId}] '{room.Name}': {state}");
        });
    }

    Console.WriteLine();
    Console.WriteLine("Monitoring device and room state changes for 50 seconds...");
    Console.WriteLine("Try flipping light switches manually to see state updates.");
    Console.WriteLine();

    // Wait 50 seconds to observe state changes
    await Task.Delay(TimeSpan.FromSeconds(50));

    // Example: Turn off all lights (uncomment to use)
    /*
    Console.WriteLine();
    Console.WriteLine("Turning off all lights...");
    foreach (var device in devices.Values.OfType<Light>())
    {
        await device.SwitchAsync(false);
    }
    await Task.Delay(TimeSpan.FromSeconds(5));
    */

    // Example: Set room temperature (uncomment to use)
    /*
    if (rooms.Count > 0)
    {
        var room = rooms.Values.First();
        Console.WriteLine($"Setting temperature for room '{room.Name}' to 22.0°C");
        await room.SetTargetTemperatureAsync(22.0);
    }
    */

    // Example: Control a shade (uncomment to use)
    /*
    foreach (var device in devices.Values.OfType<Shade>())
    {
        Console.WriteLine($"Moving shade '{device.Name}' down");
        await device.MoveDownAsync();
        await Task.Delay(TimeSpan.FromSeconds(5));
        await device.MoveStopAsync();
    }
    */

    Console.WriteLine();
    Console.WriteLine("Closing connection...");
    
    // Close the bridge connection
    await bridge.CloseAsync();
    await runTask;

    Console.WriteLine("Done!");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    await bridge.CloseAsync();
}
finally
{
    bridge.Dispose();
}
