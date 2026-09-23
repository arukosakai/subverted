using System.Buffers.Binary;

namespace Subverted.Protocol;

/// <summary>
/// Length-prefixed framing over a duplex stream: a four-byte little-endian payload length followed
/// by exactly that many bytes.
/// </summary>
/// <param name="maxPayloadBytes">
/// Largest frame this side will send or accept. A peer that claims more is refused before the
/// buffer is allocated, so a corrupt length cannot turn into a two-gigabyte allocation.
/// </param>
public sealed class MessageFramer(int maxPayloadBytes)
{
    private const int HeaderBytes = 4;

    /// <summary>Generous enough for a full status listing of a very large working copy.</summary>
    public const int DefaultMaxPayloadBytes = 64 * 1024 * 1024;

    public static MessageFramer Default { get; } = new(DefaultMaxPayloadBytes);

    public int MaxPayloadBytes { get; } = maxPayloadBytes;

    /// <exception cref="ProtocolException">The payload is larger than <see cref="MaxPayloadBytes"/>.</exception>
    public async ValueTask WriteAsync(
        Stream stream,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken
    )
    {
        if (payload.Length > MaxPayloadBytes)
        {
            throw new ProtocolException(
                $"Refusing to send a {payload.Length} byte frame; the limit is {MaxPayloadBytes}."
            );
        }

        var header = new byte[HeaderBytes];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);

        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    /// <summary>Reads one frame.</summary>
    /// <returns>
    /// The payload, or <c>null</c> when the peer closed the connection cleanly between frames.
    /// A close *part-way* through a frame is corruption, not a clean close, and throws.
    /// </returns>
    /// <exception cref="ProtocolException">
    /// The length prefix is negative or over <see cref="MaxPayloadBytes"/>, or the stream ended
    /// mid-frame.
    /// </exception>
    public async ValueTask<byte[]?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[HeaderBytes];
        if (!await TryReadExactlyAsync(stream, header, cancellationToken))
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > MaxPayloadBytes)
        {
            throw new ProtocolException(
                $"Peer announced a {length} byte frame; the limit is {MaxPayloadBytes}."
            );
        }

        var payload = new byte[length];
        if (length > 0 && !await TryReadExactlyAsync(stream, payload, cancellationToken))
        {
            throw new ProtocolException($"Stream ended before any of a {length} byte payload.");
        }

        return payload;
    }

    /// <returns><c>false</c> when the stream was already at its end; <c>true</c> once full.</returns>
    /// <exception cref="ProtocolException">The stream ended after a partial read.</exception>
    private static async ValueTask<bool> TryReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken
    )
    {
        var filled = 0;
        while (filled < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[filled..], cancellationToken);
            if (read == 0)
            {
                if (filled == 0)
                {
                    return false;
                }

                throw new ProtocolException(
                    $"Stream ended {filled} bytes into a {buffer.Length} byte read."
                );
            }

            filled += read;
        }

        return true;
    }
}
