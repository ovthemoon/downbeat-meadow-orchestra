using System;
using System.Collections;
using System.Collections.Generic;
using Downbeat.Core;
using UnityEngine;

namespace Downbeat.App
{
    /// <summary>
    /// The hand tracker as an <see cref="IHandSource"/>: datagrams (hand-udp-v1) arrive on a background thread into a small ring of the
    /// newest 8; <see cref="Poll"/> turns them into <see cref="HandFrame"/>s on the main thread. No datagram for the configured timeout means
    /// the tracker is gone (state CameraMissing, one frame without hands). A sequence number that falls below <see cref="RestartBelowSeq"/>, or more
    /// than <see cref="RestartDropSeq"/> below the last one, means the tracker restarted: the generation goes up and the frames start a new
    /// stream. Any smaller step back is a late datagram and is dropped. A port that cannot be opened is state CameraError and is tried
    /// again every 5 s.
    /// </summary>
    public sealed class UdpHandSource : IHandSource, IPreparable, IDisposable
    {
        public const int QueueCapacity = 8;
        /// <summary>A tracker that starts again counts from 0: a sequence number below this is a new stream, wherever the old one was.</summary>
        public const long RestartBelowSeq = 16;
        /// <summary>A sequence number this far (or more) behind the last one is a new stream; a smaller step back is a late packet.</summary>
        public const long RestartDropSeq = 64;
        const double RetryIntervalMs = 5000.0;
        const double MsPerSecond = 1000.0;
        const float DefaultAspect = 4f / 3f;

        readonly IPacketReceiver _receiver;
        readonly HandSettingsHolder _settings;
        readonly Func<double> _nowMs;
        readonly Func<double> _arrivalClockMs;
        readonly PacketRing _ring = new PacketRing(QueueCapacity);
        readonly byte[] _packet = new byte[PacketRing.SlotBytes];
        readonly double[] _buffer = new double[HandBufferLayout.Length];

        bool _open;
        bool _timedOut;
        bool _hasSeq;
        long _lastSeq;
        double _nextRetryMs;
        double _lastPacketMs;
        double _lastCaptureMs;
        float _lastAspect = DefaultAspect;

        /// <param name="arrivalClockMs">The clock the socket thread stamps a packet with when it comes in, and <see cref="Poll"/> reads again to see how long it waited: it must be readable from any thread
        /// (default: a stopwatch).</param>
        public UdpHandSource(IPacketReceiver receiver, HandSettingsHolder settings, Func<double> nowMs, Func<double> arrivalClockMs = null)
        {
            _receiver = receiver;
            _settings = settings;
            _nowMs = nowMs;
            _arrivalClockMs = arrivalClockMs ?? StopwatchMs;
        }

        static double StopwatchMs() => System.Diagnostics.Stopwatch.GetTimestamp() * MsPerSecond / System.Diagnostics.Stopwatch.Frequency;

        public TrackerState State { get; private set; } = TrackerState.Starting;
        public int Generation { get; private set; }
        /// <summary>Datagrams that were not valid hand-udp-v1 (or were repeats of a frame already seen).</summary>
        public long BadPackets { get; private set; }
        /// <summary>Datagrams overwritten in the ring because the main thread did not read fast enough.</summary>
        public long DroppedPackets => _ring.Dropped;
        /// <summary>Datagrams that arrived after a newer one (a small step back in the sequence number); they are not played.</summary>
        public long LatePackets { get; private set; }
        /// <summary>
        /// How long the newest packet of the last <see cref="Poll"/> had waited, in milliseconds, between the socket thread and the main thread reading it (the age of the newest tracker packet
        /// when it is used). NaN when that poll brought no hand frame. It does not include the time inside the tracker (camera to send): that is in the packet as capture and inference times on the
        /// tracker's own clock and is not comparable with this process's clock.
        /// </summary>
        public double PacketAgeMs { get; private set; } = double.NaN;

