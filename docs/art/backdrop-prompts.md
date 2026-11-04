# Meadow reference image prompts

These are the prompts (Codex CLI, OpenAI image generation, 2026-10-29) of the pictures behind the meadow scene. Only the three plates below are used by
the Unity app; they are stored as JPEG (quality 85, 1672 x 941, not resized) next to the content:

| Plate (PNG name in the prompts below) | In the repository | Used for |
|---|---|---|
| `meadow_front_b_bluehour.png` | `content/img/meadow/backdrop_bluehour.jpg` | idle (energy 0) |
| `meadow_front_b.png` | `content/img/meadow/backdrop_idle.jpg` | playing |
| `meadow_front_b_sunset.png` | `content/img/meadow/backdrop_sunset.jpg` | climax |

`meadow_idle.png`, `meadow_wind.png`, `meadow_bloom.png`, `meadow_detail.png` and `meadow_front_a.png` were only look targets (mood, light, colour) and are not in the repository.

## meadow_idle.png

Output resolution: 1672 × 941 pixels (PNG).

Reference image used: No.

Exact full prompt:

```text
Create a visual target for a webcam-driven interactive exhibition built in Unity, achievable as a stylized real-time 3D scene with game-engine geometry, instanced grass, practical lighting, and restrained particle effects. Render one wide establishing shot of a calm idle meadow at dusk. The grass sways gently, with a few closed flower buds scattered throughout. The sky is deep blue-violet with the first faint stars. The sun has just set, leaving a soft amber glow on the horizon. Camera at knee height looking across the meadow toward distant rolling hills. Quiet, inviting, slightly mysterious. Establish a clear, repeatable landscape layout: a softly dipping horizon, layered rolling hills, and a broad open center of the meadow framed by taller grass in the foreground. Preserve readable individual grass blades and a coherent three-dimensional world; do not make it a photograph or a 2D painting. Generate a single PNG at the widest supported landscape size, preferably 1536 × 864 or a wider native 16:9 resolution.

Stylized real-time 3D environment render with the polish of a high-end indie game: dense meadow of tall soft grass blades, golden-hour light from a low sun behind distant rolling hills, warm rim light glowing on grass tips, gentle atmospheric haze and depth layers, soft light shafts, painterly color grading in violet-to-amber dusk tones. Wide 16:9 landscape composition. No people, no characters, no hands, no text, no logos, no watermark.
```

## meadow_wind.png

Output resolution: 1672 × 941 pixels (PNG).

Reference image used: Yes, meadow_idle.png.

Exact full prompt:

```text
Use the attached idle meadow image as the exact environment and composition reference. Create the music-playing wind state of this same webcam-driven interactive Unity exhibition. Keep exactly the same knee-height camera position, field of view, horizon, hill silhouettes, rocks, tree placement, cloud layout, and meadow geography. A visible wave of wind sweeps outward from the center of the field: dense grass bends in a broad, smooth rolling ring, clearly readable through directional grass deformation and traveling highlights. A faint translucent glowing ribbon of wind marks where the wave started, low above the grass, delicate and restrained like a real-time particle ribbon. A few petals lift into the air, and some of the previously closed flowers begin to open. The sky is slightly warmer than the reference, while retaining its blue-violet upper sky and the same cloud shapes. Convey a single musical beat traveling through the meadow. Keep this achievable with instanced 3D grass, vertex animation, flower meshes, soft emissive particles, and game-engine lighting. Preserve readable individual grass blades and a coherent three-dimensional world; do not make it a photograph or a 2D painting. Generate one PNG at the widest supported landscape size, matching the reference aspect ratio.

Stylized real-time 3D environment render with the polish of a high-end indie game: dense meadow of tall soft grass blades, golden-hour light from a low sun behind distant rolling hills, warm rim light glowing on grass tips, gentle atmospheric haze and depth layers, soft light shafts, painterly color grading in violet-to-amber dusk tones. Wide 16:9 landscape composition. No people, no characters, no hands, no text, no logos, no watermark.
```

