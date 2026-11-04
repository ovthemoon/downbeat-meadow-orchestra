// Builds content/content.json (schema 4: six parts, two rows of three cells) from the measured sample manifest.
// Tuning numbers (gains, gaps, gestures, the whole meadow block) are DATA: they live in `defaults` and, once content.json exists,
// in content.json itself; a rebuild only refreshes what is measured (the samples). Keys that a newer `defaults` has and an older
// content.json lacks are added; values already in content.json always win.
import { noteToMidi } from './pitch.mjs';

export const DEFAULTS = Object.freeze({
  schemaVersion: 4,
  contentVersion: '2026-11-01.1',
  audio: { masterGainDb: -6, maxVoices: 32, minLeadSec: 0.1, originDelaySec: 0.5 },
  song: {
    id: 'morning-forest', bpm: 96, slotsPerBeat: 4, beatsPerBar: 4, rootMidi: 69, scaleSteps: [0, 3, 5, 7, 10], pitchSteps: 10,
    // one entry per part, in the order of the Part enum (the upper row harp, recorder, bell; the lower row lowstring, marimba, handdrum)
    partMinGapSlots: [
      { part: 'harp', slots: 1 }, { part: 'recorder', slots: 2 }, { part: 'bell', slots: 2 },
      { part: 'lowstring', slots: 2 }, { part: 'marimba', slots: 1 }, { part: 'handdrum', slots: 1 },
    ],
    maxPushSlots: 2,
  },
  // Gesture tuning (palm lengths per second etc.), see HandSettingsDto in ContentModels.cs. Tuned by playing; kept on rebuild.
  hand: {
    minScore: 0.5, triggerSpeed: 3, rearmSpeed: 1, fullSpeed: 9, minVelocity: 0.3, peakWaitMs: 50, peakDropRatio: 0.85,
    velocityWindowMs: 70, minRetriggerMs: 150, maxGapMs: 250, heightHysteresis: 0.015, partHysteresis: 0.03,
    // link to the Python hand tracker (docs/protocol/hand-udp-v1.md)
    udpPort: 47800, trackerTimeoutSec: 2, mirrorX: true,
  },
  // Conducting play (docs/conducting-play.md, see ConductDto in ContentModels.cs): a raised hand keeps the music going; the cell of the picture it is in (cells, below) is the instrument, and the amount it
  // moves sets the loudness (minVelocity when still, full at loudPalms) and the density of the notes (every beat, half beat or quarter beat). Its height does not matter. No strike is needed, so it works at
  // 5 to 10 tracker pictures a second. mode 'strike' is the old play by striking down.
  // phrases: scale steps (0..song.pitchSteps-1) per part, one step per note, from the start again at every bar line. holdMs: a hand missed for less than this goes on playing where it was seen.
  // songPhrases: the same for the two parts that do not carry the melody of a scored song (lowstring, marimba); they are used in place of phrases while a song plays.
  conduct: {
    mode: 'conduct', motionWindowMs: 400, stillPalms: 0.3, halfBeatPalms: 1.5, quarterBeatPalms: 4, minVelocity: 0.25, loudPalms: 4, downbeatAccent: 0.1, releaseSec: 1.5, crossfadeSec: 0.3, holdMs: 800,
    phrases: [
      { part: 'harp', steps: [0, 2, 4, 7, 4, 2] },
      { part: 'recorder', steps: [7, 5, 4, 2] },
      { part: 'bell', steps: [9, 7, 9, 5] },
      { part: 'lowstring', steps: [0, 0, 2, 0] },
      { part: 'marimba', steps: [0, 0, 4, 0] },
      { part: 'handdrum', steps: [0, 0, 0, 0] },
    ],
    songPhrases: [
      { part: 'lowstring', steps: [0, 0, 2, 0] },
      { part: 'marimba', steps: [1, 2, 3, 2] },
    ],
  },
  // The six cells (see CellsDto in ContentModels.cs): the hand's place in the picture is cut in three columns and two rows (rowSplit: where the upper row, the sky, ends; rowHysteresis: how far a hand has to go
  // past it to change row). labels and colors are in the order of the Part enum. The sky row (harp, recorder, bell) plays in the air, the ground row (lowstring, marimba, handdrum) on the grass.
  cells: {
    rowSplit: 0.5, rowHysteresis: 0.03,
    labels: ['하프', '리코더', '종', '낮은 현', '마림바', '손북'],
    colors: ['#F2C46B', '#F4F1E6', '#BFE3FF', '#C9A27A', '#E2453A', '#E8A04A'],
  },
  // Open hand or fist from the 21 hand points (see PoseDto in ContentModels.cs): per finger (not the thumb) the distance wrist to fingertip divided by wrist to middle knuckle. At least openRatio is stretched
  // (the recorded demo hand: 1.85-1.92 index and middle, 1.66 ring, 1.55 little; a relaxed raised hand measures about 1.3-1.5, so 1.35), at most fistRatio is curled; three of four fingers decide, otherwise the pose is unknown. A new pose is taken over after confirmFrames pictures in a row.
  pose: { openRatio: 1.35, fistRatio: 1.15, confirmFrames: 2 },
  // Automatic quality (--quality Auto, the default; see AutoQualityDto in ContentModels.cs): the TIER (High or Low) is chosen at the start from the graphics device and the frames of the first 8 s (a step down every 3 s
  // may happen then); while running it never climbs to a better tier, and goes down to a cheaper one at most once (that waits for an idle meadow). Within the tier the render scale (the second part of a `ladder` step)
  // moves both ways: down when half of the last 4 s went in frames over 1.3 times the budget of the step (60 fps without a cap, the cap otherwise), or when the hand tracker gets under 12 pictures a second for 8 s
  // while the app is busy; up after 90 s (doubling with every try that had to be taken back, up to 30 minutes: a try is taken back when the scale goes down again within upHoldSec of the step up,
  // however much was played at it meanwhile) of a clear window, and only on headroom that was seen while somebody played. A mid PC that starts Low stays Low for the run. start: a graphics device whose name holds one of weakNames (and none of
  // strongNames) or that reports under weakMemoryMb starts at weakStartStep (Low@0.7, where the weak laptop of the memo was measured), any other at strongStartStep (High).
  autoQuality: {
    enabled: true, vsyncBudgetFps: 60, slowFrameFactor: 1.3, overFraction: 0.5, windowSec: 4, downDwellSec: 10, confirmSec: 8, confirmDownDwellSec: 3,
    upDwellSec: 90, maxUpDwellSec: 1800, upHoldSec: 300, headroomShare: 0.5, trackerMinFps: 12, trackerStarveSec: 8, busyGpuShare: 0.5,
    // While a change of tier waits for an idle meadow and the frames are still too slow, the render scale goes down within the tier by pendingScaleStep per dwell, to pendingScaleFloor. A step up needs
    // performingEvidenceSec seconds of frames seen while somebody played (and almost no slow one), and counts as held only after that much playing at it and upHoldSec (5 minutes) at the step. A step down made for a starved tracker has to pay off: the
    // tracker's pictures a second rise by starveGainShare within starveGainWindowSec (20 per cent is about three standard deviations of the count in the 3 s window at 10 a second; 20 s is two down dwells); after
    // starveGiveUpAfter such steps in a row without gain the starvation is ignored for starveIgnoreSec and the frame times alone decide; each time the experiment fails again after a cooldown the next one is twice
    // as long, up to starveIgnoreMaxSec (4 hours). An experiment during which the tracker was away is cancelled (it says nothing); starveCancelsAsFailure cancelled ones in a row count as one that failed (a camera that
    // stalls every few seconds would otherwise be walked down the ladder for ever), and an experiment that ran its course starts that count again.
    pendingScaleStep: 0.1, pendingScaleFloor: 0.5, performingEvidenceSec: 20, starveGainShare: 0.2, starveGainWindowSec: 20, starveGiveUpAfter: 2, starveIgnoreSec: 600, starveIgnoreMaxSec: 14400, starveCancelsAsFailure: 3,
    ladder: [
      { tier: 'High', renderScale: 1 }, { tier: 'High', renderScale: 0.85 }, { tier: 'Low', renderScale: 1 }, { tier: 'Low', renderScale: 0.85 }, { tier: 'Low', renderScale: 0.7 }, { tier: 'Low', renderScale: 0.6 },
    ],
    start: {
      weakNames: ['Intel', 'UHD', 'HD Graphics', 'Iris', 'Vega', 'Microsoft Basic', 'llvmpipe', 'SwiftShader', 'Radeon Graphics'],
      strongNames: ['Arc', 'GeForce', 'RTX', 'GTX', 'Radeon RX', 'Apple M', 'Quadro'],
      weakMemoryMb: 2048, weakStartStep: 4, strongStartStep: 0,
    },
  },
  // Ending the piece with the hands (see CutoffDto in ContentModels.cs): both hands are fists and the sideways distance between them grows by spreadPalms palm lengths within windowSec seconds, ends at least
  // minApartPalms apart and stays so for holdSec. Then the players fade out in fadeSec, "끝" stays endWordSec, and no new visit starts for restartLockSec (then only for an open hand, never for fists held up).
  // A content.json without this block gets the same numbers from the C# field initializers; the ranges are in CutoffContentValidator.cs.
  cutoff: { spreadPalms: 2, minApartPalms: 3.5, windowSec: 1, holdSec: 0.2, fadeSec: 0.25, restartLockSec: 1.5, endWordSec: 1.5 },
  // One instrument per part, in the order of the Part enum. The values of the bell, the low string and the hand drum are in docs/conduct-six-cells-samples.md (A roots, every one of the ten steps within maxPitchShift).
  instruments: [
    { id: 'harp', part: 'harp', gainDb: 0, rootMidi: 69, maxPitchShift: 12, filePrefix: 'harp_' },
    { id: 'recorder', part: 'recorder', gainDb: -4, rootMidi: 81, maxPitchShift: 12, filePrefix: 'recorder_' },
    { id: 'bell', part: 'bell', gainDb: -4, rootMidi: 69, maxPitchShift: 2, filePrefix: 'bell_' },
    { id: 'lowstring', part: 'lowstring', gainDb: 0, rootMidi: 57, maxPitchShift: 2, filePrefix: 'lowstring_' },
    { id: 'marimba', part: 'marimba', gainDb: -1, rootMidi: 69, maxPitchShift: 12, filePrefix: 'marimba_', exclude: ['marimba_C5_loud'] },
    // unpitched drums (A2 tumba, A3 conga, A4 quinto): the pitch of a step is made by the playback rate, up to 5 semitones
    { id: 'handdrum', part: 'handdrum', gainDb: 1, rootMidi: 45, maxPitchShift: 5, filePrefix: 'handdrum_' },
  ],
  // The nature layers (see NatureDto in ContentModels.cs): the energy of a performance and its levels, what each layer plays, and how the picture follows. `instruments` is not here:
  // it comes from the measured nature manifest (natureInstruments below). Nature notes are a written score, so they are scheduled leadBars bars ahead (96 BPM: 2.5 s); hand notes stay at audio.minLeadSec.
  nature: {
    leadBars: 1,
    seed: 17,
    energy: { riseBase: 0.06, risePerVelocity: 0.06, halfLifeSec: 6, level1: 0.2, level2: 0.45, level3: 0.7, hysteresis: 0.05 },
    rules: {
      offbeatSlot: 2, fireflyChance: 0.35, fireflyTopSteps: 5, fireflyGain: 1,
      chords: [{ steps: [0, 1, 3] }, { steps: [1, 3, 4] }], barsPerChord: 2, skyGain: 0.8,
      hillsAccentSlot: 8, hillsAccentGain: 0.6, hillsGain: 1,
    },
    visualSmoothSec: 2.5, hillPulse: 0.35, hillPulseSec: 0.6, hillPulseRow: 0.4, hillPulseHalfHeight: 0.12,
    // hillPulseFollow: how much the haze of the drum follows the painted hills (the plate's own mist), 0 = one flat band.
    hillPulseFollow: 0.85,
    // The sky follows the performance energy from the blue hour at skyEnergyLow to the sunset at skyEnergyHigh (straight between). With the energies of conducting play (a held still hand 0.26..0.39, a slow hand
    // 0.51..0.62, a hand that conducts 0.85..0.98 after 30 s) a strong sustained performance reaches the sunset (sky 1.0) and a held still hand ends at about 0.3: the dusk, never the sunset.
    skyEnergyLow: 0.15, skyEnergyHigh: 0.9,
  },
  // The flow of a visit (see ExperienceDto): a hand wakes the meadow, 3 s without one settles it, and it is idle again once the energy has faded.
  // songMode: a visit plays a scored song from the `songs` list of the content (false = free play as before); songTitleSec: how long the title of the song stays on the meadow.
  experience: { wakingSec: 1, noHandsSec: 3, idleEnergy: 0.05, settlingMinSec: 6, inputPresenceSec: 2, songMode: true, songTitleSec: 3 },
  // The three nature instruments (nature.instruments). Quieter than the hand instruments by 6 dB. Roots are A, so every step of the scale is the song's own note.
  natureInstruments: [
    { id: 'firefly-bell', layer: 'fireflies', gainDb: -6, rootMidi: 69, maxPitchShift: 2, filePrefix: 'firefly_' },
    { id: 'sky-pad', layer: 'sky', gainDb: -6, rootMidi: 57, maxPitchShift: 3, filePrefix: 'pad_' },
    { id: 'hill-drum', layer: 'hills', gainDb: -6, rootMidi: 45, maxPitchShift: 0, filePrefix: 'drum_' },
  ],
  // Screens (see DisplayDto): with two screens the meadow is on meadowIndex and the record on recordIndex (never the same; with three monitors the record can go to the third), with one the record is a strip over the lower 28 % of the meadow (the strip switch of the Tab panel hides it).
  // fullscreenMode: borderless until the Windows measurement says whether exclusive is better.
  display: { mode: 'auto', meadowIndex: 0, recordIndex: 1, fullscreenMode: 'borderless', overlayFraction: 0.28 },
  // The record screen (see RecordDto): the live flower score of the last windowSec seconds and today's newest cards. Colours are #RRGGBB (the blue hour of the meadow), words are Korean.
  record: {
    windowSec: 8, scoreCapacity: 256, cardsShown: 6, minNotes: 3, cardMaxMarks: 64, cardFolder: 'cards', dateCheckSec: 60, thumbnailWidth: 480, thumbnailHeight: 270,
    // While nobody plays the record screen is not still: idleMotes fireflies and petals (every fourth) drift behind the panels, idleMoteSpeed screen heights per second (each one 0.7 to 1.3 times that), never brighter
    // than idleMoteAlpha; while a visit is on they keep idleMoteDimmed of that brightness. 0 motes = none (at most 64). One draw call whatever the number. Same defaults as RecordDto.
    idleMotes: 24, idleMoteSpeed: 0.02, idleMoteAlpha: 0.4, idleMoteDimmed: 0.3,
    // Operating-system fonts to try in order (a Korean one first): the app carries no font file.
    fontNames: ['Apple SD Gothic Neo', 'Malgun Gothic', 'NanumGothic', 'Noto Sans CJK KR'],
    colors: { background: '#0d1233', backgroundBottom: '#1d1a44', ink: '#f6ecd8', muted: '#8d90b8', staff: '#4a4f86', accent: '#ffb15a', card: '#171a48' },
    text: {
      title: '오늘의 들판', hint: '손을 들판 위로 천천히 움직여 보세요', layers: ['반딧불', '하늘', '언덕'],
      durationFormat: '{0}분 {1:00}초', natureFormat: '자연 {0}', dateFormat: '{0}월 {1}일', noCards: '아직 오늘의 카드가 없어요',
      // The time of day on each card (twelve-hour clock): {0} the word for before or after noon (meridiem), {1} the hour 1..12, {2} the minute.
      timeFormat: '{0} {1}:{2:00}', meridiem: ['오전', '오후'],
    },
  },
  // The note trail behind each open hand and the names of the cells (see TrailDto in ContentModels.cs; same defaults as the DTO, a file without the block gets them): a hand that moves as much as can be leaves
  // notesPerSec notes a second (a hand held still none, a fist none), at most maxNotes alive at once, each lifeSec long, size of the height of the picture tall. The name of a cell shows labelSec when a hand comes into it.
  // lowCountScale: the share of notesPerSec and maxNotes the Low tier keeps.
  trail: { notesPerSec: 6, maxNotes: 40, lifeSec: 1, size: 0.06, labelSec: 1.2, lowCountScale: 0.5 },
  // The follow-along tutorial (see TutorialDto in ContentModels.cs and TutorialDirector): once the meadow has been quiet for idleDelaySec (after a visit has ended, and after the app started) it dims the meadow by `dim` and
  // shows three steps of stepSec each (one line of `texts` per step), round and round while nobody is there; an open hand fades it out over fadeOutSec and the music starts at once, a fist does not fade it.
  // endWord is the word that appears when the two fists of the third step have spread apart. openHandImage and fistImage are the pictures of the hands (PNGs with a transparent background, made with an image generator,
  // see content/tutorial/IMAGES.md), as paths inside the content folder like the backdrops; the fist is a right fist and the left fist of the third step is the same picture turned round. An empty path, or a picture that
  // cannot be loaded, means the hand drawn in code. Same defaults as TutorialDto; a content.json from before this block gets them.
  tutorial: {
    idleDelaySec: 10, stepSec: 5, fadeInSec: 0.6, fadeOutSec: 0.8, dim: 0.45,
    texts: ['손을 펴서 들어 보세요', '손을 옮기면 악기가 바뀌어요', '두 주먹을 쥐고 벌리면 끝'],
    endWord: '끝',
    openHandImage: 'tutorial/hand-open.png', fistImage: 'tutorial/hand-fist.png',
  },
  // The meadow (see MeadowDto in ContentModels.cs). Colours are #RRGGBB, distances metres, angles degrees.
  meadow: {
    // The playing field: its width at farZ, the near and far depth a hand reaches, and a screen row that fits the pinhole model of the layout to the camera
    // (the true horizon of the camera is at row 0.28; 0.30 keeps the error of the depth-to-row mapping under 1.5 % of the picture height from nearZ to farZ).
    // farZ 8 is where the 3D grass has thinned out into the plate: the field is the grass, the plate supplies everything above it.
    layout: { fieldWidth: 13.2, nearZ: 2.3, farZ: 8, horizonScreenY: 0.3 },
    // Eye height, look-down angle and lens; with height 1.7 and fov 50 a pitch of -11.6 puts the horizon at row 0.28 and the bottom edge at depth 2.3.
    camera: { height: 1.7, pitchDeg: -11.6, fovDeg: 50 },
    terrain: {
      seed: 7, maxHeight: 1.2, cellSize: 0.5, zMin: -2, zMax: 20, xMargin: 1.5,
      groundDark: '#0b1406', groundMid: '#15230c', groundLight: '#24351a', patchScale: 6, reliefHeight: 0.1, reliefWavelength: 2.6, reliefFromZ: 3.4, reliefToZ: 5.2,
    },
    sky: {
      sunAzimuthDeg: 7.5,
      stops: [
        { id: 'blueHour', backdrop: 'img/meadow/backdrop_bluehour.jpg', lightColor: '#6f86e0', lightIntensity: 0.5, sunElevationDeg: -2, fogColor: '#2a3a70', ambientColor: '#2a3a78', ambientIntensity: 2.6, cloudLit: '#8294de', cloudShade: '#2a3468' },
        { id: 'mid', backdrop: 'img/meadow/backdrop_idle.jpg', lightColor: '#ffb15a', lightIntensity: 1.6, sunElevationDeg: 3, fogColor: '#9a6a80', ambientColor: '#5c4388', ambientIntensity: 3, cloudLit: '#ffb27c', cloudShade: '#6b4a86' },
        { id: 'sunset', backdrop: 'img/meadow/backdrop_sunset.jpg', lightColor: '#ff8a3a', lightIntensity: 2.4, sunElevationDeg: 1.5, fogColor: '#c0705a', ambientColor: '#86467c', ambientIntensity: 3, cloudLit: '#ffa862', cloudShade: '#85467a' },
      ],
      // Two cloud layers drift over the sky of the plates (a near and a far one: the nearer seems to move faster) and a cloud deck throws soft shadows on the meadow; all of it moves at
      // driftMps in the direction of breeze.directionDeg. Clouds are fully there above row fadeFromRow and gone at fadeToRow (the hills). The colours of the clouds are in the stops.
      clouds: {
        driftMps: 2.0, fadeFromRow: 0.1, fadeToRow: 0.205, hueShare: 0.4, litBrightness: 1.35, shadeBrightness: 0.62,
        near: { altitude: 22, cellMetres: 34, coverage: 0.5, softness: 0.2, opacity: 0.85 },
        far: { altitude: 48, cellMetres: 64, coverage: 0.42, softness: 0.24, opacity: 0.6 },
        shadow: { cellMetres: 15, coverage: 0.4, softness: 0.18, strength: 0.28 },
      },
    },
    // From fromRow (a picture row, 0 top, 1 bottom) downwards the ground is 3D; towards toRow it fades into the plate behind it, and at toRow and above it is the plate itself (sharp: the painted hummocks and mist show). plateFrom is how early
    // the haze colour is the plate's own, so the stop's fog colour only tints the first part of the band.
    haze: { fromRow: 0.61, toRow: 0.52, plateFrom: 0.1, sharpFrom: 0, blurLod: 1.2, curve: 1 },
    // The wind. Bend values are fractions of a blade's height times bendScale. A blade (and a flower stem) bends along a circular arc as long as it is; maxBendDeg is the most it bends in total, in degrees: near a fast hand
    // the blades lie down towards it instead of stretching, a harder hand further (Core/Meadow/BladeBend.cs, MeadowBladeCurve in MeadowWind.hlsl). 120 keeps the resting meadow as it was (the tip leans within 5 degrees of the old formula
    // up to a bend of 0.6; true for 100 or more) and leaves a blade that leans a lot on its own room to sway; at most 150 (the arc must stay under 180).
    // A hand is a soft local gust (MeadowWind.hlsl): within handRadius metres the grass is parted (handPush, nothing at the hand itself, feathered to nothing at the radius) and leans the way the hand moves
    // (handLean per metre a second, no faster than handSpeedCap) with the swing back behind it that the breeze's gusts have; handGustWidth is the width of that gust's body in metres.
    wind: {
      waveWidth: 2.6, handRadius: 3.4, bendScale: 2.2, maxBendDeg: 120, handPush: 0.6, handLean: 0.35, handLookAheadSec: 0.05, handGustWidth: 0.9, handSpeedCap: 2.5,
      beatSweep: { amplitude: 0.07, energyGain: 0.32, downbeatGain: 1.4, speed: 7, lifeSec: 3.2 },
      noteRadial: { amplitude: 0.3, velocityGain: 0.45, speed: 4.5, lifeSec: 2.6 },
      // The two grass reactions of the bottom row (WindLongWaveDto, WindDrumRingDto in EffectsContentModels.cs; the data classes have the same defaults). longWave, the low string: a sweep from the hand towards the
      // side of the field the hand is on, widthScale times as wide as waveWidth, soft. drumRing, the hand drum: a ring like a note's but strong, wide and quick. lifeSec is short on purpose: at the quickest
      // (a quarter beat at 96 BPM = 0.156 s; the low string every second slot) the two together hold 11 of the 16 waves of the field (WindFieldGpu.Capacity), see WindReactionTests.
      longWave: { amplitude: 0.22, velocityGain: 0.5, speed: 5, lifeSec: 1.3, widthScale: 2.4 },
      drumRing: { amplitude: 0.5, velocityGain: 0.5, speed: 7, lifeSec: 0.9, widthScale: 1.5 },
    },
    // The steady wind (bend values are fractions of a blade's height before wind.bendScale). One prevailing direction (degrees on the ground: 0 = to the right, 90 = away from the viewer);
    // the grass leans that way a little and sways around the lean. Broad gusts (width metres wide, one every spacing / speed seconds on average) roll across the field and on over the painted
    // far meadow; the foot of a blade leads and its tip follows lagMetres later, and the grass swings back past upright by springBack behind a gust. Blades differ only in stiffness.
    breeze: {
      directionDeg: 32, lean: 0.07, meanPeriodSec: 31, meanVariation: 0.18, stiffnessMin: 0.8, stiffnessMax: 1.2,
      gust: { spacing: 11, speed: 2.4, width: 2.6, amplitude: 0.16, weakest: 0.55, jitter: 0.22, frontWobble: 1.5, wobbleLength: 21, lagMetres: 1.1, springBack: 0.22, brightness: 0.18 },
      sway: { wavelength: 6.5, periodSec: 3.7, amplitude: 0.035 },
      flutter: { amplitude: 0.02, periodSec: 2.3 },
      plate: { rippleGain: 0.14, fullFromRow: 0.46, noneAtRow: 0.33 },
    },
    // The grass: depth bands (a near band of big dark blades, a body, a thinning-out band), blade size, clumps, hummocks, hue mix and light. Blades per square
    // metre come from the quality tier. The grass is full at picture row fadeFromRow and below and gone at fadeToRow (0.57 to 0.47): the plate shows above that.
    grass: {
      rings: [
        { zFrom: 1, zTo: 3.6, densityScale: 0.3, widthScale: 1.15, segments: 6, darkness: 1, heightScale: 1.1 },
        { zFrom: 3.6, zTo: 6.6, densityScale: 0.5, widthScale: 1.1, segments: 6, darkness: 0.72, heightScale: 1 },
        { zFrom: 6.6, zTo: 12.5, densityScale: 0.6, widthScale: 1.2, segments: 5, darkness: 1, heightScale: 1 },
      ],
      heightMin: 0.28, heightMax: 0.62, width: 0.042, lean: 0.5,
      clumpScale: 0.4, clumpLean: 0.75, hummockScale: 5.5, hummockHeight: 0.45, hummockDensity: 0.35, hummockDark: 0.6,
      fadeFromRow: 0.57, fadeToRow: 0.47, fadeMinHeight: 0.06,
      rootColor: '#050c04', midColor: '#176014', tipColor: '#628a22', yellowColor: '#5a8a1c', driedColor: '#7a6a3a', driedShare: 0.015,
      backlightColor: '#fff0d8', backlight: 0.1, tipGlow: 5, sunColumn: 6, nearDarkness: 0.3, nearDarkStart: 2.3, nearDarkEnd: 3.0, farDim: 0.5, farDimFrom: 3.6, farDimTo: 6.2, sunBody: 1.0, ambientMix: 1.4, relief: 8, rim: 2.4, sparkle: 0.1, glowColor: '#7fd6ff', tipRim: 0.5,
    },
    // The flowers: three species as three instruments (harp = bellflower, recorder = dandelion, marimba = poppy). A note opens 1 + round(velocity x maxPerNote) of them.
    // The quiet state is small white and violet buds that shine a little by themselves; a bloom starts leadSec before the note is heard (a pop: half open about when the sound arrives).
    flowers: {
      seed: 11, maxPerNote: 6, blend: 0.1, harpGlow: 0.8, clearRadius: 0.05, stemColor: '#3a7a20', budGlow: 0.22, openGlow: 0.12,
      ambientMix: 1.1, sunBody: 0.6, translucency: 1.2,
      bloom: { leadSec: 0.075, openSec: 0.35, holdSec: 3, closeSec: 4, restLevel: 0.15 },
      bellflower: { budColor: '#9272e0', openColor: '#6f86ff', centerColor: '#f2eeff', stemHeight: 0.46, headSize: 0.1 },
      dandelion: { budColor: '#e6efc4', openColor: '#fff4dc', centerColor: '#ffe8a0', stemHeight: 0.5, headSize: 0.1 },
      poppy: { budColor: '#e8a8cc', openColor: '#ff4a30', centerColor: '#2a0f1c', stemHeight: 0.48, headSize: 0.11 },
    },
    // The small things in the air. Seeds and petals are thrown by notes and follow the wind of the notes and the beat; fireflies come with the nature layers (their level 0..1 scales
    // fireflyMax); a little pollen is always there. Colours are #RRGGBB; glow is the HDR gain of the material (a vertex colour cannot exceed 1): above 1 the middle of a firefly is brighter than the screen shows and
    // the bloom (clamped, see UrpSetup) gives it a small halo.
    particles: {
      seedColor: '#fff6e0', petalColor: '#ff5a3a', fireflyColor: '#ffbd4f', pollenColor: '#ffd88a',
      seedLifeSec: 5, petalLifeSec: 4.5, fireflyLifeSec: 9,
      seedSize: 0.032, petalSize: 0.05, fireflySize: 0.07, pollenSize: 0.016,
      windFollow: 2.6, windDrift: 0.45, riseSpeed: 0.55, sinkSpeed: 0.12, pollenCount: 140, pollenDrift: 0.12,
      fireflyHeightMin: 0.25, fireflyHeightMax: 1.1, fireflyGlow: 9, seedGlow: 0.9,
      // limits and shapes of the bursts and the fireflies (a seed or petal starts within spawnSpread m of the spot, spawnHeight m above the ground there)
      maxSeeds: 160, maxPetals: 160, spawnSpread: 0.35, spawnHeightMin: 0.25, spawnHeightMax: 0.7,
      fireflyFadeFraction: 0.15, fireflyBlinkCount: 6, fireflyBlinkLow: 0.22, fireflyNoiseStrength: 0.2, fireflyNoiseFrequency: 0.35, fireflyFloor: 0.25, fireflyEdgeMargin: 0.3,
      // A bell of the nature score is answered: the nearest fireflyAnswerCount fireflies within fireflyAnswerRadius m of where it is heard glow (fireflyAnswerGain times brighter at the peak) for a moment.
      // particleMinPixels: a seed or a petal is drawn at least this many pixels across (a size floor for far ones: a seed 3 cm across is under 2 pixels at 12 m).
      particleMinPixels: 3.5,
      fireflyAnswerCount: 2, fireflyAnswerRadius: 3.5, fireflyAnswerAttackSec: 0.05, fireflyAnswerDecaySec: 0.8, fireflyAnswerGain: 1.5,
    },
    // The effects of the top row in the air where the hand is (SkyEffectsDto in EffectsContentModels.cs; the data class has the same defaults): a ring of ringCount points of light that spreads to ringRadius metres (harp),
    // seedCount seeds that rise (recorder), starCount specks of star dust that fall and twinkle (bell). lifeSec is the life of a seed (a ring lives 0.4 of it, star dust 0.75), distance how far in front of the camera
    // they are made (m), glow the HDR gain of the light. The Low tier makes lowCountScale of the particles (a weak chip is held back by the overdraw of added light); maxParticles is the buffer of each system.
    skyEffects: { ringCount: 28, seedCount: 10, starCount: 14, lifeSec: 3.6, distance: 7, ringRadius: 1.1, glow: 1, maxParticles: 400, lowCountScale: 0.5 },
    // The ribbon of light that follows a hand: a trail of 0.6 s, 0.4 m above the ground, fading over 0.5 s when the hand is gone.
    // follow*: the One Euro filter that lets it glide between tracker frames (HandRibbonFollow). Simulated on the replay path with 0.003 jitter
    // and ~19 tracker fps: a still hand's ribbon moves two thirds less per drawn frame than the raw place, for ~2 cm more lag in a strike.
    ribbon: { color: '#ffc86a', tailColor: '#7fa8ff', trailSec: 0.6, width: 0.1, height: 0.4, appearSec: 0.08, fadeSec: 0.5, glow: 1.1, minVertexDistance: 0.03, headAlpha: 0.7, midAlpha: 0.4, midWidth: 0.7, followMinHz: 0.6, followBeta: 6, followSpeedHz: 1.5 },
    // What --demo-waves does (captures only): a sweep and two rings startSec after the meadow is built.
    demo: { startSec: 1, radialDepth: 6, radialVelocity: 0.8 },
    // The waking moment (see MeadowWakeDto): when a hand comes into an idle meadow, up to maxFlowers buds within radius metres of it glint in a wave that spreads at speed metres a second
    // (each up to jitterSec late: not a perfect ring), and one soft gust of wind leaves the same spot. radius / speed is how long the wave lasts: 1.2 s.
    wake: { radius: 3.5, speed: 3, maxFlowers: 20, jitterSec: 0.15, gustAmplitude: 0.2, gustLifeSec: 2.4, gustGlow: 0.35 },
    // One entry per Unity quality level (same names). The first is the default; --quality NAME picks another.
    // renderScale: the share of the window's pixels per side that is drawn (the rest is stretched: upscaler); targetFps: 0 keeps the display's vertical sync; msaa 0 keeps the pipeline asset's own
    // count (High 4, Low 1); bloom: the cost of the glow (filter Gaussian|Dual|Kawase, downscale Half|Quarter, iterations); skyDetail 1 works the cloud noise out per pixel, 0 reads it from a baked tile (one read a layer): the clouds, their shadows and the ripple of the far meadow move at both tiers.
    quality: [
      { name: 'High', grassPerSqm: 5300, grassMaxSegments: 7, flowerCount: 480, fireflyMax: 300, renderScale: 1, upscaler: 'Linear', msaa: 0, targetFps: 0, bloom: { enabled: true, filter: 'Gaussian', downscale: 'Half', maxIterations: 6 }, skyDetail: 1 },
      { name: 'Low', grassPerSqm: 1400, grassMaxSegments: 4, flowerCount: 190, fireflyMax: 100, renderScale: 0.7, upscaler: 'Linear', msaa: 0, targetFps: 30, bloom: { enabled: true, filter: 'Dual', downscale: 'Quarter', maxIterations: 4 }, skyDetail: 0 },
    ],
  },
});

