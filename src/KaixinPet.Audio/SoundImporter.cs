using System.Security.Cryptography;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using KaixinPet.Core.Logging;

namespace KaixinPet.Audio;

public sealed record SoundImportResult(bool Success, string? RelativePath, TimeSpan Duration, string? Error)
{
    public static SoundImportResult Fail(string error) => new(false, null, TimeSpan.Zero, error);
}

public static class SoundImporter
{
    private const int TargetSampleRate = 44100;
    private const double MinDurationSeconds = 0.2;
    private const double MaxDurationSeconds = 30.0;
    private const long MaxFileBytes = 30L * 1024 * 1024;
    private const double SilenceThreshold = 0.005623413; // -45 dBFS

    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".wav", ".mp3", ".m4a", ".aac", ".flac" };

    public static SoundImportResult Import(
        string sourcePath,
        string eventId,
        string dataRoot,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return SoundImportResult.Fail("找不到选择的音频文件");
        }

        var extension = Path.GetExtension(sourcePath);
        if (extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase))
        {
            return SoundImportResult.Fail("不支持 OGG 格式，请使用 WAV、MP3、M4A 或 FLAC");
        }

        if (!SupportedExtensions.Contains(extension))
        {
            return SoundImportResult.Fail("不支持此格式，请使用 WAV、MP3、M4A 或 FLAC");
        }

        long fileLength;
        try
        {
            fileLength = new FileInfo(sourcePath).Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return SoundImportResult.Fail("无法读取这个文件");
        }

        if (fileLength <= 0)
        {
            return SoundImportResult.Fail("文件是空的");
        }

        if (fileLength > MaxFileBytes)
        {
            return SoundImportResult.Fail("文件超过 30MB 上限");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var reader = new MediaFoundationReader(sourcePath);
            var duration = reader.TotalTime;
            if (duration.TotalSeconds < MinDurationSeconds)
            {
                return SoundImportResult.Fail("音频太短，需要至少 0.2 秒");
            }

            if (duration.TotalSeconds > MaxDurationSeconds)
            {
                return SoundImportResult.Fail("音频太长，最多 30 秒");
            }

            var samples = ReadMonoSamples(reader, cancellationToken);
            if (samples.Length == 0)
            {
                return SoundImportResult.Fail("音频没有可用的声音");
            }

            var trimmed = TrimSilence(samples);
            if (trimmed.Length < (int)(MinDurationSeconds * TargetSampleRate))
            {
                return SoundImportResult.Fail("去掉静音后音频太短");
            }

            var peak = 0.0;
            foreach (var sample in trimmed)
            {
                peak = Math.Max(peak, Math.Abs(sample));
            }

            if (peak < 1e-4)
            {
                return SoundImportResult.Fail("音频几乎全是静音");
            }

            var gain = Math.Min(20.0, 0.891 / peak);
            var cacheDirectory = Path.Combine(dataRoot, "sounds", "custom");
            Directory.CreateDirectory(cacheDirectory);

            var hash = HashFile(sourcePath);
            var fileName = $"{SanitizeEventId(eventId)}-{hash}.wav";
            var targetPath = Path.Combine(cacheDirectory, fileName);
            var tempPath = targetPath + ".tmp";
            try
            {
                WriteWaveFile(tempPath, trimmed, gain, cancellationToken);
                File.Move(tempPath, targetPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            var relativePath = Path.GetRelativePath(dataRoot, targetPath);
            var finalDuration = TimeSpan.FromSeconds((double)trimmed.Length / TargetSampleRate);
            Log.Info(
                $"audio import ok: event={eventId}, file={Path.GetFileName(sourcePath)}, " +
                $"duration={finalDuration.TotalSeconds:0.0}s, peak={peak:0.000}");
            return new SoundImportResult(true, relativePath, finalDuration, null);
        }
        catch (OperationCanceledException)
        {
            return SoundImportResult.Fail("导入已取消");
        }
        catch (Exception exception)
        {
            Log.Warn(
                $"audio import failed: event={eventId}, file={Path.GetFileName(sourcePath)}, " +
                $"error={exception.GetType().Name}: {exception.Message}");
            return SoundImportResult.Fail("无法解码这个音频，请换一个 WAV/MP3/M4A/FLAC 文件");
        }
    }

    public static void DeleteCachedFile(string dataRoot, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        try
        {
            var customRoot = Path.GetFullPath(Path.Combine(dataRoot, "sounds", "custom"));
            var path = Path.GetFullPath(
                Path.IsPathRooted(relativePath) ? relativePath : Path.Combine(dataRoot, relativePath));
            if (!path.StartsWith(customRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"audio cache delete failed: {exception.Message}");
        }
    }

    private static float[] ReadMonoSamples(MediaFoundationReader reader, CancellationToken cancellationToken)
    {
        ISampleProvider provider = reader.ToSampleProvider();
        var channels = provider.WaveFormat.Channels;
        if (channels == 2)
        {
            provider = new StereoToMonoSampleProvider(provider)
            {
                LeftVolume = 0.5f,
                RightVolume = 0.5f
            };
        }
        else if (channels != 1)
        {
            throw new NotSupportedException($"unsupported channel count {channels}");
        }

        if (provider.WaveFormat.SampleRate != TargetSampleRate)
        {
            provider = new WdlResamplingSampleProvider(provider, TargetSampleRate);
        }

        var buffer = new float[TargetSampleRate * 2];
        var collected = new List<float>(TargetSampleRate * 8);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = provider.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                break;
            }

            for (var i = 0; i < read; i++)
            {
                collected.Add(buffer[i]);
            }

            if (collected.Count > TargetSampleRate * (MaxDurationSeconds + 1))
            {
                break;
            }
        }

        return collected.ToArray();
    }

    private static float[] TrimSilence(float[] samples)
    {
        var first = 0;
        while (first < samples.Length && Math.Abs(samples[first]) < SilenceThreshold)
        {
            first++;
        }

        if (first >= samples.Length)
        {
            return [];
        }

        var last = samples.Length - 1;
        while (last > first && Math.Abs(samples[last]) < SilenceThreshold)
        {
            last--;
        }

        var margin = (int)(TargetSampleRate * 0.05);
        var start = Math.Max(0, first - margin);
        var end = Math.Min(samples.Length - 1, last + margin);
        var result = new float[end - start + 1];
        Array.Copy(samples, start, result, 0, result.Length);
        return result;
    }

    private static void WriteWaveFile(
        string path,
        float[] samples,
        double gain,
        CancellationToken cancellationToken)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(TargetSampleRate, 16, 1));
        var buffer = new byte[4096 * 2];
        var index = 0;
        while (index < samples.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(4096, samples.Length - index);
            for (var i = 0; i < count; i++)
            {
                var value = Math.Clamp(samples[index + i] * gain, -1.0f, 1.0f);
                var sample = (short)Math.Round(value * short.MaxValue);
                buffer[i * 2] = (byte)(sample & 0xFF);
                buffer[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }

            writer.Write(buffer, 0, count * 2);
            index += count;
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        var suffix = Convert.ToHexString(hash)[..8].ToLowerInvariant();
        return suffix;
    }

    private static string SanitizeEventId(string eventId)
    {
        var chars = eventId.Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_');
        return new string(chars.ToArray());
    }
}