## meadow_bloom.png

Output resolution: 1671 × 941 pixels (PNG).

Reference image used: Yes, meadow_idle.png.

Exact full prompt:

```text
Use the attached idle meadow image as the exact environment and composition reference. Create the musical climax state of this same webcam-driven interactive Unity exhibition. Keep exactly the same knee-height camera position, field of view, horizon, hill silhouettes, rocks, tree placement, cloud layout, and meadow geography. The field is now full of blooming wildflowers in warm pink, gold, and coral, mixed naturally among the tall grass, with especially readable flower meshes in the foreground. Petals swirl in several graceful spiraling gusts above the field. Small glowing pollen motes and fireflies float at varied depths. The sky becomes a vivid sunset of violet, rose, and amber, while retaining the reference cloud shapes and the same horizon. Strong backlit glow traces the grass tips and translucent petal edges. Joyful and magical, with a clear progression from the quiet reference, while unmistakably remaining the same place. Keep the vegetation, particles, lighting, and wind effects achievable in real time with Unity: instanced mesh flowers and grass, vertex-animated wind, modest particle sprites, soft volumetric shafts, and controlled bloom. Preserve readable individual grass blades and a coherent three-dimensional world; do not make it a photograph or a 2D painting. Generate one PNG at the widest supported landscape size, matching the reference aspect ratio.

Stylized real-time 3D environment render with the polish of a high-end indie game: dense meadow of tall soft grass blades, golden-hour light from a low sun behind distant rolling hills, warm rim light glowing on grass tips, gentle atmospheric haze and depth layers, soft light shafts, painterly color grading in violet-to-amber dusk tones. Wide 16:9 landscape composition. No people, no characters, no hands, no text, no logos, no watermark.
```

## meadow_detail.png

Output resolution: 1672 × 941 pixels (PNG).

Reference image used: Yes, meadow_idle.png.

Exact full prompt:

```text
Use the attached idle meadow image as the environment, vegetation design, and color reference. Create a close-up at ground level inside this same meadow, as a material and color target for the real-time grass and flowers in a webcam-driven interactive Unity exhibition. Move the camera down among the blades, focusing on a small cluster of three-dimensional wildflowers in warm pink, gold, and coral, surrounded by individual tall soft grass blades. Keep the same species shapes, terrain mood, violet-to-amber dusk palette, and low sun direction from the reference. Strong warm backlight reveals the thin translucent petal edges and soft glowing grass tips, with simple readable mesh silhouettes, subtle surface roughness, and convincing game-engine subsurface scattering. A few petals are caught in motion across the frame, and soft glowing pollen particles float between the stems. Use shallow depth of field: the central grass and flowers are sharply readable, with nearby blades and distant rolling hills softly out of focus. The distant hills and violet-amber sky still connect this detail to the same place. This must look achievable as a polished stylized real-time 3D render, with practical mesh vegetation and restrained particle effects; do not make it a photograph or a 2D painting. Generate one PNG at the widest supported landscape size, matching the reference aspect ratio.

Stylized real-time 3D environment render with the polish of a high-end indie game: dense meadow of tall soft grass blades, golden-hour light from a low sun behind distant rolling hills, warm rim light glowing on grass tips, gentle atmospheric haze and depth layers, soft light shafts, painterly color grading in violet-to-amber dusk tones. Wide 16:9 landscape composition. No people, no characters, no hands, no text, no logos, no watermark.
```


## meadow_front_a.png

Tool: built-in image generation

Output resolution: 1672 x 941 pixels, PNG

Reference image used: meadow_idle.png

Exact full prompt:

