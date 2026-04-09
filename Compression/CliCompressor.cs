using System.Diagnostics;
using Serilog;

namespace Minechat.Server.Compression;

public class CliCompressor : ICompressionHandler
{
    private static readonly bool ZstdAvailable = CheckZstd();

    private static bool CheckZstd()
    {
        try
        {
            var exists = new CliCompressor().ZstdExists(3000);
            if (!exists)
            {
                Log.Error("zstd binary not found. Please install zstd to enable compression.");
            }
            return exists;
        }
        catch
        {
            Log.Error("zstd binary check failed. Please install zstd to enable compression.");
            return false;
        }
    }

    public byte[] Compress(byte[] data)
    {
        if (!ZstdAvailable)
            throw new InvalidOperationException("zstd is not available. Please install zstd.");
        Log.Debug("Compressing {Size} bytes", data.Length);

        var tempIn = Path.GetTempFileName();
        var tempOut = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempIn, data);
            RunZstdCli(tempIn, tempOut, "-z");
            var result = File.ReadAllBytes(tempOut);
            Log.Debug("Compressed to {Size} bytes", result.Length);
            return result;
        }
        finally
        {
            File.Delete(tempIn);
            File.Delete(tempOut);
        }
    }

    public byte[] Decompress(byte[] compressedData, int decompressedSize)
    {
        if (!ZstdAvailable)
            throw new InvalidOperationException("zstd is not available. Please install zstd.");

        Log.Debug("Decompressing {Size} bytes to expected {Expected} bytes",
            compressedData.Length, decompressedSize);

        var tempIn = Path.GetTempFileName();
        var tempOut = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempIn, compressedData);
            RunZstdCli(tempIn, tempOut, "-d");
            var result = File.ReadAllBytes(tempOut);

            if (result.Length != decompressedSize)
            {
                Log.Warning("Decompressed size mismatch. Expected {Expected}, got {Actual}",
                    decompressedSize, result.Length);
            }

            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Decompression failed for {Size} bytes, expected decompressed size {Expected}",
                compressedData.Length, decompressedSize);
            throw;
        }
        finally
        {
            File.Delete(tempIn);
            File.Delete(tempOut);
        }
    }

    private void RunZstdCli(string inputFile, string outputFile, string mode)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "zstd",
            Arguments = $"{mode} -f -o {outputFile} {inputFile}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new Exception("Failed to start zstd process");
        }

        if (!process.WaitForExit(5000))
        {
            process.Kill();
            throw new Exception("zstd timed out after 5 seconds");
        }

        if (process.ExitCode == 0) return; // Process exited successfully

        var error = process.StandardError.ReadToEnd();
        throw new Exception($"zstd exited with code {process.ExitCode}: {error}");
    }

    private bool ZstdExists(int timeoutMs = 3000)
    {
        string[] candidates = Environment.OSVersion.Platform == PlatformID.Win32NT
            ? ["zstd.exe", "zstd"]
            : ["zstd"];

        foreach (var candidate in candidates)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = candidate,
                Arguments = "-v",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var proc = new Process();

                proc.StartInfo = startInfo;
                if (!proc.Start())
                    continue;

                // Wait with timeout to avoid hanging if binary prompts for something
                var finished = proc.WaitForExit(timeoutMs);
                if (!finished)
                {
                    try { proc.Kill(); } catch { }
                    continue;
                }

                // Consider exit codes and/or output. zstd -v prints version to stderr or stdout depending on build.
                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();

                // Typical behavior: exit code 0 and version string in output; accept either stream.
                if (proc.ExitCode == 0 && (!string.IsNullOrWhiteSpace(stdout) || !string.IsNullOrWhiteSpace(stderr)))
                    return true;

                // Some builds may return non-zero but still print version; accept that too.
                if (!string.IsNullOrWhiteSpace(stdout) || !string.IsNullOrWhiteSpace(stderr))
                {
                    var combined = (stdout + "\n" + stderr).ToLowerInvariant();
                    if (combined.Contains("zstd") || combined.Contains("zstandard") || combined.Contains("zstd command"))
                        return true;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Executable not found for this candidate; try next
                continue;
            }
            catch (Exception)
            {
                // Ignore other errors and try other candidates
                continue;
            }
        }

        return false;
    }
}