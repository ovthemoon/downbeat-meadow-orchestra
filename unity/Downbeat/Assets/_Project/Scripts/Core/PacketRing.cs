using System;

namespace Downbeat.Core
{
    /// <summary>
    /// A small queue of datagrams between the socket thread (<see cref="Push"/>) and the main thread (<see cref="TryPop"/>). Fixed slots,
    /// nothing allocated per packet; when full, the oldest packet is overwritten (a late hand frame is worth less than a fresh one).
    /// </summary>
    public sealed class PacketRing
    {
        public const int SlotBytes = 2048;

        readonly object _lock = new object();
        readonly byte[][] _slots;
        readonly int[] _lengths;
        readonly double[] _arrivedMs;
        int _head;
        int _count;
        long _dropped;
        long _oversize;

        public PacketRing(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _slots = new byte[capacity][];
            _lengths = new int[capacity];
            _arrivedMs = new double[capacity];
            for (var i = 0; i < capacity; i++) _slots[i] = new byte[SlotBytes];
        }

        public int Count { get { lock (_lock) return _count; } }
        /// <summary>Packets overwritten before they were read.</summary>
        public long Dropped { get { lock (_lock) return _dropped; } }
        /// <summary>Packets larger than a slot (never a valid hand packet); they are not stored.</summary>
        public long Oversize { get { lock (_lock) return _oversize; } }

        /// <summary>Copies <paramref name="length"/> bytes of <paramref name="source"/>; the caller may reuse its buffer at once.</summary>
        public void Push(byte[] source, int length) => Push(source, length, 0.0);

        /// <param name="arrivedMs">When the packet came in, on a clock the reader also has (a stopwatch): <see cref="TryPop(byte[], out int, out double)"/> gives it back, so the reader can tell how long the packet waited.</param>
        public void Push(byte[] source, int length, double arrivedMs)
        {
            lock (_lock)
            {
                if (length < 0 || length > SlotBytes)
                {
                    _oversize++;
                    return;
                }
                if (_count == _slots.Length)
                {
                    _head = (_head + 1) % _slots.Length;
                    _count--;
                    _dropped++;
                }
                var tail = (_head + _count) % _slots.Length;
                Buffer.BlockCopy(source, 0, _slots[tail], 0, length);
                _lengths[tail] = length;
                _arrivedMs[tail] = arrivedMs;
                _count++;
            }
        }

        /// <summary>The oldest packet, copied into <paramref name="destination"/> (at least <see cref="SlotBytes"/> long); false when there is none.</summary>
        public bool TryPop(byte[] destination, out int length) => TryPop(destination, out length, out _);

        /// <summary>The oldest packet and the time it arrived (see <see cref="Push(byte[], int, double)"/>).</summary>
        public bool TryPop(byte[] destination, out int length, out double arrivedMs)
        {
            lock (_lock)
            {
                if (_count == 0)
                {
                    length = 0;
                    arrivedMs = 0.0;
                    return false;
                }
                length = _lengths[_head];
                arrivedMs = _arrivedMs[_head];
                Buffer.BlockCopy(_slots[_head], 0, destination, 0, length);
                _head = (_head + 1) % _slots.Length;
                _count--;
                return true;
            }
        }
    }
}
