using ZstdNet;

namespace Minechat.Server.Compression;

public class ZstdNetCompressor : ICompressionHandler
{
    public byte[] Compress(byte[] data)
    {
        using var compressor = new Compressor();
        return compressor.Wrap(data);
    }

    public byte[] Decompress(byte[] compressedData, int decompressedSize)
    {
        using var decompressor = new Decompressor();
        var decompressed = decompressor.Unwrap(compressedData);

        if (decompressed.Length != decompressedSize)
        {
            Serilog.Log.Warning("Decompressed size mismatch. Expected {Expected}, got {Actual}",
                decompressedSize, decompressed.Length);
        }

        return decompressed;
    }
}
