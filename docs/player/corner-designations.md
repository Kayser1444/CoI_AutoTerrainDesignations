# Corner Designations

*Current as of: v0.4.0j*

## What it does

Corner Designations add a manual terrain-shaping mode to the vanilla terrain designation tools. Instead of placing only flat or ramp-style 4×4 designations, you can place outer, inner, or diagonal Planar designations.

This is useful for manual construction work such as:

- hand-built ramps and transitions
- quay walls and embankments
- custom excavation shapes
- terrain grading that is not tied to ATD's automatic mining scan

Corner mode works with the normal terrain designation tools, so it can be used for mining, dumping, or leveling work.

## Supported tools

Corner mode works inside these vanilla designation tools:

- `M` mining
- `Z` dumping
- `N` leveling

ATD uses the currently active tool's normal designation type, so a corner placed in mining mode becomes a mining designation, a corner placed in dumping mode becomes a dumping designation, and so on.

## Controls

### Entering and leaving corner mode

- `K` enters corner mode while a terrain designation tool is active.
- Press `K` again to cycle through outer, inner, and Planar corner shapes.
- `F` exits corner mode.

Corner mode also exits automatically if you switch back to one of the vanilla flat/ramp modes or deactivate the terrain designation tool.

### Shape controls

- `R` rotates the active corner or Planar orientation.
- The normal designation height offset controls still apply.
- Corner mode snaps to neighboring designations when possible.

## Outer vs inner shapes

Corner mode supports three shape families:

- **Outer** corners raise one corner above the other three.
- **Inner** corners lower one corner below the other three.
- **Planar** corners keep two diagonal corners at the baseline, raise one of the other diagonal corners by one level, and lower the opposite corner by one level.

Press `K` again while already in corner mode to cycle between them. The toolbar buttons select the corresponding family directly; the buttons are ordered Outer, Inner, Planar, with the Planar button using a dedicated diagonal-plane icon.

### Planar shape

The default Planar orientation has corner heights like this, relative to its baseline:

```text
[+1][ 0]
[ 0][-1]
```

Rotate with `R` to move the high/low diagonal pair. Planar designations are supported by the same mining, dumping, and leveling tools as the other corner shapes, and preview and drag placement work the same way.

## Drag placement

Corner mode supports drag placement across multiple 4×4 cells.

When dragging, ATD fills the area with a checkerboard-compatible pattern so adjacent cells share matching edge heights. In practice, this makes it much easier to sketch continuous slopes and stepped transitions without correcting every tile by hand.

## Practical uses

### Manual ramps

Use mining or leveling mode together with corner placement to sketch custom approach ramps where ATD's automatic access-ramp planner is not the right tool.

### Shorelines and retaining edges

Use dumping or leveling mode to build shaped edges, small terraces, or transitions that are more controlled than a large flat fill.

### Finishing work around automation

Corner mode is useful after ATD has already created automatic mining designations. You can manually adjust nearby terrain without rerunning the full scan.

## Things to know

- Corner Designations are a manual shaping tool. They are separate from ATD's automatic mining scan and from farmland preparation automation.
- Vanilla designations can snap to existing corner designations, and corner designations snap to neighboring terrain/designations when possible.
- Because corner mode lives inside the normal terrain designation tools, it follows the behavior of the active tool type.

## Related guides

- [Mining Designations](mining-designations.md)
- [Farmland preparation](farming-designations.md)
