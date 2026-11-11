using System;

namespace FlyingGame.Core.Recording;

/// <summary>
/// PACKS ONE RECORD INTO BYTES, with no allocation: one preallocated scratch array, rewound each sample.
///
/// Floats go out as little-endian IEEE-754 through <see cref="BitConverter.SingleToInt32Bits"/> and four manual
/// byte writes. That is deliberate rather than BitConverter.GetBytes, which allocates a four-byte array per call —
/// 150 of those per sample, 50 times a second, is 30 000 garbage arrays a second in the physics step, which is
/// precisely the way a recorder ends up describing itself instead of the aeroplane.
/// </summary>
public sealed class RecordWriter
{
    private readonly byte[] _rec;
    private int _at;

    public RecordWriter(int recordBytes) { _rec = new byte[recordBytes]; }

    public byte[] Buffer => _rec;
    public int Length => _rec.Length;
    public int Written => _at;

    public void Begin(float timeSec, byte rateCode, int eventBits)
    {
        _at = 0;
        F32(timeSec);
        _rec[_at++] = rateCode;
        _rec[_at++] = 0;                                  // reserved, keeps the preamble 8 bytes and 4-byte aligned
        _rec[_at++] = (byte)(eventBits & 0xFF);
        _rec[_at++] = (byte)((eventBits >> 8) & 0xFF);
    }

    public void F32(float v)
    {
        int bits = BitConverter.SingleToInt32Bits(v);
        _rec[_at++] = (byte)bits;
        _rec[_at++] = (byte)(bits >> 8);
        _rec[_at++] = (byte)(bits >> 16);
        _rec[_at++] = (byte)(bits >> 24);
    }

    public void F32(double v) => F32((float)v);
    public void Bool(bool v) => F32(v ? 1f : 0f);

    /// <summary>Pad any channels a type does not have, so every record in a file is the same width whatever is
    /// fitted. A float NaN would be honest but unreadable in a spreadsheet; 0 with the channel table saying what
    /// the column is, is the lesser evil.</summary>
    public void Pad(int channels) { for (int i = 0; i < channels; i++) F32(0f); }

    /// <summary>True when exactly the declared number of channels was written. The recorder asserts this once at
    /// start-up rather than every sample: a schema and a filler that disagree would silently shear every column.</summary>
    public bool Complete => _at == _rec.Length;
}
