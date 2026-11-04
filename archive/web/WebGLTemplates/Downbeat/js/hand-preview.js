// A small inset that shows what the tracker sees: the camera picture (mirrored, like a mirror) with the hand skeleton.
// It is drawn from the video element in this page only; nothing is sent anywhere.
const CONNECTIONS = [
  [0, 1], [1, 2], [2, 3], [3, 4], [0, 5], [5, 6], [6, 7], [7, 8], [5, 9], [9, 10], [10, 11], [11, 12],
  [9, 13], [13, 14], [14, 15], [15, 16], [13, 17], [17, 18], [18, 19], [19, 20], [0, 17],
];
const HAND_COLORS = ['#7FD6A8', '#F3D27A'];

/**
 * @param {CanvasRenderingContext2D} ctx
 * @param {CanvasImageSource|null} video
 * @param {{id:number, landmarks:{x:number,y:number}[]}[]} hands landmarks as in the hand buffer (x already mirrored)
 */
export function drawPreview(ctx, video, hands) {
  const { width, height } = ctx.canvas;
  ctx.save();
  ctx.fillStyle = '#000';
  ctx.fillRect(0, 0, width, height);
  if (video) {
    ctx.globalAlpha = 0.85;
    ctx.translate(width, 0);
    ctx.scale(-1, 1);
    ctx.drawImage(video, 0, 0, width, height);
  }
  ctx.restore();

  ctx.save();
  ctx.strokeStyle = 'rgba(255,255,255,0.35)';
  ctx.lineWidth = 1;
  for (const third of [1 / 3, 2 / 3]) {
    ctx.beginPath();
    ctx.moveTo(width * third, 0);
    ctx.lineTo(width * third, height);
    ctx.stroke();
  }
  for (const hand of hands) {
    const color = HAND_COLORS[hand.id % HAND_COLORS.length];
    ctx.strokeStyle = color;
    ctx.fillStyle = color;
    ctx.lineWidth = 2;
    ctx.beginPath();
    for (const [a, b] of CONNECTIONS) {
      ctx.moveTo(hand.landmarks[a].x * width, hand.landmarks[a].y * height);
      ctx.lineTo(hand.landmarks[b].x * width, hand.landmarks[b].y * height);
    }
    ctx.stroke();
    for (const p of hand.landmarks) {
      ctx.beginPath();
      ctx.arc(p.x * width, p.y * height, 2.5, 0, Math.PI * 2);
      ctx.fill();
    }
  }
  ctx.restore();
}
