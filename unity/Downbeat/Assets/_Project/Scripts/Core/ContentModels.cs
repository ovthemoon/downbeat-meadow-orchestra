using System;

namespace Downbeat.Core
{
    // Data transfer objects for content/content.json. Plain public fields so that JsonUtility can fill them.
    // Arrays instead of dictionaries (JsonUtility has no dictionary support); names follow content.json.

    [Serializable]
    public sealed class ContentDto
    {
        public int schemaVersion;
        public string contentVersion;
        public AudioSettingsDto audio;
        public InstrumentDto[] instruments;
        public SongDto song;
        /// <summary>The scored songs a visit can play (song mode). Optional: a content file without them plays as before. Each is checked against the instruments by <see cref="SongBook"/>; one that fails is left out, not an error of the content.</summary>
        public SongEntryDto[] songs;
        /// <summary>Hand gesture tuning. Optional: a content file without it uses the defaults below.</summary>
        public HandSettingsDto hand;
        /// <summary>How a hand plays: by where it is (conduct, the default) or by striking down (strike). Optional: a content file without it conducts with the defaults.</summary>
        public ConductDto conduct;
        /// <summary>Ending the piece with two fists spread apart. Optional: a content file without it uses the defaults of <see cref="CutoffDto"/>.</summary>
        public CutoffDto cutoff;
        /// <summary>The six cells of the hand's place in the picture (two rows of three): where the rows meet, the name and the colour of each. Optional: a content file without it uses the defaults.</summary>
        public CellsDto cells;
        /// <summary>How an open hand and a fist are told apart (the 21 hand points, see <see cref="PoseDto"/>). Optional: a content file without it uses the defaults.</summary>
        public PoseDto pose;
        /// <summary>Automatic quality (<c>--quality Auto</c>, the default): how the picture gets cheaper or dearer by itself. Optional: a content file without it uses the defaults.</summary>
        public AutoQualityDto autoQuality;
        /// <summary>The meadow: camera, ground, sky plates, haze and (in later tasks) wind, grass and flowers.</summary>
        public MeadowDto meadow;
        /// <summary>The nature layers: how the energy of a performance builds up, the flow of a visit, and the three nature instruments.</summary>
        public NatureDto nature;
        public ExperienceDto experience;
        /// <summary>Which screen shows what, and how the player goes full screen.</summary>
        public DisplayDto display;
        /// <summary>The performance record screen: the live flower score and today's cards.</summary>
        public RecordDto record;
        /// <summary>The note trail behind each hand and the names of the cells that show when a hand comes into one. Optional: a content file without it uses the defaults of <see cref="TrailDto"/>.</summary>
        public TrailDto trail;
        /// <summary>The follow-along tutorial that shows after a quiet while. Optional: a content file without it uses the defaults.</summary>
        public TutorialDto tutorial;
    }

    [Serializable]
    public sealed class AudioSettingsDto
    {
        public float masterGainDb;
        public int maxVoices;
        public double minLeadSec;
        public double originDelaySec;
    }

    [Serializable]
    public sealed class InstrumentDto
    {
        public string id;
        public string part;
        public float gainDb;
        /// <summary>MIDI note of step 0 (the lowest note of this instrument on the scale), sounding pitch.</summary>
        public int rootMidi;
        public int maxPitchShift;
        public SampleDto[] samples;
    }

    [Serializable]
    public sealed class SampleDto
    {
        public string id;
        public string file;
        /// <summary>Sounding pitch measured from the recording (not the file name).</summary>
        public int soundingMidi;
        /// <summary>Measured deviation from soundingMidi in cents (positive = the recording is sharp).</summary>
        public float tuneCents;
        /// <summary>Time from sample start until half of its peak is reached, measured after trimming.</summary>
        public float perceivedOnsetOffsetMs;
    }

    [Serializable]
    public sealed class SongDto
    {
        public string id;
        public float bpm;
        public int slotsPerBeat;
        public int beatsPerBar;
        public int rootMidi;
        public int[] scaleSteps;
        public int pitchSteps;
        public PartGapDto[] partMinGapSlots;
        public int maxPushSlots;
    }

    [Serializable]
    public sealed class PartGapDto
    {
        public string part;
        public int slots;
    }

    /// <summary>
    /// One scored song (<c>songs[]</c> in content.json): the chord and the melody of every bar, in the frame of <see cref="SongDto"/> (its BPM, its beats per bar, a straight rhythm).
    /// A note is an object because <c>JsonUtility</c> cannot read arrays inside arrays. Not <c>SongDto</c>, which is the beat grid.
    /// </summary>
    [Serializable]
    public sealed class SongEntryDto
    {
        public string id;
        public string title;
        public string credit;
        /// <summary>Must equal <see cref="SongDto.bpm"/>: the app has one tempo.</summary>
        public int bpm;
        /// <summary>Must equal <see cref="SongDto.beatsPerBar"/>.</summary>
        public int beatsPerBar;
        /// <summary>Only 0.5 (a straight rhythm) is accepted.</summary>
        public float swing = 0.5f;
        /// <summary>Semitones added to the melody for each instrument of the upper row (e.g. -12 puts the harp an octave down). Optional: no change.</summary>
        public MelodyOctaveDto melodyOctave;
        public SongBarDto[] bars;
    }

    [Serializable]
    public sealed class MelodyOctaveDto
    {
        public int harp;
        public int recorder;
        public int bell;
    }

    [Serializable]
    public sealed class SongBarDto
    {
        /// <summary>MIDI note of the bass of the bar.</summary>
        public int bass;
        /// <summary>MIDI notes of the chord of the bar, exactly three.</summary>
        public int[] chord;
        /// <summary>The melody notes of the bar, by slot (0..15) in growing order. A bar may have none.</summary>
        public SongNoteDto[] melody;
    }

    [Serializable]
    public sealed class SongNoteDto
    {
        /// <summary>The slot of the bar, 0..(slots per bar - 1).</summary>
        public int slot;
        public int midi;
    }

    /// <summary>
    /// How a hand strike becomes a note. Speeds are in palm lengths per second (a palm length is the distance from the wrist to the
    /// middle finger base), so the same gesture gives the same number near and far from the camera. All values are starting points
    /// to be tuned by playing; they are data so that no rebuild is needed.
    /// </summary>
    [Serializable]
    public sealed class HandSettingsDto
    {
        /// <summary>Hands detected with less confidence than this are ignored.</summary>
        public float minScore = 0.5f;
        /// <summary>Downward speed that triggers a note.</summary>
        public float triggerSpeed = 3f;
        /// <summary>After a strike the hand must slow below this before it can strike again (hysteresis).</summary>
        public float rearmSpeed = 1f;
        /// <summary>Strike speed that gives the loudest note.</summary>
        public float fullSpeed = 9f;
        /// <summary>Strength of a note that only just triggers.</summary>
        public float minVelocity = 0.3f;
        /// <summary>After the trigger, wait at most this long for the speed to peak before the note is decided (strength comes from the peak).</summary>
        public float peakWaitMs = 50f;
        /// <summary>The note is decided early when the speed falls below this fraction of the peak.</summary>
        public float peakDropRatio = 0.85f;
        /// <summary>Speed is measured over the last this many ms of hand positions.</summary>
        public float velocityWindowMs = 70f;
        /// <summary>The same hand cannot trigger twice within this time.</summary>
        public float minRetriggerMs = 150f;
        /// <summary>A hand missing for longer than this starts over (history cleared).</summary>
        public float maxGapMs = 250f;
        /// <summary>Port on 127.0.0.1 where the hand tracker's datagrams (hand-udp-v1) arrive.</summary>
        public int udpPort = 47800;
        /// <summary>No datagram for this long means the tracker is gone: the hands are dropped and the input falls back.</summary>
        public float trackerTimeoutSec = 2f;
        /// <summary>The tracker sends the camera image as it is; a webcam image is a mirror of what the person sees, so x is flipped when this is true.</summary>
        public bool mirrorX = true;
        /// <summary>Dead band around a pitch step boundary, as a fraction of the full height, so a hand resting on a boundary does not flip steps.</summary>
        public float heightHysteresis = 0.015f;
        /// <summary>Same for the left / middle / right boundaries, as a fraction of the width.</summary>
        public float partHysteresis = 0.03f;
    }