        /// <summary>Called once the content is loaded (the port comes from content.json).</summary>
        public IEnumerator Prepare(Action<string> onFailed)
        {
            Open();
            yield break;
        }

        public void Open() => TryOpen(_nowMs());

        public void Poll(List<HandFrame> frames)
        {
            PacketAgeMs = double.NaN;
            var now = _nowMs();
            if (!_open)
            {
                if (now >= _nextRetryMs) TryOpen(now);
                if (!_open) return;
            }

            var firstOfThisPoll = frames.Count;
            var received = false;
            var newestArrivedMs = double.NaN;
            // at most one queue's worth per frame: a socket that never stops must not keep the main thread in this loop
            for (var popped = 0; popped < QueueCapacity && _ring.TryPop(_packet, out var length, out var arrivedMs); popped++)
            {
                if (!Accept(length, now, frames, firstOfThisPoll)) continue;
                received = true;
                newestArrivedMs = arrivedMs;
            }
            if (received) PacketAgeMs = Math.Max(0.0, _arrivalClockMs() - newestArrivedMs);
            else CheckTimeout(now, frames);
        }

        public double RequestTimeFor(double captureMs) => double.NaN;

        public void Dispose() => _receiver.Dispose();

        bool TryOpen(double now)
        {
            var link = _settings.Link;
            if (_receiver.TryStart(link.UdpPort, OnPacket, out var error))
            {
                _open = true;
                _timedOut = false;
                _lastPacketMs = now;
                State = TrackerState.Starting;
                return true;
            }

            _open = false;
            _nextRetryMs = now + RetryIntervalMs;
            State = TrackerState.CameraError;
            Debug.LogWarning($"[UdpHandSource] cannot listen on 127.0.0.1:{link.UdpPort}: {error}; trying again in {RetryIntervalMs / MsPerSecond:0} s");
            return false;
        }

        void OnPacket(byte[] data, int length) => _ring.Push(data, length, _arrivalClockMs());

        bool Accept(int length, double now, List<HandFrame> frames, int firstOfThisPoll)
        {
            if (!HandUdpCodec.TryDecodeToBuffer(new ReadOnlySpan<byte>(_packet, 0, length), _settings.Link.MirrorX, _buffer, out var seq))
            {
                BadPackets++;
                return false;
            }
            if (_hasSeq && seq == _lastSeq)
            {
                BadPackets++;
                return false;
            }
            var restarted = _hasSeq && seq < _lastSeq && (seq < RestartBelowSeq || seq < _lastSeq - RestartDropSeq);
            if (_hasSeq && seq < _lastSeq && !restarted)
            {
                LatePackets++; // overtaken on its way: the hand has moved on since
                return false;
            }
            if (!HandFrameReader.TryRead(_buffer, now, out var frame, _settings.Pose))
            {
                BadPackets++;
                return false;
            }

            if (restarted)
            {
                Generation++; // the tracker was restarted: its counter and its clock start again
                frames.RemoveRange(firstOfThisPoll, frames.Count - firstOfThisPoll); // what is still unread of the old stream is stale
            }
            _hasSeq = true;
            _lastSeq = seq;
            _lastPacketMs = now;
            _lastCaptureMs = frame.CaptureMs;
            _lastAspect = frame.Aspect;
            _timedOut = false;
            State = frame.State;
            frames.Add(frame);
            return true;
        }

        void CheckTimeout(double now, List<HandFrame> frames)
        {
            if (_timedOut || now - _lastPacketMs < _settings.Link.TrackerTimeoutSec * MsPerSecond) return;
            _timedOut = true;
            State = TrackerState.CameraMissing;
            var silentMs = now - _lastPacketMs;
            var captureMs = _lastCaptureMs + silentMs; // in the tracker's own time base, so that gaps stay measurable
            frames.Add(new HandFrame(_hasSeq ? _lastSeq : 0, captureMs, captureMs, now, TrackerState.CameraMissing, _lastAspect, 0, default, default));
        }
    }
}
