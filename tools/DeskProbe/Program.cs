using IdasenDeskControl;

// Read-only companion to the GUI, handy when something does not connect.
//
//   DeskProbe                 list every paired Bluetooth LE device
//   DeskProbe <name-or-id>    connect to the first match and print its height
//   DeskProbe <match> --watch keep printing height changes until Enter

var match = args.FirstOrDefault(a => !a.StartsWith("--"));
var watch = args.Contains("--watch");

if (match is null)
{
    foreach (var device in await DeskController.ListPairedDevicesAsync())
        Console.WriteLine($"{device.Name,-30} {device.Id}");
    return 0;
}

var devices = await DeskController.ListPairedDevicesAsync();
var target = devices.FirstOrDefault(d =>
                 string.Equals(d.Id, match, StringComparison.OrdinalIgnoreCase))
             ?? devices.FirstOrDefault(d => d.Name.Contains(match, StringComparison.OrdinalIgnoreCase));

if (target is null)
{
    Console.Error.WriteLine($"No paired device matching '{match}'.");
    return 1;
}

using var desk = new DeskController();
try
{
    Console.WriteLine($"Connecting to {target.Name}...");
    await desk.ConnectAsync(target.Id);
    Console.WriteLine($"Height: {desk.CurrentCm:0.0} cm   (speed {desk.CurrentSpeed:0.000})");

    if (watch)
    {
        desk.HeightChanged += cm => Console.WriteLine($"  -> {cm:0.0} cm");
        Console.WriteLine("Watching. Press Enter to quit.");
        Console.ReadLine();
    }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
