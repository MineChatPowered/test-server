namespace Minechat.Server.Compression;

public interface ICompressionHandler
{
    byte[] Compress(byte[] data);

    byte[] Decompress(byte[] data, int decompressedSize);
}