    /// <summary>
    /// Conducting play (docs/conducting-play.md): a hand that is raised keeps the music going, its height is the loudness, its side picks the instrument and how much it moves sets how dense
    /// the notes are. It needs no strike, so it works when the tracker only manages 5 to 10 pictures a second. All distances are palm lengths, times are milliseconds or seconds as named.
    /// </summary>
    [Serializable]
    public sealed class ConductDto
    {
        /// <summary>"conduct" (a raised hand keeps playing) or "strike" (the hand plays by striking down, see <see cref="HandSettingsDto"/>). <c>--play-mode</c> overrides it.</summary>
        public string mode = "conduct";
        /// <summary>How much the palm moved in this long is the amount of movement, in palm lengths per second.</summary>
        public float motionWindowMs = 400f;
        /// <summary>Less movement than this is a hand held still (picture jitter included).</summary>
        public float stillPalms = 0.3f;
        /// <summary>From this much movement on the notes come every half beat (below it: every beat).</summary>
        public float halfBeatPalms = 1.5f;
        /// <summary>From this much movement on the notes come every quarter beat.</summary>
        public float quarterBeatPalms = 4f;
        /// <summary>The strength of a hand held still; the strength grows with the amount the hand moves (loudness is movement, not height).</summary>
        public float minVelocity = 0.25f;
        /// <summary>A hand that moves this much (palm lengths per second) plays at full strength; between <see cref="stillPalms"/> and this the strength rises straight from <see cref="minVelocity"/> to 1.</summary>
        public float loudPalms = 4f;
        /// <summary>Added to the strength of the first beat of a bar.</summary>
        public float downbeatAccent = 0.1f;
        /// <summary>After the hand has gone the music fades to nothing in this long.</summary>
        public float releaseSec = 1.5f;
        /// <summary>When a hand crosses into the next part's band, the part it leaves fades out in this long (a crossfade) instead of <see cref="releaseSec"/>, which is for a hand that goes.</summary>
        public float crossfadeSec = 0.3f;
        /// <summary>A hand that is missing for less than this long (a tracker that sees it only now and then) goes on playing where it was last seen.</summary>
        public float holdMs = 800f;
        /// <summary>One short phrase of scale steps per part; every note of a part is the next step, and a bar line starts the phrase again.</summary>
        public ConductPhraseDto[] phrases;
        /// <summary>In a song, the phrase of a part of the lower row: each step is the number of a tone of the bar's chord (0 = the bass, 1..3 = the three chord notes, 4..6 = the chord notes an octave up), not a step of the scale. Optional: a part without one plays its phrase of <see cref="phrases"/>.</summary>
        public ConductPhraseDto[] songPhrases;
    }

    /// <summary>
    /// The six cells (<c>cells</c> in content.json): the hand's place in the picture is cut into three columns and two rows, and each cell is an instrument (the row 0, the upper one, is the sky and plays in the air, the row 1
    /// is the ground and plays on the grass). The lists are in the order of <see cref="Part"/>. A content file without the block gets these values.
    /// </summary>
    [Serializable]
    public sealed class CellsDto
    {
        public static readonly string[] DefaultLabels = { "하프", "리코더", "종", "낮은 현", "마림바", "손북" };
        public static readonly string[] DefaultColors = { "#F2C46B", "#F4F1E6", "#BFE3FF", "#C9A27A", "#E2453A", "#E8A04A" };

        /// <summary>Where the upper row ends and the lower one begins, as a fraction of the picture's height from the top.</summary>
        public float rowSplit = 0.5f;
        /// <summary>A hand changes row only when it is this far past <see cref="rowSplit"/> (a fraction of the picture's height).</summary>
        public float rowHysteresis = 0.03f;
        /// <summary>The name of each of the six cells, in Korean.</summary>
        public string[] labels = (string[])DefaultLabels.Clone();
        /// <summary>The colour of each of the six cells, #RRGGBB.</summary>
        public string[] colors = (string[])DefaultColors.Clone();
    }

    [Serializable]
    public sealed class ConductPhraseDto
    {
        public string part;
        public int[] steps;
    }

    /// <summary>
    /// Ending the piece with the hands: both hands are fists and they are pulled apart sideways. Distances are palm lengths (the horizontal distance of the two hands, aspect corrected, divided by the
    /// mean palm size), times are seconds. A content file without this block gets these values (tools/lib/content-build.mjs has the same defaults).
    /// </summary>
    [Serializable]
    public sealed class CutoffDto
    {
        /// <summary>The distance between the fists must grow by this much ...</summary>
        public float spreadPalms = 2f;
        /// <summary>... and end at least this far apart ...</summary>
        public float minApartPalms = 3.5f;
        /// <summary>... within this long ...</summary>
        public float windowSec = 1f;
        /// <summary>... and stay so (both still fists) for this long before the piece ends.</summary>
        public float holdSec = 0.2f;
        /// <summary>The voices of the players fade out in this long when the piece ends.</summary>
        public float fadeSec = 0.25f;
        /// <summary>After the piece was ended with the hands no new visit starts for this long, and then only for an open hand.</summary>
        public float restartLockSec = 1.5f;
        /// <summary>How long the word that says the piece is over stays on the screen.</summary>
        public float endWordSec = 1.5f;
    }

    /// <summary>
    /// Open hand or fist, read from the 21 points of a hand: for each of the four fingers (not the thumb) the distance from the wrist to the fingertip divided by the distance from the wrist to the knuckle of the middle finger.
    /// The recorded demo hand (open) measures 1.55 for the little finger up to 1.9 for the index and middle finger; no fist has been recorded yet, so <c>fistRatio</c> is a first guess to tune on the laptop. Every field has a default, so a content file without the block (or without a field) reads as these.
    /// </summary>
    [Serializable]
    public sealed class PoseDto
    {
        /// <summary>A finger with at least this ratio is stretched out; three of the four make an open hand.</summary>
        public float openRatio = PoseSettings.DefaultOpenRatio;
        /// <summary>A finger with at most this ratio is curled; three of the four make a fist. Between the two the finger counts for neither.</summary>
        public float fistRatio = PoseSettings.DefaultFistRatio;
        /// <summary>A new pose is taken over only after this many tracker pictures in a row show it (a single wrong picture changes nothing).</summary>
        public int confirmFrames = PoseSettings.DefaultConfirmFrames;
    }