const round = (v, digits) => Number(v.toFixed(digits));

function samplesFor(instrument, manifest) {
  const excluded = new Set(instrument.exclude ?? []);
  return manifest.files
    .filter((f) => f.file.startsWith(instrument.filePrefix) && !excluded.has(f.file.replace(/\.wav$/, '')))
    .map((f) => ({
      id: f.file.replace(/\.wav$/, ''),
      file: `audio/${f.file}`,
      soundingMidi: noteToMidi(f.sourceNote),
      tuneCents: round(f.centsFromLabel, 1),
      perceivedOnsetOffsetMs: round(f.trimmed.perceivedOnsetOffsetMs, 1),
    }))
    .sort((a, b) => a.soundingMidi - b.soundingMidi);
}

const isObject = (v) => v !== null && typeof v === 'object' && !Array.isArray(v);

/** `defaults` with every value of `existing` on top; objects are merged key by key, arrays and numbers of `existing` replace the default whole. */
export function mergeKeepingExisting(defaults, existing) {
  if (!isObject(defaults) || !isObject(existing)) return existing === undefined ? structuredClone(defaults) : structuredClone(existing);
  const merged = {};
  for (const key of new Set([...Object.keys(defaults), ...Object.keys(existing)])) {
    merged[key] = key in existing ? mergeKeepingExisting(defaults[key], existing[key]) : structuredClone(defaults[key]);
  }
  return merged;
}

