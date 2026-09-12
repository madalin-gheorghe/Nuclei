# Documentation screenshot rules

User-approved rules, updated 2026-09-12.

## Image sizing

- Display all documentation images at the same width as the text column, preserving their aspect ratio. Do not cap the height in a way that makes an image narrower than the text. Approved 2026-09-13.
- Export full definition diagrams at 1,840 pixels wide; export component screenshots and other displayed images at 920 pixels wide. Preserve aspect ratio and optimize file size for fast loading. Display both at the text column width. Higher-resolution native captures may be kept outside the guide as source files. Never enlarge a low-resolution screenshot; recapture it first. Check pixel dimensions and file sizes before delivering the review.

## Individual component images

- Render actual Grasshopper components with icons clearly visible, using the original Nuclei V4 definitions where available.
- Show only the featured component and its directly connected sliders or primitives. Keep those controls close, fully inside the picture, and neatly aligned.
- Preserve the connected input wires from other components and connected output wires. Let them continue beyond the image edges, without displaying the source or destination components. Use actual representative connections to show common usage; do not imply that disconnected inputs are normally unused.
- Render all wires behind components and primitives. Never draw wires over component bodies, icons, labels, or input/output sockets.
- No groups, group labels, compass widget, disabled-canvas overlay, or image outline.
- Cover every public component, especially the previews and extraction category. Audit the full inventory; do not stop at components already pictured in the example definitions. Any additional example wiring must be grounded in the component implementation and presented accurately.
- Do not add descriptions or captions underneath images. Keep accessibility alt text where it does not appear as a visible caption.

## Definition images

- Preserve the original definition layout, settings, wires, and group colors.
- Keep groups, but hide their descriptions or labels.
- No compass widget, disabled-canvas overlay, or image outline.
- Do not modify original .gh files to make screenshots; work on temporary copies.
- Do not add descriptions or captions underneath images.

## Approval and publication

- The user approved the solver and mixed-input samples and gave explicit GO for the full update.
- Apply the approved style across every public component, remove image captions, and publish through GitBook Git Sync. Further changes to this visual style should be shown as a sample before a full replacement.
- The previously published image revision is commit 646ac0b.