```text
Use case: stylized-concept.
Asset type: calm idle meadow environment reference, meadow_front_a.png.
Reference image: meadow_idle.png is the style, palette, lighting and world reference. Recreate the same dusk meadow, same grass, rolling hills, trees, rocks, clouds, closed flower buds and color grading seen in the reference. Preserve the reference's violet sky, amber sunset glow, golden grass tips, soft haze and polished real-time 3D rendering. Only the camera and resulting framing change; this is another view of the same world.

Stylized real-time 3D environment render with the polish of a high-end indie game: dense meadow of tall soft grass blades, golden-hour light from a low sun behind distant rolling hills, warm rim light glowing on grass tips, gentle atmospheric haze and depth layers, soft light shafts, painterly color grading in violet-to-amber dusk tones. Wide 16:9 landscape composition. No people, no characters, no hands, no text, no logos, no watermark.

Camera/composition: Camera at eye height, about 1.6 m, looking perfectly straight ahead from the front. Keep the camera level with zero roll and no downward or upward tilt. Place the level horizon exactly at the vertical middle of the frame (50% from the top). Use a strong symmetric one-point perspective. The setting sun sits exactly in the center of the frame on the horizon. A gentle open lane made of shorter grass runs straight from the viewer at bottom center toward the centered sun, narrowing naturally to the single central vanishing point. It is a meadow lane of grass, not a dirt road. The meadow spreads evenly to the left and right. Balance the grass, hills, scattered trees and rocks around the central axis while retaining the natural organic appearance of the reference. Closed flower buds are scattered through the field. Calm idle state, balanced, calm, frontal.

Output: one PNG image, widest landscape size the tool supports, using the maximum available native output resolution while preserving the requested wide 16:9 landscape composition.
```


## meadow_front_b.png

Tool: built-in image generation

Output resolution: 1672 x 941 pixels, PNG

Reference image used: meadow_idle.png

Exact full prompt:

```text
Use case: stylized-concept.
Asset type: calm idle meadow environment reference, meadow_front_b.png.
Reference image: meadow_idle.png is the style, palette, lighting and world reference. Recreate the same dusk meadow, same grass, rolling hills, trees, rocks, clouds, closed flower buds and color grading seen in the reference. Preserve the reference's violet sky, amber sunset glow, golden grass tips, soft haze and polished real-time 3D rendering. Only the camera and resulting framing change; this is another view of the same world.

Stylized real-time 3D environment render with the polish of a high-end indie game: dense meadow of tall soft grass blades, golden-hour light from a low sun behind distant rolling hills, warm rim light glowing on grass tips, gentle atmospheric haze and depth layers, soft light shafts, painterly color grading in violet-to-amber dusk tones. Wide 16:9 landscape composition. No people, no characters, no hands, no text, no logos, no watermark.

Camera/composition: Camera at standing height, facing straight ahead from the front and angled slightly down over a vast, gently rolling meadow that faces the viewer like a stage. Keep zero roll and a horizontal horizon. Place the horizon high, about one fifth from the top (20% down the frame). The grass field fills most of the image, approximately the lower 80%, continuously from the left edge to the right edge, with evenly distributed detail. Far hills and sunset glow form a thin distant band at the top. The overall composition is frontal and symmetric around the vertical centerline. The left, center and right thirds of the meadow must each be clearly visible and equally readable, with soft grass and closed flower buds scattered evenly across all three thirds. No strong foreground blades, large foreground rocks or near-camera objects blocking the view. Keep the foreground grass low enough in the frame to reveal the broad continuous field beyond it, while preserving the same grass species and visual texture as the reference. Calm idle state.

Output: one PNG image, widest landscape size the tool supports, using the maximum available native output resolution while preserving the requested wide 16:9 landscape composition.
```


## meadow_front_b_sunset.png

- Tool: built-in image_gen
- Output resolution: 1672 x 941 pixels
- Output format: PNG
- Reference image: meadow_front_b.png

Exact full prompt:

```text
Use case: lighting-weather
Asset type: far background plate for a real-time 3D scene, cross-faded with the reference.
Input image: meadow_front_b.png is the exact composition reference and edit target.

Create exactly one new image named meadow_front_b_sunset.png. Edit the reference image with the same frontal view at the musical climax. Output PNG at exactly the same pixel resolution as the reference, 1672 x 941 pixels.

Composition lock: keep the camera, lens, field of view, framing, perspective, horizon height, every hill silhouette, all depth layers, every tree position and shape, every rock position and shape, and the entire meadow layout exactly the same. Preserve the cloud layout and cloud shapes in their exact positions. Preserve the exact positions of grass clumps and blades. This must register spatially with meadow_front_b.png for a cross-fade: no pan, tilt, zoom, crop, reframing, warping, rearrangement, new geography, or added objects. Change only the time of day, lighting, atmospheric glow and color grading, plus remove any open flower blossoms so no flowers are in bloom.

Style lock, word for word:
"Stylized real-time 3D environment render with the polish of a high-end indie game: dense meadow of tall soft grass blades, golden-hour light from a low sun behind distant rolling hills, warm rim light glowing on grass tips, gentle atmospheric haze and depth layers, soft light shafts, painterly color grading in violet-to-amber dusk tones. Wide 16:9 landscape composition. No people, no characters, no hands, no text, no logos, no watermark."

Sunset lighting: the sky becomes a vivid sunset of violet, rose and amber with the same cloud layout. The sun glows larger and warmer with its center fixed at the exact same spot on the horizon. Soft light shafts fan across the hills, the haze in the valleys glows warm, and the backlit grass tips shine brighter. Make this an expressive musical-climax lighting state of the exact same environment.

No flowers in bloom. Existing closed buds may remain closed in their original locations. No petals, no floating particles, no pollen, no sparks, no added decorative effects; those are added in 3D later. Same camera, same geography.
```


## meadow_front_b_bluehour.png

Output resolution: 1672 x 941 pixels, PNG
Reference image: meadow_front_b.png
Generation tool: built-in image generation

Exact full prompt:

```text
Use case: lighting-weather.
Asset type: far background plate for the quiet idle state of a real-time 3D scene.
Output: exactly one PNG image, 1672 x 941 pixels, the same resolution and aspect ratio as the reference. Intended filename: meadow_front_b_bluehour.png.

Input image: meadow_front_b.png is the edit target and the exact composition reference. Create a blue-hour relighting of this image. It will be cross-faded with meadow_front_b.png and meadow_front_b_sunset.png, so preserve pixel-aligned composition: exactly the same camera, framing, field of view, horizon height, hill silhouettes, tree positions and shapes, rocks, valley contours, meadow layout and grass-clump positions. Do not move, reshape, add or remove geographical features. No crop, zoom, pan, perspective change or compositional redesign. Only time of day and lighting change.

Keep this style lock word for word:
"Stylized real-time 3D environment render with the polish of a high-end indie game: dense meadow of tall soft grass blades, golden-hour light from a low sun behind distant rolling hills, warm rim light glowing on grass tips, gentle atmospheric haze and depth layers, soft light shafts, painterly color grading in violet-to-amber dusk tones. Wide 16:9 landscape composition. No people, no characters, no hands, no text, no logos, no watermark."

Time-of-day and lighting specification for this blue-hour variant: the same frontal view about twenty minutes after sunset, the calm idle state before any music. The sun is fully below the horizon, with no visible solar disk; only a thin, faint amber line remains along the far hills where the sun set. The upper sky is deep blue to indigo with clear first stars. Preserve the existing cloud formations and positions, but render them dim blue-violet with no bright orange. The valley haze is cool blue. The meadow is darker and cooler, with only a subtle soft sheen on the grass tips. Quiet, still, slightly mysterious, clearly darker than the reference.

The blue-hour specification controls the actual time of day and illumination: retain the style lock's stylized real-time 3D rendering quality, dense soft grass, atmospheric depth and painterly grading while replacing the reference's golden sunlight, warm grass rim light and bright sunset glow with the subdued cool post-sunset illumination described above. No direct sunlight, bright orange clouds, strong amber wash or bright shafts. Preserve readable layered hills and grass detail within the darker exposure.

No flowers in bloom, no visible petals, no particles. Keep the vegetation layout fixed; any flower forms must remain unobtrusive closed buds without blooming petals. No added objects or celestial bodies. Stars belong only in the sky. Same camera, same geography.
```
