using System.Diagnostics;
using Serilog;

namespace Minechat.Server.Compression;

public class CliCompressor : ICompressionHandler
{
    public byte[] Compress(byte[] data)
    {
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

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new Exception($"zstd exited with code {process.ExitCode}: {error}");
        }
    }
}