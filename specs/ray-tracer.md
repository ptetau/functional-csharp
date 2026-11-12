# Ray tracer: render a simple 3D scene to an image

A command line program that reads a scene description and renders it to an image file by ray tracing.

## Scene file

A text file, one item per line. `#` starts a comment. Numbers are decimals. Colours are red, green and blue from 0 to 1.

```
# Output size and samples per pixel
image 320 240 4
camera  0 1 -5   0 1 0   60        # position, look-at point, vertical field of view in degrees
background 0.6 0.8 1.0
light   -5 5 -5   1 1 1             # point light: position, colour
material red    diffuse   0.9 0.2 0.2
material mirror reflect   0.9 0.9 0.9  0.8   # colour, reflectivity 0..1
material floor  diffuse   0.8 0.8 0.8
sphere  0 1 0   1       red         # centre, radius, material
sphere  2 1 1   1       mirror
plane   0 1 0   0       floor       # normal, distance from origin, material
```

- R-1 Each item may appear in any order. `image` and `camera` are required. Every material named by a shape must be defined.
- R-2 Errors in the scene file are reported with their line number. All errors are reported, not just the first.

## Rendering

- R-3 For each pixel, fire `samples` rays through evenly spread points inside the pixel (a square grid when `samples` is a square number, otherwise evenly spaced along the diagonal), and average their colours.
- R-4 A ray's colour is the colour of the nearest surface it hits in front of the camera, or the background colour if it hits nothing.
- R-5 Diffuse surfaces use Lambert shading: material colour × light colour × max(0, normal · direction to light), summed over lights, plus an ambient term of 0.1 × material colour.
- R-6 A point is in shadow for a light if any surface lies between it and the light. Shadowed lights contribute nothing.
- R-7 Reflective surfaces mix their diffuse shading with the colour of the reflected ray: `(1 − reflectivity) × diffuse + reflectivity × reflected`. Reflection stops after 5 bounces, and the 5th bounce returns the background colour.
- R-8 To avoid a surface shadowing or reflecting itself, start secondary rays a small distance (1e-4) along the surface normal.
- R-9 Final colours are clamped to 0–1, gamma corrected with gamma 2.2, and scaled to 0–255, rounding to the nearest whole number.
- R-10 Rendering must be deterministic: the same scene gives byte-identical output every time.

## Output

- R-11 `raytrace <scene-file> <output.ppm>` writes a binary PPM (P6) image and prints the time taken.
- R-12 `--threads <n>` may split the work across threads. The output must be identical whatever the thread count.

## Examples

- A scene with only `image 2 2 1`, `camera 0 0 -1 0 0 0 90` and `background 1 0 0` produces a 2×2 PPM where every pixel is `255 0 0`.
- A ray from `(0,0,-5)` towards `(0,0,1)` hits a unit sphere at the origin at distance 4, with normal `(0,0,-1)`.
- A diffuse surface lit head-on by a white light with colour `(0.5, 0.5, 0.5)`, no shadow and nothing else, has colour `0.5 + 0.1 × 0.5 = 0.55` per channel. After gamma correction that is 194 (`round(255 × 0.55^(1/2.2))`).
- The sample scene above renders at 320×240 with 4 samples in under 10 seconds on a typical laptop.

## Out of scope

Textures, refraction, area lights, triangles or meshes, anti-aliasing beyond the fixed sampling above, any output format other than PPM.
