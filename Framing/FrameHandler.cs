using Serilog;

namespace Minechat.Server.Framing;

public class FrameHandler
{
    private const int HeaderSize = 8;
    private const int MaxDecompressedSize = 1024 * 1024;
    private const int MaxCompressedSize = 1024 * 1024;

    public async Task<(int decompressedSize, int compressedSize, byte[] compressedData)?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        // Read exactly 8 bytes for the header - must handle partial reads
        var headerBuffer = new byte[HeaderSize];
        var headerRead = 0;

        while (headerRead < HeaderSize)
        {
            var bytesRead = await stream.ReadAsync(headerBuffer.AsMemory(headerRead, HeaderSize - headerRead), cancellationToken);
            if (bytesRead == 0)
                return null;  // Connection closed
            headerRead += bytesRead;
        }

        Log.Debug("Header read: {HeaderBufferHex}", Convert.ToHexString(headerBuffer));

        // Both big-endian as per spec
        var decompressedSize = ReadInt32BigEndian(headerBuffer.AsSpan(0, 4));
        var compressedSize = ReadInt32BigEndian(headerBuffer.AsSpan(4, 4));

        Log.Debug("decompressedSize={DecompressedSize}, compressedSize={CompressedSize}", decompressedSize, compressedSize);

        if (decompressedSize <= 0 || compressedSize <= 0)
            throw new InvalidDataException("Invalid frame size: decompressed or compressed size is non-positive");

        if (decompressedSize > MaxDecompressedSize)
            throw new InvalidDataException($"Decompressed size {decompressedSize} exceeds maximum {MaxDecompressedSize}");

        if (compressedSize > MaxCompressedSize)
            throw new InvalidDataException($"Compressed size {compressedSize} exceeds maximum {MaxCompressedSize}");

        // Read exactly compressedSize bytes
        var compressedData = new byte[compressedSize];
        var dataRead = 0;

        while (dataRead < compressedSize)
        {
            var bytesRead = await stream.ReadAsync(compressedData.AsMemory(dataRead, compressedSize - dataRead), cancellationToken);
            if (bytesRead == 0)
                throw new EndOfStreamException("Connection closed while reading compressed data");
            dataRead += bytesRead;
        }

        Log.Debug("Read compressed data: {CompressedDataLength} bytes", compressedData.Length);

        return (decompressedSize, compressedSize, compressedData);
    }

    private static int ReadInt32BigEndian(ReadOnlySpan<byte> data)
    {
        if (BitConverter.IsLittleEndian)
            return (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];
        return BitConverter.ToInt32(data);
    }

    private static void WriteInt32BigEndian(Span<byte> data, int value)
    {
        if (BitConverter.IsLittleEndian)
        {
            data[0] = (byte)(value >> 24);
            data[1] = (byte)(value >> 16);
            data[2] = (byte)(value >> 8);
            data[3] = (byte)value;
        }
        else
        {
            BitConverter.GetBytes(value).CopyTo(data);
        }
    }

    public async Task WriteFrameAsync(Stream stream, byte[] compressedData, int decompressedSize, CancellationToken cancellationToken = default)
    {
        var header = new byte[HeaderSize];
        WriteInt32BigEndian(header, decompressedSize);
        WriteInt32BigEndian(header.AsSpan(4, 4), compressedData.Length);

        Log.Debug("Writing header: {HeaderHex}, decompressedSize={DecompressedSize}, compressedSize={CompressedDataLength}", Convert.ToHexString(header), decompressedSize, compressedData.Length);

        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(compressedData, cancellationToken);
    }
}