    /// <summary>
    /// Automatic quality: a controller moves one step at a time along a ladder of (tier, render scale) when the frames are too slow, when the hand tracker is starved of its share of a laptop's power,
    /// and, after a long stretch of clear headroom, back up. Times are seconds, shares are 0..1 of the frame budget.
    /// </summary>
    [Serializable]
    public sealed class AutoQualityDto
    {
        /// <summary>False leaves the picture at the tier of <c>--quality</c> (or the first tier).</summary>
        public bool enabled = true;
        /// <summary>The frames a second a tier without a frame cap is judged against (the display's usual vertical sync).</summary>
        public float vsyncBudgetFps = 60f;
        /// <summary>A frame is slow when it takes more than this many times the budget of its step.</summary>
        public float slowFrameFactor = 1.3f;
        /// <summary>The picture is too dear when at least this share of the time of the last <see cref="windowSec"/> went in slow frames (a frame is slow when it takes over <see cref="slowFrameFactor"/> times its budget).</summary>
        public float overFraction = 0.5f;
        public float windowSec = 4f;
        /// <summary>The shortest time between two steps down, and the same for the first <see cref="confirmSec"/> after the start (the start is checked by the first frames).</summary>
        public float downDwellSec = 10f;
        public float confirmSec = 8f;
        public float confirmDownDwellSec = 3f;
        /// <summary>The shortest time at a step before a step up is tried; every step up that fails (it goes down again before it has held: within <see cref="upHoldSec"/>, or before somebody has played at it for <see cref="performingEvidenceSec"/>) doubles it, up to <see cref="maxUpDwellSec"/>. Steps up only stay within the tier and need frames seen while somebody played.</summary>
        public float upDwellSec = 90f;
        public float maxUpDwellSec = 1800f;
        /// <summary>A step up has held only once it has stayed this long and somebody has played at it for <see cref="performingEvidenceSec"/>: a step down before that counts as a try that failed, even when the evidence time was over (a machine that holds a step for a short while and is then over budget is not forgiven on every round, the wait doubles). 0 forgives as soon as the evidence is there.</summary>
        public float upHoldSec = 300f;
        /// <summary>Headroom for a step up: the GPU time (where it is measured) is below this share of the budget in at least 90% of the frames, and no frame was slow.</summary>
        public float headroomShare = 0.5f;
        /// <summary>The hand tracker is starved when its pictures arrive slower than this for <see cref="trackerStarveSec"/> while the app is busy (its GPU time over <see cref="busyGpuShare"/> of the budget, or not measured).</summary>
        public float trackerMinFps = 12f;
        public float trackerStarveSec = 8f;
        public float busyGpuShare = 0.5f;
        /// <summary>While a change of tier waits for an idle meadow and the frames are still too slow, the render scale goes down within the tier by this much per dwell, no lower than <see cref="pendingScaleFloor"/>.</summary>
        public float pendingScaleStep = 0.1f;
        public float pendingScaleFloor = 0.5f;
        /// <summary>A step up that changes the tier (the grass is built again) needs this many seconds of frames seen while somebody played, with (almost) no slow one: an idle meadow costs far less than a performance.</summary>
        public float performingEvidenceSec = 20f;
        /// <summary>
        /// A step down that was made because the tracker was starved must pay off: the tracker's pictures a second rise by this share within <see cref="starveGainWindowSec"/>. After
        /// <see cref="starveGiveUpAfter"/> step downs in a row that bring no gain the starvation is taken as not caused by the app (a slow camera, a tracker on a busy processor) and is ignored for
        /// <see cref="starveIgnoreSec"/>, during which the frame times alone decide, also for stepping back up.
        /// 20 per cent is about three standard deviations of the count of pictures in the 3 s window at 10 a second; 20 s is two down dwells, long enough for the scale to settle and for the processor clocks that the tracker shares with the app to follow.
        /// </summary>
        public float starveGainShare = 0.2f;
        public float starveGainWindowSec = 20f;
        public int starveGiveUpAfter = 2;
        public float starveIgnoreSec = 600f;
        /// <summary>Each time the experiment fails again after a cooldown the next cooldown is twice as long, up to this (4 hours).</summary>
        public float starveIgnoreMaxSec = 14400f;
        /// <summary>An experiment is cancelled when the tracker is away inside its window (a stall of the camera, a restart): it says nothing. This many cancelled experiments in a row count as one that failed, so that a camera that stalls every few seconds does not walk the picture down for ever (the give-up and its doubling cooldown apply). A completed experiment, with gain or without, starts the count again.</summary>
        public int starveCancelsAsFailure = 3;
        /// <summary>From the lightest (index 0) to the cheapest step. A step names a tier of <c>meadow.quality</c> and the render scale used with it.</summary>
        public AutoQualityStepDto[] ladder;
        public AutoQualityStartDto start;
    }

    [Serializable]
    public sealed class AutoQualityStepDto
    {
        public string tier;
        public float renderScale = 1f;
    }

    /// <summary>Where the ladder starts: a weak or integrated graphics chip starts at <see cref="weakStartStep"/>, anything else at <see cref="strongStartStep"/>; the first frames confirm it.</summary>
    [Serializable]
    public sealed class AutoQualityStartDto
    {
        /// <summary>A graphics device whose name holds one of these words (any case) is taken for an integrated or weak one.</summary>
        public string[] weakNames;
        /// <summary>... unless its name also holds one of these (a discrete card of a maker that also makes integrated ones).</summary>
        public string[] strongNames;
        /// <summary>A device that reports less memory than this (MB; 0 = not checked) is weak; an integrated chip shares the system memory and often reports little.</summary>
        public int weakMemoryMb;
        public int weakStartStep;
        public int strongStartStep;
    }

    // ---- meadow ----------------------------------------------------------------------------------------------------------
    // Colours are "#RRGGBB" (sRGB, as written in an image editor); distances are metres; angles are degrees.

    [Serializable]
    public sealed class MeadowDto
    {
        public MeadowLayoutDto layout;
        public MeadowCameraDto camera;
        public MeadowTerrainDto terrain;
        public MeadowSkyDto sky;
        public MeadowHazeDto haze;
        public MeadowWindDto wind;
        /// <summary>The steady wind: one prevailing direction, gusts that roll across the field, sway, and the cloud shadows that drift with it.</summary>
        public MeadowBreezeDto breeze;
        public MeadowGrassDto grass;
        public MeadowFlowersDto flowers;
        public MeadowParticlesDto particles;
        /// <summary>The effects in the air that the notes of the top row make (a ring of light, rising seeds, star dust). Optional: a content file without it uses the defaults of <see cref="SkyEffectsDto"/>.</summary>
        public SkyEffectsDto skyEffects;
        public MeadowRibbonDto ribbon;
        /// <summary>What <c>--demo-waves</c> does for captures: when it starts and where it puts its ring.</summary>
        public MeadowDemoDto demo;
        /// <summary>The waking moment: how the meadow answers when a hand comes into an idle one.</summary>
        public MeadowWakeDto wake;
        /// <summary>One entry per quality tier, named like the Unity quality levels ("High", "Low"). The first is the default.</summary>
        public MeadowQualityDto[] quality;
    }

    /// <summary>The playing field (see MeadowLayout): the part of the ground a hand reaches.</summary>
    [Serializable]
    public sealed class MeadowLayoutDto
    {
        /// <summary>Width of the field at farZ; it narrows in a straight line to the camera.</summary>
        public float fieldWidth;
        public float nearZ;
        public float farZ;
        /// <summary>Where the horizon is on screen, as a fraction of the picture height from the top.</summary>
        public float horizonScreenY;
    }

    /// <summary>The standing viewer: eye height, how far the view looks down, and the lens. Chosen so that the ground meets the backdrop plates.</summary>
    [Serializable]
    public sealed class MeadowCameraDto
    {
        public float height;
        public float pitchDeg;
        public float fovDeg;
    }

    [Serializable]
    public sealed class MeadowTerrainDto
    {
        public int seed;
        /// <summary>The ground rolls between 0 and this height.</summary>
        public float maxHeight;
        /// <summary>Grid spacing of the ground mesh.</summary>
        public float cellSize;
        /// <summary>The mesh starts this far behind the camera and ends at farZ; beyond the haze the backdrop plate takes over.</summary>
        public float zMin;
        public float zMax;
        /// <summary>The mesh is wider than the view by this much on each side.</summary>
        public float xMargin;
        /// <summary>The bare ground under the grass: darkest in the hollows, lightest on the rises (the grass of the next task covers most of it).</summary>
        public string groundDark;
        public string groundMid;
        public string groundLight;
        /// <summary>Size in metres of the patches of the ground colour.</summary>
        public float patchScale;
        /// <summary>
        /// The near ground undulates a little more: hummocks <c>reliefHeight</c> metres up and down every <c>reliefWavelength</c> metres, at full strength up to <c>reliefFromZ</c> metres in front of the camera and gone
        /// at <c>reliefToZ</c>, before the haze that hands the 3D ground over to the painted plate (0 = flat as before).
        /// </summary>
        public float reliefHeight;
        public float reliefWavelength;
        public float reliefFromZ;
        public float reliefToZ;
    }

