using KaixinPet.Audio;
using KaixinPet.Core.Settings;
using NAudio.Wave;
using System.IO;

var tempRoot = Path.Combine(Path.GetTempPath(), "kaixin-audio-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
var failures = 0;

try
{
    TestCatalog();
    TestValidImport(tempRoot);
    TestShortAudio(tempRoot);
    TestSilentAudio(tempRoot);
    TestOggRejected(tempRoot);
    TestCorruptFile(tempRoot);
    TestCustomSourceInfo(tempRoot);
}
catch (Exception exception)
{
    failures++;
    Console.WriteLine($"FAIL  unexpected exception: {exception}");
}
finally
{
    try
    {
        Directory.Delete(tempRoot, recursive: true);
    }
    catch
    {
        // Temp cleanup is best effort.
    }
}

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;

void TestCatalog()
{
    var root = FindRepoRoot();
    var assets = Path.Combine(root, "assets", "sounds");
    var catalog = SoundCatalog.Load(assets);
    Assert(catalog.Events.Count == 13, "catalog has 13 events");
    var pet = catalog.Find("interaction.pet");
    Assert(pet is not null && pet.Loop == false && File.Exists(Path.Combine(assets, pet.File)),
        "interaction.pet default file exists");
    var purr = catalog.Find("ambient.purr");
    Assert(purr is not null && purr.Loop, "ambient.purr is looped");

    foreach (var definition in catalog.Events)
    {
        using var reader = new WaveFileReader(Path.Combine(assets, definition.File));
        Assert(reader.WaveFormat.SampleRate == 44100, $"{definition.Id} default is 44.1kHz");
        Assert(reader.WaveFormat.BitsPerSample == 16, $"{definition.Id} default is 16-bit");
        Assert(reader.WaveFormat.Channels == 1, $"{definition.Id} default is mono");
        var buffer = new byte[Math.Min(reader.Length, 8192)];
        Assert(reader.Read(buffer, 0, buffer.Length) > 0, $"{definition.Id} default has audio data");
    }

    Console.WriteLine("PASS  catalog loads default events and all default files are readable");
}

void TestValidImport(string root)
{
    var dataRoot = Path.Combine(root, "data-valid");
    Directory.CreateDirectory(dataRoot);
    var source = Path.Combine(root, "valid.wav");
    WriteWave(source, 0.75, 0.15, 0.5, 0.25);
    var result = SoundImporter.Import(source, "interaction.pet", dataRoot);
    Assert(result.Success, "valid wav import succeeds");
    Assert(result.Error is null, "valid wav import has no error");
    Assert(result.RelativePath is not null, "valid wav returns relative path");
    var output = Path.Combine(dataRoot, result.RelativePath!);
    Assert(File.Exists(output), "imported wav exists");
    Assert(result.Duration.TotalSeconds is > 0.5 and < 0.75, "silence trimmed with margins");

    var peak = 0.0;
    using (var reader = new WaveFileReader(output))
    {
        Assert(reader.WaveFormat.SampleRate == 44100, "output is 44.1kHz");
        Assert(reader.WaveFormat.BitsPerSample == 16, "output is 16-bit");
        Assert(reader.WaveFormat.Channels == 1, "output is mono");

        var buffer = new byte[reader.Length];
        var read = reader.Read(buffer, 0, buffer.Length);
        for (var i = 0; i + 1 < read; i += 2)
        {
            var sample = BitConverter.ToInt16(buffer, i) / 32768.0;
            peak = Math.Max(peak, Math.Abs(sample));
        }
    }

    Assert(peak is > 0.86 and < 0.92, "output peak normalized to about -1 dBFS");

    var info = new AudioService(Path.Combine(FindRepoRoot(), "assets", "sounds"), dataRoot);
    var settings = new AudioSettings();
    settings.CustomSounds["interaction.pet"] = result.RelativePath!;
    info.ApplySettings(settings);
    var sourceInfo = info.GetSourceInfo("interaction.pet");
    Assert(sourceInfo.HasCustom && !sourceInfo.CustomMissing, "custom source is reported");
    info.Dispose();
    SoundImporter.DeleteCachedFile(dataRoot, result.RelativePath);
    Assert(!File.Exists(output), "cached custom file can be deleted");
    Console.WriteLine("PASS  valid import, format, trim, normalize, source info");
}

void TestShortAudio(string root)
{
    var dataRoot = Path.Combine(root, "data-short");
    var source = Path.Combine(root, "short.wav");
    WriteWave(source, 0.1, 0.0, 0.1, 0.3);
    var result = SoundImporter.Import(source, "interaction.pet", dataRoot);
    Assert(!result.Success && result.Error is not null, "short audio is rejected");
    Console.WriteLine("PASS  short audio rejected");
}

void TestSilentAudio(string root)
{
    var dataRoot = Path.Combine(root, "data-silent");
    var source = Path.Combine(root, "silent.wav");
    using (var writer = new WaveFileWriter(source, new WaveFormat(44100, 16, 1)))
    {
        writer.Write(new byte[44100 * 2 / 2], 0, 44100 * 2 / 2);
    }

    var result = SoundImporter.Import(source, "interaction.pet", dataRoot);
    Assert(!result.Success && result.Error is not null, "silent audio is rejected");
    Console.WriteLine("PASS  silent audio rejected");
}

void TestOggRejected(string root)
{
    var dataRoot = Path.Combine(root, "data-ogg");
    var source = Path.Combine(root, "voice.ogg");
    File.WriteAllText(source, "not really ogg");
    var result = SoundImporter.Import(source, "interaction.pet", dataRoot);
    Assert(!result.Success && result.Error!.Contains("OGG"), "ogg is rejected with a clear message");
    Console.WriteLine("PASS  OGG rejected");
}

void TestCorruptFile(string root)
{
    var dataRoot = Path.Combine(root, "data-corrupt");
    var source = Path.Combine(root, "broken.wav");
    File.WriteAllText(source, "this is not a wave file");
    var result = SoundImporter.Import(source, "interaction.pet", dataRoot);
    Assert(!result.Success && result.Error is not null, "corrupt wav is rejected");
    Assert(!Directory.Exists(Path.Combine(dataRoot, "sounds", "custom")) ||
           Directory.GetFiles(Path.Combine(dataRoot, "sounds", "custom")).Length == 0,
        "corrupt import leaves no cache file");
    Console.WriteLine("PASS  corrupt file rejected without cache");
}

void TestCustomSourceInfo(string root)
{
    var dataRoot = Path.Combine(root, "data-info");
    var service = new AudioService(Path.Combine(FindRepoRoot(), "assets", "sounds"), dataRoot);
    var settings = new AudioSettings();
    settings.CustomSounds["interaction.pet"] = "sounds/custom/missing.wav";
    service.ApplySettings(settings);
    var info = service.GetSourceInfo("interaction.pet");
    Assert(info.HasCustom && info.CustomMissing, "missing custom file is reported");
    service.Dispose();
    Console.WriteLine("PASS  missing custom source falls back info");
}

static void WriteWave(
    string path,
    double totalSeconds,
    double toneStartSeconds,
    double toneSeconds,
    double amplitude)
{
    const int rate = 44100;
    var count = (int)(rate * totalSeconds);
    using var writer = new WaveFileWriter(path, new WaveFormat(rate, 16, 1));
    var buffer = new byte[count * 2];
    for (var i = 0; i < count; i++)
    {
        var t = (double)i / rate;
        var value = 0.0;
        if (t >= toneStartSeconds && t < toneStartSeconds + toneSeconds)
        {
            value = amplitude * Math.Sin(2 * Math.PI * 440 * t);
        }

        var sample = (short)Math.Round(Math.Clamp(value, -1, 1) * short.MaxValue);
        buffer[i * 2] = (byte)(sample & 0xFF);
        buffer[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
    }

    writer.Write(buffer, 0, buffer.Length);
}

static string FindRepoRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "assets", "sounds", "sounds.json")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("repository root with assets/sounds not found");
}

static void Assert(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException("assertion failed: " + name);
    }

    Console.WriteLine("  ok  " + name);
}
