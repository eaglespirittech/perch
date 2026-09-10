using System.Text.Json;

namespace Perch.Cli;

/// <summary>
/// The help text doubles as the contract for scripts and agents, so it lists the exit
/// codes and can emit itself as JSON.
/// </summary>
static class Help
{
    public const string Text = """
        perch-cli - control an IKEA IDASEN sit/stand desk over Bluetooth LE.

        USAGE
          perch-cli <command> [arguments] [options]

        COMMANDS
          list                 List paired Bluetooth LE devices. The desk Perch normally
                               uses is marked "saved".
          status               Print the desk's current height in centimetres.
          set <height>         Move the desk to an absolute height in centimetres,
                               between 62.0 and 127.0. Waits until the desk settles.
          nudge <delta>        Move by a relative amount, e.g. "nudge 1.5" or
                               "nudge -2". Same range limits apply.
          preset <1|2>         Move to one of the two heights saved in the app.
          stop                 Stop a move that is in progress.
          watch                Print height changes until interrupted.
          help                 Show this text. Add --json for a machine-readable form.
          version              Print the version.

        OPTIONS
          --device <name|id>   Which desk to use. Matches an exact device id, or any
                               device whose name contains the text. Defaults to the desk
                               saved by the Perch app, then to the first desk-like name.
          --timeout <seconds>  How long to wait for a move to finish. Default 60.
          --json               Emit JSON on stdout instead of plain text.
          --quiet              Suppress progress chatter; results and errors still print.
          --direct             Always talk to the desk over Bluetooth, never through the
                               running app. Fails with exit code 3 if the app has the
                               connection.
          --help, -h           Show this text.

        EXIT CODES
          0  success
          1  bad usage or arguments
          2  no matching desk is paired with Windows
          3  could not connect - something else holds the desk's one Bluetooth
             connection, such as the IKEA phone app
          4  the move failed, timed out, or was interrupted

        EXAMPLES
          perch-cli list --json
          perch-cli status
          perch-cli set 110.5
          perch-cli nudge -1
          perch-cli set 72 --device "Desk 9770" --timeout 90 --json

        NOTES
          The desk accepts one Bluetooth connection at a time. When the Perch app is
          running it holds that connection, so these commands are handed to the app and
          it does the work. When the app is not running the CLI drives the desk itself.
          Either way the command behaves the same; --json reports which route was taken.
        """;

    public static string Json()
    {
        var schema = new
        {
            name = "perch-cli",
            summary = "Control an IKEA IDASEN sit/stand desk over Bluetooth LE.",
            usage = "perch-cli <command> [arguments] [options]",
            commands = new object[]
            {
                new { name = "list", summary = "List paired Bluetooth LE devices.", arguments = Array.Empty<object>() },
                new { name = "status", summary = "Print the desk's current height in centimetres.", arguments = Array.Empty<object>() },
                new
                {
                    name = "set",
                    summary = "Move the desk to an absolute height in centimetres.",
                    arguments = new object[]
                    {
                        new { name = "height", required = true, type = "number", minimum = DeskController.MinCm, maximum = DeskController.MaxCm }
                    }
                },
                new
                {
                    name = "nudge",
                    summary = "Move by a relative number of centimetres, positive or negative.",
                    arguments = new object[] { new { name = "delta", required = true, type = "number" } }
                },
                new
                {
                    name = "preset",
                    summary = "Move to one of the two heights saved in the Perch app.",
                    arguments = new object[] { new { name = "slot", required = true, type = "integer", minimum = 1, maximum = 2 } }
                },
                new { name = "stop", summary = "Stop a move in progress.", arguments = Array.Empty<object>() },
                new { name = "watch", summary = "Print height changes until interrupted.", arguments = Array.Empty<object>() },
                new { name = "help", summary = "Show help.", arguments = Array.Empty<object>() },
                new { name = "version", summary = "Print the version.", arguments = Array.Empty<object>() }
            },
            options = new object[]
            {
                new { name = "--device", type = "string", summary = "Device id, or text matched against device names." },
                new { name = "--timeout", type = "integer", summary = "Seconds to wait for a move. Default 60." },
                new { name = "--json", type = "boolean", summary = "Emit JSON instead of plain text." },
                new { name = "--quiet", type = "boolean", summary = "Suppress progress chatter." },
                new { name = "--direct", type = "boolean", summary = "Always use Bluetooth directly instead of the running app." }
            },
            exit_codes = new object[]
            {
                new { code = 0, meaning = "success" },
                new { code = 1, meaning = "bad usage or arguments" },
                new { code = 2, meaning = "no matching desk is paired with Windows" },
                new { code = 3, meaning = "could not connect; something else holds the desk's connection" },
                new { code = 4, meaning = "the move failed, timed out, or was interrupted" }
            },
            notes = new[]
            {
                "The desk accepts one Bluetooth connection at a time.",
                "When the Perch app is running, commands are handed to it; otherwise the CLI drives the desk directly.",
                "JSON results include a \"via\" field saying which route was used: \"app\" or \"bluetooth\"."
            }
        };

        return JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });
    }
}