/** Keys that older content files still carry and the app no longer reads: mergeKeepingExisting would keep them, so they are dropped here. */
const RETIRED_MEADOW_KEYS = ['overlay']; // the notice in the lower left corner of the meadow; its font list moved to record.fontNames

const withoutRetiredMeadowKeys = (meadow) => Object.fromEntries(Object.entries(meadow).filter(([key]) => !RETIRED_MEADOW_KEYS.includes(key)));

/** The names the three parts of schema 3 had, and the part of the six that took their place. */
const LEGACY_PART_NAMES = Object.freeze({ strings: 'harp', winds: 'recorder', perc: 'marimba' });

/**
 * A list with one entry per part (`song.partMinGapSlots`, `conduct.phrases`): what a content file of an older schema has is kept for the parts it knows (under their new names), and the parts it lacks
 * get the entry of `defaults`. The result is in the order of `defaults`.
 */
function partListKeeping(kept, defaults) {
  const byPart = new Map();
  for (const entry of Array.isArray(kept) ? kept : []) byPart.set(LEGACY_PART_NAMES[entry.part] ?? entry.part, { ...entry, part: LEGACY_PART_NAMES[entry.part] ?? entry.part });
  return defaults.map((def) => structuredClone(byPart.get(def.part) ?? def));
}