    /// <summary>The sky as three plates, quiet blue hour -> playing dusk -> climax sunset (see SkyArc). The light and the fog follow the same three stops.</summary>
    [Serializable]
    public sealed class MeadowSkyDto
    {
        /// <summary>Exactly three: blueHour, mid, sunset, in this order.</summary>
        public SkyStopDto[] stops;
        /// <summary>Where the sun sits sideways in the plates, in degrees to the right of straight ahead (the grass glows most in that column of the screen).</summary>
        public float sunAzimuthDeg;
        /// <summary>The drifting cloud layers over the sky of the plates and the shadows they throw on the meadow.</summary>
        public MeadowCloudsDto clouds;
    }

    [Serializable]
    public sealed class SkyStopDto
    {
        public string id;
        /// <summary>The backdrop picture of this stop, relative to the content folder.</summary>
        public string backdrop;
        /// <summary>Colour of the sun.</summary>
        public string lightColor;
        /// <summary>Strength of the sun (1 is a neutral white sun, above 1 is a bright one).</summary>
        public float lightIntensity;
        /// <summary>How high the sun stands above the horizon, in degrees (it is behind the far hills, so a few degrees; negative = just below).</summary>
        public float sunElevationDeg;
        /// <summary>The colour the far ground fades towards before it reaches the plate.</summary>
        public string fogColor;
        /// <summary>The colour of the sky light on the ground and the grass.</summary>
        public string ambientColor;
        /// <summary>How strong that sky light is (the colour above is only a hue; 1 lights a pure white surface to its own colour).</summary>
        public float ambientIntensity;
        /// <summary>Hue of the sunlit side of the clouds at this stop (only the hue counts, not the brightness; the cross-fade of the stops mixes it like the other colours).</summary>
        public string cloudLit;
        /// <summary>Colour of the shaded side of the clouds.</summary>
        public string cloudShade;
    }

    /// <summary>
    /// The band where the 3D ground hands over to the painted plate (a band of picture rows, see fromRow and toRow). In between the
    /// ground fades first into a soft, blurred version of the plate behind it (so no painted detail ghosts through), and the plate gets sharp again
    /// towards the end of the band, so that at the end the two are the same pixel.
    /// </summary>
    [Serializable]
    public sealed class MeadowHazeDto
    {
        /// <summary>The picture rows (0 top, 1 bottom) between which the ground fades into the plate: fully 3D at fromRow and below, fully the plate at toRow and above. A row band, so the hand-over is the same across the whole width and follows no terrain, no field edge.</summary>
        public float fromRow;
        public float toRow;
        /// <summary>Fraction (0..1) of the fade at which the haze colour has moved from the stop's fog colour to the plate's own colour.</summary>
        public float plateFrom;
        /// <summary>Fraction (0..1) of the fade after which the plate behind is shown sharp again (before that it is blurred by blurLod).</summary>
        public float sharpFrom;
        /// <summary>How blurred the plate is where it is used as haze: the level of the plate's mip chain (0 sharp, 4 soft, 6 very soft).</summary>
        public float blurLod;
        /// <summary>Shape of the fade: 1 is linear, above 1 keeps the near ground clear for longer.</summary>
        public float curve;
    }

    /// <summary>The wind. Bend values are fractions of a blade's height: 1 pushes the tip one blade-height sideways (times bendScale).</summary>
    [Serializable]
    public sealed class MeadowWindDto
    {
        /// <summary>Width of a wave front in metres (the d in exp(-d^2 / width^2)).</summary>
        public float waveWidth;
        /// <summary>A hand pushes the grass within this many metres.</summary>
        public float handRadius;
        /// <summary>How many blade-heights the tip moves for a bend of 1.</summary>
        public float bendScale;
        /// <summary>The most a blade (or a flower stem) bends in total, in degrees along a circle as long as the blade: near a fast hand the blades lie down towards this angle instead of stretching, a harder hand further. Under 180, so that the tip stays above its foot; 100 or more keeps the resting meadow as it was.</summary>
        public float maxBendDeg = 120f;
        /// <summary>How far grass is pushed away from a hand, as a bend.</summary>
        public float handPush;
        /// <summary>How much grass lies in the direction a hand moves, as a bend per metre per second.</summary>
        public float handLean;
        /// <summary>The hand's place is also read this far ahead along its motion (seconds) to know how fast it moves over the ground.</summary>
        public float handLookAheadSec;
        /// <summary>The gust that goes with a moving hand: how wide its body is (metres; the grass swings back about 1.9 widths behind the hand).</summary>
        public float handGustWidth;
        /// <summary>The fastest hand (metres per second) that makes the gust stronger; a faster one is read as this fast.</summary>
        public float handSpeedCap;
        /// <summary>A sweep that crosses the field on every beat (the beat wiring comes later; the debug key Space makes one).</summary>
        public WindSweepDto beatSweep;
        /// <summary>A ring around every played note (a mouse click makes one).</summary>
        public WindRadialDto noteRadial;
        /// <summary>The long low wave that lays the grass towards the side of the hand (the low string). Optional: a content file without it uses the defaults of <see cref="WindLongWaveDto"/>.</summary>
        public WindLongWaveDto longWave;
        /// <summary>The strong wide ring of the hand drum. Optional: a content file without it uses the defaults of <see cref="WindDrumRingDto"/>.</summary>
        public WindDrumRingDto drumRing;
    }

    [Serializable]
    public sealed class WindSweepDto
    {
        public float amplitude;
        /// <summary>The amplitude grows by this fraction at full performance energy.</summary>
        public float energyGain;
        /// <summary>The first beat of a bar is this many times stronger.</summary>
        public float downbeatGain;
        public float speed;
        public float lifeSec;
    }

    [Serializable]
    public sealed class WindRadialDto
    {
        public float amplitude;
        /// <summary>The amplitude grows by this fraction at full velocity.</summary>
        public float velocityGain;
        public float speed;
        public float lifeSec;
    }

    /// <summary>
    /// The steady wind. Bend values are fractions of a blade's height before <see cref="MeadowWindDto.bendScale"/>. The wind blows towards <see cref="directionDeg"/> (0 = to the right,
    /// 90 = away from the viewer); the grass leans a little that way and sways around the lean, and broad gusts roll over the field and out over the painted far meadow.
    /// </summary>
    [Serializable]
    public sealed class MeadowBreezeDto
    {
        /// <summary>Where the wind blows to, in degrees on the ground: 0 = +x (right), 90 = +z (away from the camera).</summary>
        public float directionDeg;
        /// <summary>The steady lean of the grass in the wind direction, as a bend.</summary>
        public float lean;
        /// <summary>The whole wind swells and eases once in this many seconds.</summary>
        public float meanPeriodSec;
        /// <summary>How much of the wind the slow swell adds and takes (0.2 = plus and minus a fifth).</summary>
        public float meanVariation;
        /// <summary>Blades differ only in how far they bend: a factor between these two (one number per blade).</summary>
        public float stiffnessMin;
        public float stiffnessMax;
        public BreezeGustDto gust;
        public BreezeSwayDto sway;
        public BreezeFlutterDto flutter;
        public BreezePlateDto plate;
    }

    [Serializable]
    public sealed class BreezeGustDto
    {
        /// <summary>Metres between two gusts along the wind (on average).</summary>
        public float spacing;
        /// <summary>How fast a gust travels over the ground (m/s); a point of the field meets one every spacing / speed seconds.</summary>
        public float speed;
        /// <summary>Width of a gust in metres: the grass leans for about this long (the front is steeper than the back).</summary>
        public float width;
        /// <summary>The bend the strongest gust adds.</summary>
        public float amplitude;
        /// <summary>The weakest gust as a share of the strongest (0..1).</summary>
        public float weakest;
        /// <summary>How far a gust is moved from an even rhythm, as a share of the spacing (0..0.35).</summary>
        public float jitter;
        /// <summary>The front of a gust is not a straight line: it waves sideways by up to this many metres.</summary>
        public float frontWobble;
        /// <summary>Across the wind, one wave of the front is this many metres long.</summary>
        public float wobbleLength;
        /// <summary>The top of a blade meets a gust this many metres later than its foot: the blade whips and springs back.</summary>
        public float lagMetres;
        /// <summary>Behind a gust the grass swings back past upright by this share of the push (0..0.6).</summary>
        public float springBack;
        /// <summary>The tips brighten under a gust by this share (0.3 = a third brighter at the strongest).</summary>
        public float brightness;
    }

