using System.Diagnostics;

namespace Minechat.Server.Compression;

public class CompressionHandler
{
    public byte[] Compress(byte[] data)
    {
        var tempIn = Path.GetTempFileName();
        var tempOut = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempIn, data);
            RunZstdCli(tempIn, tempOut, "-z");
            return File.ReadAllBytes(tempOut);
        }
        finally
        {
            File.Delete(tempIn);
            File.Delete(tempOut);
        }
    }

    public byte[] Decompress(byte[] compressedData, int decompressedSize)
    {
        var tempIn = Path.GetTempFileName();
        var tempOut = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempIn, compressedData);
            RunZstdCli(tempIn, tempOut, "-d");
            return File.ReadAllBytes(tempOut);
        }
        catch
        {
            return compressedData;
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
        process.WaitForExit(5000);

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new Exception($"zstd exited with code {process.ExitCode}: {error}");
        }
    }
}