/** The quality tiers are an array, which mergeKeepingExisting keeps whole; a tier is merged key by key instead, so that a tier written before a new key gets that key. */
function withTiersMergedByName(meadow, keptTiers) {
  if (!Array.isArray(keptTiers)) return meadow;
  const tiers = DEFAULTS.meadow.quality.map((def) => {
    const kept = keptTiers.find((t) => t.name === def.name);
    return kept ? mergeKeepingExisting(def, kept) : structuredClone(def);
  });
  for (const kept of keptTiers) if (!tiers.some((t) => t.name === kept.name)) tiers.push(structuredClone(kept));
  return { ...meadow, quality: tiers };
}

const withoutInstruments = (nature) => {
  const { instruments, ...rest } = nature ?? {};
  return rest;
};

/** The nature instruments with their (measured or by-construction) samples; hand-edited gain, root and shift of an existing content.json are kept. */
function natureInstrumentsFor(natureManifest, keep) {
  const old = keep.nature?.instruments ?? [];
  return DEFAULTS.natureInstruments.map((def) => {
    const kept = old.find((i) => i.id === def.id);
    const { filePrefix, ...settings } = { ...def, ...(kept ? { gainDb: kept.gainDb, rootMidi: kept.rootMidi, maxPitchShift: kept.maxPitchShift } : {}) };
    const samples = samplesFor(def, natureManifest);
    if (samples.length === 0) throw new Error(`no samples found for nature instrument ${def.id}`);
    return { ...settings, samples };
  });
}

