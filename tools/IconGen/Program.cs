using System.Buffers.Binary;
using Perch.App.Controls;
using SkiaSharp;

// Writes assets/perch.ico and assets/perch.icns, the icons baked into the Windows exe and
// the macOS app bundle. Run it after changing AppIconShape:
//
//   dotnet run --project tools/IconGen [output-folder]

var folder = args.FirstOrDefault() ?? "assets";
Directory.CreateDirectory(folder);

WriteIco(Path.Combine(folder, "perch.ico"), new[] { 16, 20, 24, 32, 48, 64, 128, 256 });
WriteIcns(Path.Combine(folder, "perch.icns"));

static byte[] Png(int size)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);

    var scale = size / AppIconShape.Size;
    canvas.Scale(scale);

    using var tile = new SKPaint { Color = new SKColor(AppIconShape.BrandArgb), IsAntialias = true };
    canvas.DrawRoundRect(0, 0, AppIconShape.Size, AppIconShape.Size, AppIconShape.TileRadius, AppIconShape.TileRadius, tile);

    using var mark = new SKPaint { Color = new SKColor(AppIconShape.MarkArgb), IsAntialias = true };
    foreach (var (x, y, w, h) in AppIconShape.Desk)
        canvas.DrawRect(x, y, w, h, mark);

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

// PNG frames in an .ico are fine from Windows Vista on.
static void WriteIco(string path, int[] sizes)
{
    var frames = sizes.Select(Png).ToList();

    using var file = File.Create(path);
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
    Console.WriteLine($"Wrote {path} ({frames.Count} sizes, {file.Length:n0} bytes)");
}

// An .icns is a big-endian list of (type, length, PNG) chunks; each type names a size.
static void WriteIcns(string path)
{
    (string Type, int Size)[] entries =
    {
        ("icp4", 16), ("icp5", 32), ("icp6", 64), ("ic07", 128), ("ic08", 256), ("ic09", 512), ("ic10", 1024),
        ("ic11", 32), ("ic12", 64), ("ic13", 256), ("ic14", 512)
    };

    var chunks = entries.Select(e => (e.Type, Data: Png(e.Size))).ToList();
    var total = 8 + chunks.Sum(c => 8 + c.Data.Length);

    using var file = File.Create(path);
    var header = new byte[8];

    void Chunk(string type, int length)
    {
        System.Text.Encoding.ASCII.GetBytes(type, 0, 4, header, 0);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), length);
        file.Write(header);
    }

    Chunk("icns", total);
    foreach (var (type, data) in chunks)
    {
        Chunk(type, 8 + data.Length);
        file.Write(data);
    }

    Console.WriteLine($"Wrote {path} ({chunks.Count} entries, {file.Length:n0} bytes)");
}