    [Serializable]
    public sealed class BreezeSwayDto
    {
        /// <summary>Metres from one crest of the slow sway to the next, along the wind.</summary>
        public float wavelength;
        public float periodSec;
        /// <summary>The bend that the sway adds and takes.</summary>
        public float amplitude;
    }

    [Serializable]
    public sealed class BreezeFlutterDto
    {
        /// <summary>The small, slow motion of the very tips, as a bend (grows with the cube of the height up the blade).</summary>
        public float amplitude;
        public float periodSec;
    }

    /// <summary>How the gusts show on the painted far meadow of the backdrop plates.</summary>
    [Serializable]
    public sealed class BreezePlateDto
    {
        /// <summary>How much a gust brightens the plate (0.1 = a tenth at the strongest).</summary>
        public float rippleGain;
        /// <summary>From this picture row (from the top) down the ripple is at full strength; the 3D grass takes over the picture below the haze, so it is the same stripes there.</summary>
        public float fullFromRow;
        /// <summary>Above this row (the hills) the plate does not ripple.</summary>
        public float noneAtRow;
    }

    /// <summary>
    /// Clouds: two layers (a near and a far one, for parallax) drift over the sky of the plates in the wind direction at <see cref="driftMps"/> metres a second, and the soft shadows
    /// of a cloud deck slide over the meadow and the painted far ground at the same speed and in the same direction.
    /// </summary>
    [Serializable]
    public sealed class MeadowCloudsDto
    {
        /// <summary>How fast the clouds and their shadows move over the ground (m/s), in the direction of <see cref="MeadowBreezeDto.directionDeg"/>.</summary>
        public float driftMps;
        /// <summary>Clouds are fully there above this picture row (from the top) and fade out to nothing at <see cref="fadeToRow"/>, above the hills.</summary>
        public float fadeFromRow;
        public float fadeToRow;
        /// <summary>How far the sky behind a cloud takes the hue of the stop's cloud colours (0 = none, 1 = fully).</summary>
        public float hueShare;
        /// <summary>The sunlit side of a cloud is this many times as bright as the sky behind it, the shaded side this many times (about 1.2 and 0.75: the average brightness of the plate stays).</summary>
        public float litBrightness;
        public float shadeBrightness;
        public CloudLayerDto near;
        public CloudLayerDto far;
        public CloudShadowDto shadow;
    }

    [Serializable]
    public sealed class CloudLayerDto
    {
        /// <summary>How high the layer's plane is above the camera (m): the lower, the faster it seems to move.</summary>
        public float altitude;
        /// <summary>Size of the cloud shapes on the plane (m).</summary>
        public float cellMetres;
        /// <summary>Share of the sky the layer covers (0..1).</summary>
        public float coverage;
        /// <summary>How soft the edges are (0.02..0.5).</summary>
        public float softness;
        /// <summary>How strongly the layer shows (0..1).</summary>
        public float opacity;
    }

    [Serializable]
    public sealed class CloudShadowDto
    {
        /// <summary>Size of the shadow patches on the ground (m).</summary>
        public float cellMetres;
        public float coverage;
        public float softness;
        /// <summary>How much darker the ground is in a shadow (0.2 = a fifth).</summary>
        public float strength;
    }

    /// <summary>The real-time grass: blades placed by a compute shader in depth rings (fewer, wider blades farther away) and drawn in one indirect call per ring.</summary>
    [Serializable]
    public sealed class MeadowGrassDto
    {
        public GrassRingDto[] rings;
        public float heightMin;
        public float heightMax;
        /// <summary>Width of a blade at its base, in metres (rings scale it).</summary>
        public float width;
        /// <summary>How far blades lean on their own, as a fraction of their height.</summary>
        public float lean;
        /// <summary>Size of a clump in metres: blades of one clump share a lean, a height and a shade, and the clump is a little taller in the middle.</summary>
        public float clumpScale;
        /// <summary>How closely a blade follows the lean of its clump (0 every blade leans its own way, 1 the whole clump leans as one).</summary>
        public float clumpLean;
        /// <summary>Size in metres of the hummocks and hollows (several metres).</summary>
        public float hummockScale;
        /// <summary>How much shorter the grass in a hollow is than on a hummock (0 none, 0.5 half as tall).</summary>
        public float hummockHeight;
        /// <summary>How much thinner the grass in a hollow is (0 none, 0.6 only 40 % of the blades).</summary>
        public float hummockDensity;
        /// <summary>How much darker the grass in a hollow is (0 none, 0.7 only 30 % as bright).</summary>
        public float hummockDark;
        /// <summary>The 3D grass is full at this picture row (0 top, 1 bottom) and below, and gone at fadeToRow and above: blades become fewer, shorter and narrower, so that where it ends it is as short as the painted meadow of the plate.</summary>
        public float fadeFromRow;
        public float fadeToRow;
        /// <summary>Height of the last blades of the fade as a fraction of the normal height (a few pixels).</summary>
        public float fadeMinHeight;
        public string rootColor;
        public string midColor;
        public string tipColor;
        /// <summary>The colour of yellow-green clumps (a mid-frequency hue mix with the deep green) and of the few dried ones.</summary>
        public string yellowColor;
        public string driedColor;
        /// <summary>Fraction of the clumps that are dried.</summary>
        public float driedShare;
        /// <summary>Tint of the light that shines through a blade towards the viewer when the sun is behind it: it multiplies the colour of the sun, so the glow follows the sky stops.</summary>
        public string backlightColor;
        /// <summary>Strength of that glow.</summary>
        public float backlight;
        /// <summary>Strength of the bright (HDR) end of the tips in the sun's column; the bloom catches it when the sun is strong.</summary>
        public float tipGlow;
        /// <summary>A subtle rim on the tips of the blades when the sun is low and behind the field (0 = none): the edge of a blade seen at a grazing angle. Not at a weak quality tier (skyDetail 0); no noise.</summary>
        public float tipRim;
        /// <summary>Brightness of the body of the grass right at the viewer's feet (1 normal, 0.4 dark) and the depths in metres (in front of the viewer) between which it returns to normal: the foreground is the darkest part of the picture.</summary>
        public float nearDarkness;
        public float nearDarkStart;
        public float nearDarkEnd;
        /// <summary>Brightness of the grass in the middle distance, relative to the foreground, between the depths farDimFrom and farDimTo (metres in front of the viewer): the middle of the field is hazed by the plate's own mist.</summary>
        public float farDim;
        public float farDimFrom;
        public float farDimTo;
        /// <summary>How narrow the glowing column below the sun is (higher is narrower; the glow fades towards the sides of the screen).</summary>
        public float sunColumn;
        /// <summary>How strongly the low sun lights the body of a blade (wrapped round it).</summary>
        public float sunBody;
        /// <summary>How strongly the slope of the ground shades the grass: ground that falls away from the viewer faces the sun and is brighter, rising ground is darker.</summary>
        public float relief;
        /// <summary>How much of the sky light reaches the grass (the sky stops give its colour and strength).</summary>
        public float ambientMix;
        /// <summary>Strength of the bright edge of a blade seen at a grazing angle.</summary>
        public float rim;
        /// <summary>Fraction of blades whose tip catches the sun like a drop of dew.</summary>
        public float sparkle;
        /// <summary>Colour of the tips that a harp note makes sparkle.</summary>
        public string glowColor;
    }

