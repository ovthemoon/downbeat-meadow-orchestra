# hand-udp-v1: the hand tracker's UDP format

The hand tracker (`tracker/`, Python) sends one UDP datagram per tracked frame to the Unity app on the same PC, `127.0.0.1:47800` by default.
Only hand coordinates travel. Camera frames never leave the tracker and are never saved.

All numbers are **little endian**. One datagram is one frame: 44 bytes of header, then 260 bytes per hand (0 to 2 hands).
A datagram of any other size, or with a wrong magic or version, is not a hand-udp-v1 packet and is ignored.

## Header (44 bytes)

| Offset | Type | Name | Meaning |
|---|---|---|---|
| 0 | 4 bytes | `magic` | ASCII `DBH1` |
| 4 | u8 | `version` | `1` |
| 5 | u8 | `state` | tracker state, same numbers as `Downbeat.Core.TrackerState`: 2 Running, 4 CameraMissing, 5 CameraError, 6 ModelFailed (0 Off, 1 Starting, 3 CameraDenied exist in the enum and are not sent by this tracker) |
| 6 | u8 | `hand_count` | 0 to 2; the number of hand blocks that follow |
| 7 | u8 | `reserved` | 0 |
| 8 | u64 | `seq` | frame counter of this tracker run, 0, 1, 2, ... (every datagram, including "camera lost" notices). **Restarts from 0 when the tracker restarts** |
| 16 | f64 | `capture_ms` | when the camera frame was taken, ms on the tracker's monotonic clock (0 = tracker start). For a camera without a capture timestamp this is the time the frame was read |
| 24 | f64 | `infer_end_ms` | when hand detection finished, same clock |
| 32 | f64 | `send_ms` | when the datagram was built, same clock |
| 40 | f32 | `aspect` | image width / height |

## Hand block (260 bytes, repeated `hand_count` times)

| Offset in block | Type | Name | Meaning |
|---|---|---|---|
| 0 | i32 | `id` | 0 or 1, kept while the hand stays in view (see below) |
| 4 | f32 | `score` | MediaPipe handedness score, 0..1 |
| 8 | 21 x (f32 x, f32 y, f32 z) | `landmarks` | the 21 MediaPipe hand landmarks (0 = wrist, 5/9/13/17 = finger bases). **x, y are 0..1 of the camera image, not mirrored**; y grows downward; z is MediaPipe's relative depth |

## Rules

- **A frame is sent for every processed camera frame, also when no hand is visible** (`hand_count = 0`). The Unity side relies on this to know the tracker is alive; it treats 2 s without any datagram as "tracker gone" (`hand.trackerTimeoutSec`).
- **Camera lost**: the tracker sends `state = 4` (no camera found) or `5` (camera error), `hand_count = 0`, twice a second, and tries to reopen the camera once a second. **Model failed**: `state = 6`, twice a second.
- **Left-right mirroring is not done here.** Unity mirrors x when `hand.mirrorX` is true (a webcam image is a mirror of what a person sees).
- **Hand ids** follow the wrist: a detection is matched to the hand that was nearest in the previous frames, best overall match first. A hand that moves more than 0.25 (of the normalized image) between two frames is a new hand; an id stays reserved for 300 ms for a hand that is not seen, and is free after that. Ids are 0 or 1 because Unity's reader only accepts those.
- **A `seq` that goes far back means the tracker restarted.** The Unity side starts a new stream when `seq` is below 16, or more than 64 below the last one received. A smaller step back is a datagram that arrived late; it is dropped. The Unity side also reads at most 8 datagrams per frame (the ring holds 8; older ones are overwritten).
- A datagram is built and sent within the same millisecond range as `send_ms`; there is no acknowledgement, retransmission or ordering guarantee. Lost or reordered frames are harmless: every frame carries the full state.

## Test fixtures

Python writes these and Unity reads the same files (`tracker/tests/test_protocol.py::test_writes_fixtures`, `HandUdpCodecTests`):

| File | Content |
|---|---|
| `tests/fixtures/hand-udp-v1/two-hands.bin` / `.json` | 564 bytes, two hands (ids 0 and 1), `seq` 1234567890123 (needs 64 bits), state Running |
| `tests/fixtures/hand-udp-v1/no-hands.bin` / `.json` | 44 bytes, `hand_count` 0 |

The `.json` has the same values as the `.bin`: `seq`, `state`, `capture_ms`, `infer_end_ms`, `send_ms`, `aspect`, and `hands[]` with `id`, `score` and `landmarks` as a **flat** list `[x0, y0, z0, x1, y1, z1, ...]` (63 numbers; flat because Unity's `JsonUtility` cannot read nested lists). Values are multiples of 1/1024 so that float32 and JSON agree exactly.

## Versioning

Any change of a field or size gets a new `version` (and magic stays `DBH`). A receiver that sees a version it does not know ignores the datagram.
