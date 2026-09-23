using System.Buffers.Binary;

namespace Subverted.Protocol.Tests;

/// <summary>
/// The wire format is the one thing a daemon and a front-end from different builds must agree on
/// byte for byte, so the boundaries here are pinned rather than described.
/// </summary>
public sealed class MessageFramerTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    public async Task A_payload_comes_back_byte_for_byte()
    {
        var payload = new byte[] { 1, 2, 250, 0, 7 };
        var stream = new MemoryStream();

        await MessageFramer.Default.WriteAsync(stream, payload, None);
        stream.Position = 0;

        await Assert
            .That(await MessageFramer.Default.ReadAsync(stream, None))
            .IsEquivalentTo(payload);
    }

    [Test]
    public async Task Frames_are_read_back_in_the_order_they_were_written()
    {
        var stream = new MemoryStream();
        await MessageFramer.Default.WriteAsync(stream, new byte[] { 1 }, None);
        await MessageFramer.Default.WriteAsync(stream, new byte[] { 2, 2 }, None);
        stream.Position = 0;

        await Assert
            .That(await MessageFramer.Default.ReadAsync(stream, None))
            .IsEquivalentTo(new byte[] { 1 });
        await Assert
            .That(await MessageFramer.Default.ReadAsync(stream, None))
            .IsEquivalentTo(new byte[] { 2, 2 });
    }

    /// <summary>
    /// An empty payload is a message, not a hang-up: reading into a zero-length buffer returns 0,
    /// which is exactly what end-of-stream looks like. The second frame proves the first one did
    /// not swallow the stream.
    /// </summary>
    [Test]
    public async Task An_empty_payload_is_a_frame_and_not_an_end_of_stream()
    {
        var stream = new MemoryStream();
        await MessageFramer.Default.WriteAsync(stream, ReadOnlyMemory<byte>.Empty, None);
        await MessageFramer.Default.WriteAsync(stream, new byte[] { 9 }, None);
        stream.Position = 0;

        await Assert
            .That(await MessageFramer.Default.ReadAsync(stream, None))
            .IsEquivalentTo(Array.Empty<byte>());
        await Assert
            .That(await MessageFramer.Default.ReadAsync(stream, None))
            .IsEquivalentTo(new byte[] { 9 });
    }

    [Test]
    public async Task A_close_between_frames_reads_as_null()
    {
        await Assert.That(await MessageFramer.Default.ReadAsync(new MemoryStream(), None)).IsNull();
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task A_close_part_way_through_the_header_is_corruption_not_a_close(int delivered)
    {
        var stream = new MemoryStream(new byte[delivered]);

        await Assert
            .That(async () => await MessageFramer.Default.ReadAsync(stream, None))
            .Throws<ProtocolException>();
    }

    [Test]
    public async Task A_close_part_way_through_the_payload_is_corruption()
    {
        var stream = Framed(announcedLength: 5, payload: [1, 2]);

        await Assert
            .That(async () => await MessageFramer.Default.ReadAsync(stream, None))
            .Throws<ProtocolException>();
    }

    [Test]
    public async Task A_header_with_no_payload_behind_it_is_corruption_not_a_close()
    {
        var stream = Framed(announcedLength: 5, payload: []);

        await Assert
            .That(async () => await MessageFramer.Default.ReadAsync(stream, None))
            .Throws<ProtocolException>();
    }

    /// <summary>
    /// A socket returns what has arrived, not what was asked for. Any version of this that assumed
    /// one read per frame passes against a MemoryStream and fails on a loaded socket.
    /// </summary>
    [Test]
    public async Task A_frame_split_across_many_reads_is_reassembled()
    {
        var buffer = new MemoryStream();
        await MessageFramer.Default.WriteAsync(buffer, new byte[] { 3, 1, 4, 1, 5, 9 }, None);

        var payload = await MessageFramer.Default.ReadAsync(
            new DribblingStream(buffer.ToArray()),
            None
        );

        await Assert.That(payload).IsEquivalentTo(new byte[] { 3, 1, 4, 1, 5, 9 });
    }

    [Test]
    [Arguments(8, true)]
    [Arguments(9, false)]
    public async Task The_limit_is_the_largest_frame_accepted_not_the_first_one_refused(
        int length,
        bool accepted
    )
    {
        var framer = new MessageFramer(maxPayloadBytes: 8);
        var stream = Framed(length, new byte[length]);

        if (accepted)
        {
            await Assert.That((await framer.ReadAsync(stream, None))!.Length).IsEqualTo(length);
        }
        else
        {
            await Assert
                .That(async () => await framer.ReadAsync(stream, None))
                .Throws<ProtocolException>();
        }
    }

    [Test]
    [Arguments(8, true)]
    [Arguments(9, false)]
    public async Task Writing_refuses_a_payload_over_the_limit(int length, bool accepted)
    {
        var framer = new MessageFramer(maxPayloadBytes: 8);
        var stream = new MemoryStream();

        if (accepted)
        {
            await framer.WriteAsync(stream, new byte[length], None);
            await Assert.That(stream.Length).IsEqualTo(4 + length);
        }
        else
        {
            await Assert
                .That(async () => await framer.WriteAsync(stream, new byte[length], None))
                .Throws<ProtocolException>();
        }
    }

    /// <summary>
    /// Announced lengths are read as signed, so a corrupt high bit arrives as a negative number
    /// rather than as a two-gigabyte allocation.
    /// </summary>
    [Test]
    public async Task A_negative_length_is_refused()
    {
        var stream = Framed(announcedLength: int.MinValue, payload: []);

        await Assert
            .That(async () => await MessageFramer.Default.ReadAsync(stream, None))
            .Throws<ProtocolException>();
    }

    [Test]
    public async Task The_length_prefix_is_four_bytes_little_endian()
    {
        var stream = new MemoryStream();

        await MessageFramer.Default.WriteAsync(stream, new byte[258], None);

        await Assert.That(stream.ToArray()[..4]).IsEquivalentTo(new byte[] { 2, 1, 0, 0 });
    }

    private static MemoryStream Framed(int announcedLength, byte[] payload)
    {
        var bytes = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, announcedLength);
        payload.CopyTo(bytes, 4);
        return new MemoryStream(bytes);
    }

    /// <summary>Hands back one byte per read, the way a socket under load does.</summary>
    private sealed class DribblingStream(byte[] bytes) : Stream
    {
        private int _position;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= bytes.Length || count == 0)
            {
                return 0;
            }

            buffer[offset] = bytes[_position++];
            return 1;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