    /// <summary>A band of the field between two depths. Far bands hold fewer, wider blades: the haze hides the change.</summary>
    [Serializable]
    public sealed class GrassRingDto
    {
        public float zFrom;
        public float zTo;
        /// <summary>Share of the tier's blades per square metre that this band gets.</summary>
        public float densityScale;
        public float widthScale;
        /// <summary>Segments along the blade (more = smoother bend, more triangles).</summary>
        public int segments;
        /// <summary>Brightness of the band's body light (1 normal; the foreground is darker).</summary>
        public float darkness;
        /// <summary>Multiplier on blade height in this band.</summary>
        public float heightScale;
    }

    [Serializable]
    public sealed class MeadowQualityDto
    {
        public string name;
        /// <summary>Blades per square metre in the nearest band.</summary>
        public float grassPerSqm;
        /// <summary>The most fireflies that can be alive at once in this tier (the level 0..1 of the nature layers scales it).</summary>
        public int fireflyMax;
        /// <summary>Flowers in the meadow in this tier.</summary>
        public int flowerCount;
        /// <summary>Most segments a blade may have in this tier (a band asks for its own number; the weaker tier asks for fewer triangles).</summary>
        public int grassMaxSegments;
        /// <summary>
        /// The picture is drawn at this fraction of the window's size per side (the URP render scale) and stretched to the window: 1 is every pixel, 0.7 about half the pixels. On a graphics chip that
        /// shares its power with the processor the pixels are what costs; <c>--render-scale</c> overrides it.
        /// </summary>
        public float renderScale = 1f;
        /// <summary>How the smaller picture is stretched: Auto, Linear (bilinear, no cost), Point or FSR (sharper, a pass of its own).</summary>
        public string upscaler = "Linear";
        /// <summary>The MSAA sample count of this tier (1, 2, 4 or 8); 0 keeps what the tier's pipeline asset says (High 4, Low 1).</summary>
        public int msaa;
        /// <summary>The frames a second this tier is held to (it sleeps instead of drawing more: a chip that is not busy leaves its power to the processor, and so to the hand tracker); 0 keeps the vertical sync of the display. <c>--max-fps</c> overrides it.</summary>
        public int targetFps;
        /// <summary>The glow around bright things: how much work it is.</summary>
        public MeadowBloomQualityDto bloom;
        /// <summary>How the sky is drawn, not whether it moves (the drifting clouds, their shadows over the field and the painted far meadow, and the gust ripple over the far meadow are there at every tier): 1 works the cloud noise out for every pixel of the sky; 0 (anything below 0.5) reads the same clouds from a picture of the noise that is made at the start (one texture read a layer, a little less fine detail), and also leaves the hand gust a plain lean and the tip rim of the grass out.</summary>
        public float skyDetail = 1f;
    }

    /// <summary>The cost of the Bloom of one quality tier; the strength and colour come from the scene's volume profile.</summary>
    [Serializable]
    public sealed class MeadowBloomQualityDto
    {
        public bool enabled = true;
        /// <summary>Gaussian (best), Dual (faster) or Kawase (fastest at low resolutions).</summary>
        public string filter = "Gaussian";
        /// <summary>Half or Quarter: the size of the first (largest) bloom picture, as a fraction of the screen.</summary>
        public string downscale = "Half";
        /// <summary>How many blurred pictures it goes down through (1..16; fewer is cheaper and the glow is tighter).</summary>
        public int maxIterations = 6;
    }

    /// <summary>Flower species as instruments: how many bloom per note, how the three zones blend, how a bloom runs and how the flowers look.</summary>
    [Serializable]
    public sealed class MeadowFlowersDto
    {
        /// <summary>A note opens 1 + round(velocity x maxPerNote) flowers.</summary>
        public int maxPerNote;
        /// <summary>Width (fraction of the picture width) of the mix between two species zones (the zone centres are 0.17, 0.5 and 0.83).</summary>
        public float blend;
        /// <summary>How strongly the grass tips sparkle around a harp note (the glow of its wind ring).</summary>
        public float harpGlow;
        /// <summary>A flower stands this far (metres) clear of grass blades (the grass leaves a bare spot around it).</summary>
        public float clearRadius;
        /// <summary>The green of the stems.</summary>
        public string stemColor;
        /// <summary>How much a closed bud shines by itself (the quiet cue: small points of white and violet that stay visible when the light is low), and how much an open flower does.</summary>
        public float budGlow;
        public float openGlow;
        /// <summary>The light on a flower: how much sky light reaches it, how strongly the low sun lights its body (wrapped round), and how brightly it glows when the sun shines through its petals towards the viewer.</summary>
        public float ambientMix;
        public float sunBody;
        public float translucency;
        /// <summary>Seed of the places the flowers stand.</summary>
        public int seed;
        public BloomDto bloom;
        public FlowerLookDto bellflower;
        public FlowerLookDto dandelion;
        public FlowerLookDto poppy;
    }

    /// <summary>How a bloom runs (see BloomTimeline).</summary>
    [Serializable]
    public sealed class BloomDto
    {
        public float leadSec;
        public float openSec;
        public float holdSec;
        public float closeSec;
        public float restLevel;
    }

    /// <summary>The look of one species: the colour of the closed bud and of the open flower, the heart, and the size.</summary>
    [Serializable]
    public sealed class FlowerLookDto
    {
        public string budColor;
        public string openColor;
        public string centerColor;
        /// <summary>Height of the stem in metres, from the ground to the head.</summary>
        public float stemHeight;
        /// <summary>Size of the open head in metres (across).</summary>
        public float headSize;
    }

    /// <summary>The small things in the air: dandelion seeds and petals thrown by notes, fireflies of the nature layers and a little pollen that is always there.</summary>
    [Serializable]
    public sealed class MeadowParticlesDto
    {
        public string seedColor;
        public string petalColor;
        public string fireflyColor;
        public string pollenColor;
        /// <summary>Seconds a petal lives (<c>seedLifeSec</c> is no longer read: the recorder's seeds rise in the air, <c>meadow.skyEffects.lifeSec</c>).</summary>
        public float seedLifeSec;
        public float petalLifeSec;
        public float fireflyLifeSec;
        /// <summary>Sizes (metres across).</summary>
        public float seedSize;
        public float petalSize;
        public float fireflySize;
        public float pollenSize;
        /// <summary>How much of the wind the particles follow (1 = as the grass does; the wind field is the one of the notes and the beat).</summary>
        public float windFollow;
        /// <summary>The steady wind (meadow.breeze) carries pollen, seeds and petals along its direction at this speed (m/s).</summary>
        public float windDrift;
        /// <summary>Seeds and petals also rise a little at first (m/s) and then sink at this speed.</summary>
        public float riseSpeed;
        public float sinkSpeed;
        /// <summary>Pollen motes alive at any time (always, in every state), and how fast they drift.</summary>
        public int pollenCount;
        public float pollenDrift;
        /// <summary>Fireflies wander within this height above the ground (metres): from, to.</summary>
        public float fireflyHeightMin;
        public float fireflyHeightMax;
        /// <summary>HDR brightness of the glow of fireflies (the camera's bloom catches it); <c>seedGlow</c> is no longer read (the effects in the air have <c>meadow.skyEffects.glow</c>).</summary>
        public float fireflyGlow;
        public float seedGlow;
        /// <summary>Most petals alive at once (the buffer of their particle system); <c>maxSeeds</c> is no longer read (<c>meadow.skyEffects.maxParticles</c> is the buffer of the effects in the air).</summary>
        public int maxSeeds;
        public int maxPetals;
        /// <summary>A thrown seed or petal starts within this many metres of the spot that was played, and this high above the ground there (from, to).</summary>
        public float spawnSpread;
        public float spawnHeightMin;
        public float spawnHeightMax;
        /// <summary>Fireflies: how long of its life (fraction) one fades in and out, how many times it blinks, how dim it gets between blinks (0..1), and how much it wanders (noise strength in metres, frequency).</summary>
        public float fireflyFadeFraction;
        public int fireflyBlinkCount;
        public float fireflyBlinkLow;
        public float fireflyNoiseStrength;
        public float fireflyNoiseFrequency;
        /// <summary>A firefly stays at least this high above the ground, and is born this far inside the edge of the field (metres).</summary>
        public float fireflyFloor;
        public float fireflyEdgeMargin;
        /// <summary>
        /// A bell of the nature score (the fireflies' own instrument) is answered: the nearest <c>fireflyAnswerCount</c> fireflies within <c>fireflyAnswerRadius</c> metres of where the bell is heard glow brighter
        /// (<c>fireflyAnswerGain</c> = how much more at the peak) for a moment: up in <c>fireflyAnswerAttackSec</c>, away in <c>fireflyAnswerDecaySec</c>.
        /// </summary>
        /// <summary>The fewest pixels a seed or a petal is drawn across, however far away it is (the size floor; 0 = none): far ones stay a few points of light instead of flickering under a pixel.</summary>
        public float particleMinPixels;
        public int fireflyAnswerCount;
        public float fireflyAnswerRadius;
        public float fireflyAnswerAttackSec;
        public float fireflyAnswerDecaySec;
        public float fireflyAnswerGain;
    }

