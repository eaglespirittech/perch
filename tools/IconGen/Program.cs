using System.Drawing.Imaging;
using Perch.Ui;

// Writes assets/perch.ico, the icon compiled into the exe. Run it after changing
// AppIcon.Draw:  dotnet run --project tools/IconGen

var brand = Color.FromArgb(0, 95, 184);
var mark = Color.White;
int[] sizes = { 16, 20, 24, 32, 48, 64, 128, 256 };

var output = args.FirstOrDefault() ?? Path.Combine("assets", "perch.ico");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);

var frames = new List<byte[]>();
foreach (var size in sizes)
{
    using var bitmap = AppIcon.Draw(size, brand, mark);
    using var buffer = new MemoryStream();
    bitmap.Save(buffer, ImageFormat.Png); // PNG frames are fine in an .ico on Vista and up
    frames.Add(buffer.ToArray());
}

using var file = File.Create(output);
using var writer = new BinaryWriter(file);

writer.Write((short)0);              // reserved
writer.Write((short)1);              // 1 = icon
writer.Write((short)frames.Count);

var offset = 6 + 16 * frames.Count;
for (var i = 0; i < frames.Count; i++)
{
    writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
    writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
    writer.Write((byte)0);           // palette size
    writer.Write((byte)0);           // reserved
    writer.Write((short)1);          // colour planes
    writer.Write((short)32);         // bits per pixel
    writer.Write(frames[i].Length);
    writer.Write(offset);
    offset += frames[i].Length;
}

foreach (var frame in frames) writer.Write(frame);

Console.WriteLine($"Wrote {output} ({frames.Count} sizes, {file.Length:n0} bytes)");
