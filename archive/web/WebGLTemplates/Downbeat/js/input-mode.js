// ?input=hand|mouse|replay. Same rule as InputModeParser in Core/InputMode.cs: "mouse" and "replay" are explicit,
// anything else (including nothing) asks for the camera.
export const InputMode = Object.freeze({ MOUSE: 'mouse', HAND: 'hand', REPLAY: 'replay' });

export function parseInputMode(value) {
  switch (String(value ?? '').trim().toLowerCase()) {
    case 'mouse': return InputMode.MOUSE;
    case 'replay': return InputMode.REPLAY;
    default: return InputMode.HAND;
  }
}

/** Only the camera mode asks for camera permission. */
export const wantsCamera = (mode) => mode === InputMode.HAND;