    /// <summary>The ribbon of light that follows a hand: a trail of additive light, a little above the grass, that fades when the hand is gone.</summary>
    [Serializable]
    public sealed class MeadowRibbonDto
    {
        public string color;
        public string tailColor;
        /// <summary>How long the trail is (seconds of the hand's path) and how wide it is (metres).</summary>
        public float trailSec;
        public float width;
        /// <summary>Height above the ground of the hand's place (metres).</summary>
        public float height;
        /// <summary>It appears within appearSec and fades out over fadeSec when the hand is gone.</summary>
        public float appearSec;
        public float fadeSec;
        /// <summary>HDR brightness of the head of the ribbon.</summary>
        public float glow;
        /// <summary>The trail adds a point after the hand has moved this far (metres); smaller is smoother and costs more vertices.</summary>
        public float minVertexDistance;
        /// <summary>How opaque the head and the middle of the trail are (0..1; the tail is always clear), and how wide the middle is compared with the head (0..1; the tail is a point).</summary>
        public float headAlpha;
        public float midAlpha;
        public float midWidth;
        /// <summary>
        /// How the ribbon follows the hand between tracker frames (<see cref="HandRibbonFollow"/>, a One Euro filter on the field): the cut-off of a still
        /// hand (Hz; lower is steadier and lags more, 0 turns the filter off), how much each m/s of hand speed raises it (Hz per m/s), and the cut-off
        /// that smooths the measured speed (Hz).
        /// </summary>
        public float followMinHz;
        public float followBeta;
        public float followSpeedHz;
    }

    /// <summary>The scripted waves of <c>--demo-waves</c> (captures): a sweep and two rings a moment after start.</summary>
    [Serializable]
    public sealed class MeadowDemoDto
    {
        public float startSec;
        /// <summary>Depth (metres) of the first ring; the second is 3 m farther.</summary>
        public float radialDepth;
        public float radialVelocity;
    }

    /// <summary>
    /// The waking moment (a hand comes into an idle meadow): the buds within <c>radius</c> metres glint in a wave that spreads from the hand at <c>speed</c> metres a second
    /// (at most <c>maxFlowers</c>, each up to <c>jitterSec</c> late), with one soft gust of wind from the same spot.
    /// </summary>
    [Serializable]
    public sealed class MeadowWakeDto
    {
        public float radius;
        public float speed;
        public int maxFlowers;
        public float jitterSec;
        /// <summary>The gust is a wind ring like a note's, softer: its amplitude (a note's is meadow.wind.noteRadial.amplitude), how long it lives and how much the tips it passes sparkle.</summary>
        public float gustAmplitude;
        public float gustLifeSec;
        public float gustGlow;
    }

    // ---- nature and experience ---------------------------------------------------------------------------------------------

    /// <summary>The nature layers (fireflies, sky, hills): when they join and what they play.</summary>
    [Serializable]
    public sealed class NatureDto
    {
        /// <summary>Nature notes are a written score, so they are put on the audio clock at least this many bars ahead (a stall of the main thread of up to that long does not drop one). Hand notes stay at 0.1 s.</summary>
        public int leadBars;
        /// <summary>Seed of the chance in the nature score (which off-beat gets a firefly bell): the same seed gives the same score in every run.</summary>
        public int seed;
        /// <summary>The performance energy: how much a note adds, how fast it fades, where the levels are.</summary>
        public NatureEnergyDto energy;
        public NatureRulesDto rules;
        /// <summary>Exactly three, one per layer: fireflies, sky, hills.</summary>
        public NatureInstrumentDto[] instruments;
        /// <summary>How fast the picture follows the energy (seconds to cover most of a change): the sky does not jump with every note.</summary>
        public float visualSmoothSec;
        /// <summary>How much brighter the far hills' haze gets on a drum beat (0..1), and how fast it fades (seconds).</summary>
        public float hillPulse;
        public float hillPulseSec;
        /// <summary>The picture rows (0 top, 1 bottom) of the far hills that the pulse brightens: the centre and half the height of the band.</summary>
        public float hillPulseRow;
        public float hillPulseHalfHeight;
        /// <summary>
        /// Where the sky is on its arc as a function of the performance energy: the sky stays at the blue hour up to <c>skyEnergyLow</c>, is at the sunset (1.0) from <c>skyEnergyHigh</c> on, and goes straight
        /// between. With the energies of conducting play (docs/conducting-play.md: a held still hand 0.26 to 0.39, a slow hand 0.51 to 0.62, a hand that conducts 0.85 to 0.98) a strong sustained performance
        /// reaches the sunset and a held still hand never does.
        /// </summary>
        public float skyEnergyLow;
        public float skyEnergyHigh;
        /// <summary>How much the pulse follows what is painted (0 = one flat band of haze, 1 = only where the plate is hazy: the mist in the valleys and the far ridges, not the dark trees).</summary>
        public float hillPulseFollow;
    }

    [Serializable]
    public sealed class NatureEnergyDto
    {
        /// <summary>A note adds riseBase + risePerVelocity x velocity.</summary>
        public float riseBase;
        public float risePerVelocity;
        public float halfLifeSec;
        /// <summary>Energy at which level 1, 2 and 3 start (ascending).</summary>
        public float level1;
        public float level2;
        public float level3;
        /// <summary>A level ends only when the energy is this far below the energy at which it started (no flicker at a boundary).</summary>
        public float hysteresis;
    }

    [Serializable]
    public sealed class NatureRulesDto
    {
        /// <summary>Fireflies: a bell on slots where slot % slotsPerBeat == offbeatSlot, with this chance, on one of the top topSteps pitch steps.</summary>
        public int offbeatSlot;
        public float fireflyChance;
        public int fireflyTopSteps;
        public float fireflyGain;
        /// <summary>Sky: a chord on the first slot of every bar; the chord changes every barsPerChord bars between the given sets of scale degrees (as pitch steps of the first octave).</summary>
        public SkyChordDto[] chords;
        public int barsPerChord;
        public float skyGain;
        /// <summary>Hills: a drum on the first slot of every bar and on this slot of the bar (the third beat) at accentGain.</summary>
        public int hillsAccentSlot;
        public float hillsAccentGain;
        public float hillsGain;
    }

    [Serializable]
    public sealed class SkyChordDto
    {
        public int[] steps;
    }

    /// <summary>A nature instrument: like an <see cref="InstrumentDto"/> (samples, root note, gain) for one layer instead of one part.</summary>
    [Serializable]
    public sealed class NatureInstrumentDto
    {
        public string id;
        /// <summary>fireflies | sky | hills</summary>
        public string layer;
        public float gainDb;
        public int rootMidi;
        public int maxPitchShift;
        public SampleDto[] samples;
    }

