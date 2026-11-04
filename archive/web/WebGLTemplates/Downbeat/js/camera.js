// Camera access with the failure reasons the game cares about: denied, no camera, or something else.
export const CameraProblem = Object.freeze({ DENIED: 'denied', MISSING: 'missing', ERROR: 'error' });

export class CameraError extends Error {
  constructor(problem, message) {
    super(message);
    this.name = 'CameraError';
    this.problem = problem;
  }
}

export function classifyCameraError(error) {
  switch (error?.name) {
    case 'NotAllowedError':
    case 'PermissionDeniedError':
    case 'SecurityError':
      return CameraProblem.DENIED;
    case 'NotFoundError':
    case 'DevicesNotFoundError':
      return CameraProblem.MISSING;
    default:
      return CameraProblem.ERROR;
  }
}

export const DEFAULT_CONSTRAINTS = Object.freeze({
  video: { width: { ideal: 640 }, height: { ideal: 480 }, frameRate: { ideal: 30 } },
  audio: false,
});

/**
 * Opens the camera and starts playing it into a (hidden) video element. Resolves with the stream and its settings.
 * Rejects with a CameraError. The video never leaves the page: it only feeds the hand tracker and the small preview.
 */
export async function openCamera(video, { mediaDevices = globalThis.navigator?.mediaDevices, constraints = DEFAULT_CONSTRAINTS } = {}) {
  if (!mediaDevices?.getUserMedia) {
    throw new CameraError(CameraProblem.ERROR, 'the camera needs a secure page (https or localhost)');
  }
  let stream;
  try {
    stream = await mediaDevices.getUserMedia(constraints);
  } catch (error) {
    throw new CameraError(classifyCameraError(error), String(error?.message ?? error));
  }
  try {
    video.srcObject = stream;
    video.muted = true;
    video.playsInline = true;
    await video.play();
  } catch (error) {
    stream.getTracks().forEach((track) => track.stop());
    throw new CameraError(CameraProblem.ERROR, String(error?.message ?? error));
  }
  return { stream, settings: stream.getVideoTracks()[0]?.getSettings?.() ?? {} };
}
