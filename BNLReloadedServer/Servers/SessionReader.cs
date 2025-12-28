using System.Buffers;

namespace BNLReloadedServer.Servers;

public class SessionReader(IServiceDispatcher dispatcher, bool debugMode, string onError)
{
    private bool _packetInBuffer;
    private const int BodyMaxSize = 10_000_000;
    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(1024);
    private int _bufferLength;

    public void ProcessPacket(byte[] buffer, long offset, long size)
    {
        MemoryStream memStream;
        if (_packetInBuffer)
        {
            if (!TryAppendToBuffer(buffer, offset, size, out memStream))
                return;
        }
        else
        {
            memStream = new MemoryStream(buffer, (int)offset, (int)size, writable: false, publiclyVisible: true);
        }

        using var reader = new BinaryReader(memStream);
        try
        {
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                // The first part of every packet is an 7 bit encoded int of its length.
                var startPosition = reader.BaseStream.Position;
                var startLength = reader.BaseStream.Length;

                var packetLength = reader.Read7BitEncodedInt();
                if (reader.BaseStream.Position + packetLength > reader.BaseStream.Length)
                {
                    if (Math.Max(startLength - startPosition, 0) > 0)
                    {
                        _packetInBuffer = true;
                        BufferPartialPacket(memStream, startPosition);
                    }

                    break;
                }

                var currentPosition = reader.BaseStream.Position;
                if (debugMode)
                {
                    Console.WriteLine($"Packet length: {packetLength}");
                    var res = dispatcher.Dispatch(reader);
                    Console.WriteLine();
                    if (!res)
                    {
                        if (_packetInBuffer)
                            WipeBuffer();
                        break;
                    }
                }
                else
                {
                    if (!dispatcher.Dispatch(reader))
                    {
                        if (_packetInBuffer)
                            WipeBuffer();
                        break;
                    }
                }

                if (reader.BaseStream.Position < currentPosition + packetLength)
                {
                    reader.ReadBytes((int)(currentPosition + packetLength - reader.BaseStream.Position));
                }

                if (_packetInBuffer)
                    WipeBuffer();
            }
        }
        catch (EndOfStreamException)
        {
            Console.WriteLine(onError);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private void BufferPartialPacket(MemoryStream stream, long startPosition)
    {
        if (!stream.TryGetBuffer(out var segment))
        {
            WipeBuffer();
            return;
        }

        var remainingLength = (int)(segment.Count - startPosition);
        if (remainingLength <= 0 || remainingLength > BodyMaxSize)
        {
            WipeBuffer();
            return;
        }

        EnsureCapacity(remainingLength);
        Buffer.BlockCopy(segment.Array!, segment.Offset + (int)startPosition, _buffer, 0, remainingLength);
        _bufferLength = remainingLength;
    }

    private bool TryAppendToBuffer(byte[] buffer, long offset, long size, out MemoryStream stream)
    {
        var requiredLength = _bufferLength + (int)size;
        if (requiredLength > BodyMaxSize)
        {
            WipeBuffer();
            stream = new MemoryStream(Array.Empty<byte>());
            return false;
        }

        EnsureCapacity(requiredLength);
        Buffer.BlockCopy(buffer, (int)offset, _buffer, _bufferLength, (int)size);
        _bufferLength = requiredLength;
        stream = new MemoryStream(_buffer, 0, _bufferLength, writable: false, publiclyVisible: true);
        return true;
    }

    private void EnsureCapacity(int requiredLength)
    {
        if (_buffer.Length >= requiredLength)
            return;

        var newLength = Math.Min(Math.Max(_buffer.Length * 2, requiredLength), BodyMaxSize);
        var newBuffer = ArrayPool<byte>.Shared.Rent(newLength);
        Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _bufferLength);
        ArrayPool<byte>.Shared.Return(_buffer, clearArray: true);
        _buffer = newBuffer;
    }

    private void WipeBuffer()
    {
        ArrayPool<byte>.Shared.Return(_buffer, clearArray: true);
        _buffer = ArrayPool<byte>.Shared.Rent(1024);
        _bufferLength = 0;
        _packetInBuffer = false;
    }
}