    /// <summary>The flow of a visit: Idle -> Waking -> Playing -> Settling -> Idle.</summary>
    [Serializable]
    public sealed class ExperienceDto
    {
        /// <summary>Waking turns into Playing at the first note or after this many seconds.</summary>
        public float wakingSec;
        /// <summary>No hands for this long (a tracker that stops counts as no hands) and Playing turns into Settling.</summary>
        public float noHandsSec;
        /// <summary>Settling turns into Idle when the energy is below this and Settling has lasted at least settlingMinSec.</summary>
        public float idleEnergy;
        public float settlingMinSec;
        /// <summary>The mouse and the keyboard have no hand: a note of theirs counts as a hand for this long (seconds).</summary>
        public float inputPresenceSec;
        /// <summary>Whether visits play the scored songs (<see cref="ContentDto.songs"/>) or the free phrases. The switch of the settings panel and <c>--songs</c> decide over it.</summary>
        public bool songMode = true;
        /// <summary>How long the title of a song shows when its first bar begins (seconds).</summary>
        public float songTitleSec = 3f;
    }

    // ---- display and record screen -----------------------------------------------------------------------------------------

    /// <summary>Screens. Two or more: the meadow on one and the record on the other; one: the record is a strip over the lower part of the meadow.</summary>
    [Serializable]
    public sealed class DisplayDto
    {
        /// <summary>auto (two screens when there are two) | dual | single</summary>
        public string mode;
        /// <summary>Which screen shows the meadow (0 = the first); the record goes to the other one.</summary>
        public int meadowIndex;
        /// <summary>Which screen shows the record when there are two or more (never the meadow's own).</summary>
        public int recordIndex;
        /// <summary>borderless (a window the size of the screen: alt-tab and a second screen stay calm) | exclusive (the screen is the app's own). The player goes full screen only when it was started full screen; this only picks the kind.</summary>
        public string fullscreenMode;
        /// <summary>The height of the record strip over the meadow when there is only one screen, as a fraction of the screen.</summary>
        public float overlayFraction;
    }

    [Serializable]
    public sealed class RecordDto
    {
        /// <summary>How many seconds of the live score are on the screen (the right edge is now).</summary>
        public float windowSec;
        public int scoreCapacity;
        /// <summary>How many of today's newest cards are shown (1..12), in one row of up to 6.</summary>
        public int cardsShown;
        /// <summary>A visit with fewer notes than this makes no card.</summary>
        public int minNotes;
        /// <summary>A card keeps at most this many marks of the score.</summary>
        public int cardMaxMarks;
        /// <summary>Folder (inside the app's data folder) for the cards; the cards of a day are in a subfolder named by the date.</summary>
        public string cardFolder;
        /// <summary>How often the screen looks whether the date has changed (seconds).</summary>
        public float dateCheckSec;
        public int thumbnailWidth;
        public int thumbnailHeight;
        /// <summary>How many slow motes (fireflies and a few petals) drift behind the panels of the record screen while nobody plays (0 = none, at most 64). One draw call whatever the number.</summary>
        public int idleMotes = 24;
        /// <summary>How fast a mote glides, in screen heights per second (each one has its own 0.7 to 1.3 times this): a few percent, so that the screen is alive but never busy.</summary>
        public float idleMoteSpeed = 0.02f;
        /// <summary>The brightest a mote gets (0..1); low on purpose, the live score must stay the brightest thing.</summary>
        public float idleMoteAlpha = 0.4f;
        /// <summary>The share of their brightness the motes keep while a performance is on (0..1); they come back when the meadow is idle again.</summary>
        public float idleMoteDimmed = 0.3f;
        /// <summary>Operating-system fonts to try in order (a Korean one first): the app carries no font file. A content.json from before this key has none and gets these (tools/lib/content-build.mjs has the same default).</summary>
        public string[] fontNames = { "Apple SD Gothic Neo", "Malgun Gothic", "NanumGothic", "Noto Sans CJK KR" };
        public RecordColorsDto colors;
        public RecordTextDto text;
    }

    /// <summary>
    /// The follow-along tutorial (see <c>TutorialDirector</c>): when nobody has played for <see cref="idleDelaySec"/> it dims the meadow and shows three steps of <see cref="stepSec"/> each, round and round, until an open hand
    /// shows. A content.json from before this block has none and gets these values (tools/lib/content-build.mjs has the same defaults).
    /// </summary>
    [Serializable]
    public sealed class TutorialDto
    {
        /// <summary>The meadow has been quiet this long (after a visit, and after the app started) before the tutorial shows.</summary>
        public float idleDelaySec = 10f;
        /// <summary>Each of the three steps is shown this long.</summary>
        public float stepSec = 5f;
        public float fadeInSec = 0.6f;
        /// <summary>An open hand fades the tutorial out over this long (the music does not wait for it).</summary>
        public float fadeOutSec = 0.8f;
        /// <summary>How dark the meadow gets behind the tutorial (0..0.9).</summary>
        public float dim = 0.45f;
        /// <summary>The line of each of the three steps.</summary>
        public string[] texts = { "손을 펴서 들어 보세요", "손을 옮기면 악기가 바뀌어요", "두 주먹을 쥐고 벌리면 끝" };
        /// <summary>The word that appears when the two fists of the third step have spread apart.</summary>
        public string endWord = "끝";
        /// <summary>The picture of the open hand, a PNG with a transparent background, as a path inside the content folder like the backdrops of the meadow. Empty (or a picture that cannot be loaded) means the hand drawn in code.</summary>
        public string openHandImage = "tutorial/hand-open.png";
        /// <summary>The picture of a fist (a right fist: the fist on the left of the two-fist scene is this picture turned round). Empty (or a picture that cannot be loaded) means the fist drawn in code.</summary>
        public string fistImage = "tutorial/hand-fist.png";
    }

    [Serializable]
    public sealed class RecordColorsDto
    {
        public string background;
        public string backgroundBottom;
        public string ink;
        public string muted;
        public string staff;
        public string accent;
        public string card;
    }

    /// <summary>The words on the record screen (Korean; the app carries no font, see <c>record.fontNames</c>).</summary>
    [Serializable]
    public sealed class RecordTextDto
    {
        public string title;
        public string hint;
        /// <summary>Names of the three nature lines: fireflies, sky, hills.</summary>
        public string[] layers;
        /// <summary>{0} minutes, {1} seconds</summary>
        public string durationFormat;
        /// <summary>{0} how many nature instruments joined</summary>
        public string natureFormat;
        /// <summary>{0} month, {1} day</summary>
        public string dateFormat;
        /// <summary>The time of day a card was made, on a twelve-hour clock: {0} the word for before or after noon (<c>meridiem</c>), {1} the hour 1..12, {2} the minute.</summary>
        public string timeFormat = "{0} {1}:{2:00}";
        /// <summary>The two words for before noon and after noon.</summary>
        public string[] meridiem = { "오전", "오후" };
        public string noCards;
    }

    /// <summary>
    /// The note trail (little music notes scattered behind each open hand on the screen) and the names of the cells (a cell shows its name for a moment when a hand comes into it).
    /// Every key has its default here, so a content file from before this block loads with them (tools/lib/content-build.mjs has the same values).
    /// </summary>
    [Serializable]
    public sealed class TrailDto
    {
        /// <summary>The most notes a hand that moves as much as can be leaves a second (a hand that moves less leaves fewer; a hand held still leaves none).</summary>
        public float notesPerSec = 6f;
        /// <summary>The most notes of one hand that are alive at once (the oldest goes first). 0 = no trail.</summary>
        public int maxNotes = 40;
        /// <summary>How long a note lives, fading out, in seconds.</summary>
        public float lifeSec = 1f;
        /// <summary>The height of a note, as a share of the height of the picture.</summary>
        public float size = 0.06f;
        /// <summary>How long the name of a cell shows (then it fades out in a moment) after a hand has come into the cell, in seconds. 0 = no names.</summary>
        public float labelSec = 1.2f;
        /// <summary>On the Low tier the notes a second and the notes alive are this share of the above (0 = none).</summary>
        public float lowCountScale = 0.5f;
    }
}