/**
 * @param {{manifest:object, natureManifest:object, existing?:object}} input
 * Existing content keeps its hand-edited values; measured values (the samples) are always refreshed.
 */
export function buildContent({ manifest, natureManifest, existing = null }) {
  const keep = existing ?? {};
  const instruments = DEFAULTS.instruments.map((def) => {
    const old = (keep.instruments ?? []).find((i) => i.id === def.id);
    const { filePrefix, exclude, ...settings } = { ...def, ...(old ? { gainDb: old.gainDb, rootMidi: old.rootMidi, maxPitchShift: old.maxPitchShift } : {}) };
    const samples = samplesFor(def, manifest);
    if (samples.length === 0) throw new Error(`no samples found for instrument ${def.id}`);
    return { ...settings, samples };
  });

  return {
    schemaVersion: DEFAULTS.schemaVersion,
    contentVersion: keep.schemaVersion === DEFAULTS.schemaVersion ? (keep.contentVersion ?? DEFAULTS.contentVersion) : DEFAULTS.contentVersion,
    audio: keep.audio ?? { ...DEFAULTS.audio },
    instruments,
    song: { ...structuredClone(DEFAULTS.song), ...(keep.song ?? {}), partMinGapSlots: partListKeeping(keep.song?.partMinGapSlots, DEFAULTS.song.partMinGapSlots) },
    hand: { ...DEFAULTS.hand, ...(keep.hand ?? {}) },
    conduct: { ...mergeKeepingExisting(DEFAULTS.conduct, keep.conduct), phrases: partListKeeping(keep.conduct?.phrases, DEFAULTS.conduct.phrases) },
    cells: mergeKeepingExisting(DEFAULTS.cells, keep.cells),
    pose: mergeKeepingExisting(DEFAULTS.pose, keep.pose),
    autoQuality: mergeKeepingExisting(DEFAULTS.autoQuality, keep.autoQuality),
    cutoff: mergeKeepingExisting(DEFAULTS.cutoff, keep.cutoff),
    nature: { ...mergeKeepingExisting(DEFAULTS.nature, withoutInstruments(keep.nature)), instruments: natureInstrumentsFor(natureManifest, keep) },
    experience: mergeKeepingExisting(DEFAULTS.experience, keep.experience),
    display: mergeKeepingExisting(DEFAULTS.display, keep.display),
    record: mergeKeepingExisting(DEFAULTS.record, keep.record),
    trail: mergeKeepingExisting(DEFAULTS.trail, keep.trail),
    tutorial: mergeKeepingExisting(DEFAULTS.tutorial, keep.tutorial),
    meadow: withoutRetiredMeadowKeys(withTiersMergedByName(mergeKeepingExisting(DEFAULTS.meadow, keep.meadow), keep.meadow?.quality)),
    // The scored songs are written by tools/import_songs.mjs, not measured: a rebuild keeps them as they are.
    songs: Array.isArray(keep.songs) ? structuredClone(keep.songs) : [],
  };
}
