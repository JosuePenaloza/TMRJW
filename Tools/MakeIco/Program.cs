using System;
using System.Drawing;
using System.IO;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: MakeIco <input.png> <output.ico>");
    return 1;
}

var inPath = args[0];
var outPath = args[1];
if (!File.Exists(inPath))
{
    Console.Error.WriteLine($"Input not found: {inPath}");
    return 2;
}

try
{
    using var bmp = (Bitmap)Image.FromFile(inPath);
    using var icon = Icon.FromHandle(bmp.GetHicon());
    using var fs = new FileStream(outPath, FileMode.Create);
    icon.Save(fs);
    Console.WriteLine("Ico created");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.ToString());
    return 3;
}
