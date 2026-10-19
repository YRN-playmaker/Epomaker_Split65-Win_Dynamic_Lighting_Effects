# Chroma layout and cross-device coupling

English | [中文](CHROMA.md)

Companion 0.4.9.3 and later includes **Fix Chroma coupling**. Export the current setup from Chroma Studio, choose and save a separated copy in Companion, then import it into Chroma and select the `[Split65 separated]` profile. No Python or keyboard reflashing is required; the original profile is not overwritten. The tool supports the observed version-1, unrotated JSON exports with 0/40-pixel lamp cells. It processes the full matrix using the model below. The current `】` effect and actual import of the new build still need validation.

On 2026-10-18, the user confirmed that importing the separated device layout removed the previous coupling between keyboard keys and the Cobra Pro mouse. Both halves were v0.4.7; firmware, driver, lamp IDs and key bindings were not changed.

## Usage

1. Fully exit the OpenRGB background process and confirm that Windows Settings → Personalization → Dynamic Lighting shows Split65.
2. In Windows Dynamic Lighting's background control priority, put Chroma first and keep the currently working mouse control method.
3. In Chroma Studio, first copy or export the original profile, then use the move tool (M) to move the mouse far to the right. If both native and Windows Dynamic Lighting mouse instances appear, move both away and leave distance between them.
4. Save the layout, reassign a solid color to the previously affected keys and save, then check whether the mouse follows. Checking only that device outlines do not overlap is not enough to rule out logical lamp-position collisions.

In the successful export, the keyboard stayed fixed; native mouse X moved from 5360 to 6120, and WDL mouse X moved from 5360 to 6640; Y was unchanged. These numbers apply only to that export; leave space according to the device matrices in other layouts.

## Basis and scope

The Split65 export graphics are 660 wide, while the lamp matrix has 28 columns. Based on the 40-pixel lamp cells in the mouse definition, the logical lamp range is wider than the graphics frame.

Expanding lamp positions with `global position = device origin + (column, row) × 40` accurately reproduces the observed collisions:

| Mouse path | Corresponding Split65 keys |
| --- | --- |
| Native Chroma | P |
| Windows Dynamic Lighting | `-`, `=`, `;`, `/`, Right Shift |

After moving the mouse, model overlaps dropped from 8 pairs to 0, and the user reported that the coupling disappeared. This supports the hypothesis that the Chroma canvas layout or scaling causes logical lamp-position overlap; without Chroma source code or vendor confirmation, it is not a claim about the internal implementation. The current profile has been validated; other effects, versions, devices and long-running use still need validation.

## Optional export tool

Python is not required for normal use. Developers can run:

```bash
python3 tools/chroma-mapping/separate_canvas_layout.py input.ChromaEffects separated.ChromaEffects
```

The tool accepts the observed version-1, single-profile JSON export. It moves the mouse to the right of the keyboard's logical range, generates a new title, profile ID and layer ID, and preserves device identities, matrices and effect targets. The original file is not overwritten; if the target file already exists, the tool stops. After import, select `Split65-Separated-Layout`. If the original export did not save the effect area, reselect the lamp positions, set the effect and save again.

The 40-pixel model is inferred from these exports and is not guaranteed to apply to every format or scale. Keep the original profile; stop when the format does not match, and do not delete device fields to force an import. The public repository does not contain user profiles, logs or device serial numbers.

## References

- [Razer Chroma App manual](https://dl.razerzone.com/master-guides/RazerChromaApp/RazerChromaApp-en.pdf): Studio move tool and device layout.
- [Razer third-party device control FAQ](https://mysupport.razer.com/app/answers/detail/a_id/13698/kw/off): Windows Dynamic Lighting and background priority.

The official documents describe the operation entry points; the coordinate model and results come from user exports and testing, not from an official defect statement.
