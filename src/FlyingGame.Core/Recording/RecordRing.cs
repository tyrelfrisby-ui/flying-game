using System;

namespace FlyingGame.Core.Recording;

/// <summary>
/// THE RING THE RECORDING LIVES IN: a single preallocated byte array holding the last N minutes of records, written
/// from the physics step with NO ALLOCATION AT ALL.
///
/// WHY A RING AND NOT A GROWING FILE. The recorder is always armed — Ty's test pilots will be flying cards, not
/// navigating menus, and a recorder you have to remember to switch on is off for the flight that mattered. Always
/// armed means it must have a bound, and the bound that keeps the useful part is the RECENT part: when the cap is
/// reached the oldest records are overwritten, so a twenty-minute flight still has the last N minutes including
/// whatever just happened. It also means the burst can reach BACKWARDS — the recorder cannot know an event is
/// coming, so the two seconds before it are already in here when it arrives.
///
/// WHY IT DOES NOT TOUCH THE DISK IN FLIGHT. Nothing is written to storage while flying: the ring is memory, and
/// saving is an explicit copy taken off the hot path and handed to a worker thread. So the per-step cost is one
/// block copy into an array that already exists, and there is no file I/O, no flush and no GC anywhere near the
/// physics. The cost of that choice is that a hard kill loses the buffer, which is why a major event can ask for an
/// automatic save rather than relying on the pilot.
///
/// THREADING: ONE WRITER (the sim) and any number of snapshot readers. <see cref="Append"/> must only ever be
/// called from the stepping thread. <see cref="SnapshotChronological"/> takes a lock, so a save never tears a
/// record in half, and it is the only thing that allocates — deliberately, since it happens when a pilot asks to
/// save and not while he is flying.
/// </summary>
public sealed class RecordRing
{
    private readonly byte[] _buf;
    private readonly object _lock = new object();
    private readonly int _recordBytes;
    private readonly int _capacityRecords;
    private int _writeIndex;        // record slot to write next
    private int _count;             // records held, saturating at capacity
    private long _totalWritten;     // every record ever appended, including those overwritten

    /// <param name="recordBytes">Fixed size of one record.</param>
    /// <param name="capacityBytes">How much memory the ring may use. Rounded DOWN to whole records.</param>
    public RecordRing(int recordBytes, int capacityBytes)
    {
        if (recordBytes <= 0) throw new ArgumentOutOfRangeException(nameof(recordBytes));
        if (capacityBytes < recordBytes) throw new ArgumentOutOfRangeException(nameof(capacityBytes), "the ring must hold at least one record");
        _recordBytes = recordBytes;
        _capacityRecords = capacityBytes / recordBytes;
        _buf = new byte[(long)_capacityRecords * recordBytes <= int.MaxValue
                        ? _capacityRecords * recordBytes
                        : throw new ArgumentOutOfRangeException(nameof(capacityBytes), "ring larger than a single array can hold")];
    }

    public int RecordBytes => _recordBytes;
    public int CapacityRecords => _capacityRecords;
    public int Count { get { lock (_lock) return _count; } }
    public long TotalWritten { get { lock (_lock) return _totalWritten; } }
    /// <summary>True once the ring has wrapped and begun discarding its oldest records.</summary>
    public bool Wrapped { get { lock (_lock) return _totalWritten > _capacityRecords; } }
    public int CapacityBytes => _buf.Length;

    /// <summary>
    /// Append one record. ZERO ALLOCATION: a block copy into an array that already exists, and nothing else. Called
    /// from the physics step, so it does no formatting, no I/O and no bounds-growing.
    /// </summary>
    public void Append(byte[] record, int length)
    {
        if (length != _recordBytes) throw new ArgumentException("a ring holds one size of record", nameof(length));
        lock (_lock)
        {
            Buffer.BlockCopy(record, 0, _buf, _writeIndex * _recordBytes, _recordBytes);
            _writeIndex = _writeIndex + 1 == _capacityRecords ? 0 : _writeIndex + 1;
            if (_count < _capacityRecords) _count++;
            _totalWritten++;
        }
    }

    /// <summary>
    /// REACH BACK INTO RECORDS ALREADY WRITTEN and OR these event bits into them — how the burst covers the two
    /// seconds BEFORE an event the recorder could not have predicted. <paramref name="records"/> counts back from
    /// the most recent. Bounded by what the ring still holds, so an event early in a flight simply marks fewer.
    /// </summary>
    public void MarkRecent(int records, int eventBits, int eventBitsOffset)
    {
        if (records <= 0 || eventBits == 0) return;
        lock (_lock)
        {
            int n = records < _count ? records : _count;
            for (int i = 1; i <= n; i++)
            {
                int slot = _writeIndex - i;
                if (slot < 0) slot += _capacityRecords;
                int at = slot * _recordBytes + eventBitsOffset;
                int cur = _buf[at] | (_buf[at + 1] << 8);
                cur |= eventBits;
                _buf[at] = (byte)(cur & 0xFF);
                _buf[at + 1] = (byte)((cur >> 8) & 0xFF);
            }
        }
    }

    /// <summary>
    /// Every record the ring holds, oldest first. This is the ONLY method here that allocates; it runs when a pilot
    /// saves, never while he is flying.
    /// </summary>
    public byte[] SnapshotChronological()
    {
        lock (_lock)
        {
            var dst = new byte[_count * _recordBytes];
            if (_count == 0) return dst;
            int oldest = _count == _capacityRecords ? _writeIndex : 0;
            int firstRun = _count - oldest > 0 && oldest + _count > _capacityRecords
                         ? _capacityRecords - oldest
                         : _count;
            Buffer.BlockCopy(_buf, oldest * _recordBytes, dst, 0, firstRun * _recordBytes);
            if (firstRun < _count)
                Buffer.BlockCopy(_buf, 0, dst, firstRun * _recordBytes, (_count - firstRun) * _recordBytes);
            return dst;
        }
    }

    public void Clear()
    {
        lock (_lock) { _writeIndex = 0; _count = 0; _totalWritten = 0; }
    }
}
