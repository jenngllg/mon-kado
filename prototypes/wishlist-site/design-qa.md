# Design QA — La liste de Léa

- Source visual truth: `design/mockups/03-shared-wishlist-selected-concept.png`
- Browser-rendered implementation: `qa/implementation-desktop.png`
- Responsive evidence: `qa/implementation-tablet.png`, `qa/implementation-mobile.png`
- Full comparison: `qa/comparison-full.png`
- Focused comparison: `qa/comparison-focus.png`
- State: shared birthday wishlist, one gift already reserved, primary picnic-basket gift available
- Browser: Codex in-app browser

## Viewport and normalization

- Source pixels: 1536 × 1024.
- Desktop CSS viewport: 1440 × 1024 at device scale factor 1; screenshot pixels: 1424 × 1027 after scrollbar normalization.
- Tablet CSS viewport: 1024 × 1024 at device scale factor 1; full-page screenshot pixels: 1008 × 1765.
- Mobile CSS viewport: 390 × 844 at device scale factor 1; visible-viewport screenshot pixels: 375 × 811.
- Full-view comparison normalizes the source with a centered crop to 1424 × 1024 and the implementation to the same 1424 × 1024 content frame. Browser chrome is excluded.
- Focused comparison normalizes the profile column and the first four gift cards independently so typography, imagery, radii, state chips, and controls remain readable.

## Full-view comparison evidence

The implementation preserves the source composition: a pale-sage editorial profile column, warm ivory gift surface, coral primary action, two-column desktop gift grid, six image-led gifts, and explicit available/reserved states. The large-region proportions, reading order, visual density, and primary action hierarchy match the selected design.

## Focused comparison evidence

- Profile: circular portrait, title, date, coral divider, list message, reservation progress, and copy-link control are all present in the same order and visual family.
- Cards: product subjects, half-image/half-copy desktop anatomy, rounded borders, status pills, coral featured border, reserved treatment, price placement, and button hierarchy match the source.
- A focused comparison was required because the card typography, image crop, and small state treatments were not readable enough in the full-width comparison alone.

## Required fidelity surfaces

- Fonts and typography: Nunito Sans provides the rounded humanist character of the reference. Heading, item, price, status, and supporting-text weights preserve the hierarchy; French wrapping is clean at all three breakpoints.
- Spacing and layout rhythm: desktop column proportions, card gaps, image/text split, radii, and section rhythm closely match the mockup. Tablet switches to an introduction band and vertical cards; mobile uses a compact identity header and one-column list.
- Colors and visual tokens: sage, forest green, ivory, coral, available green, and reserved blush states map consistently to CSS custom properties with accessible foreground contrast.
- Image quality and asset fidelity: all eight visible raster assets are present, sharp, subject-correct, and share a warm natural editorial treatment. No product image, portrait, or decorative motif is replaced by CSS art or a placeholder.
- Copy and content: the French title, date, wish count, thanks message, progress, gift names, prices, availability states, and actions are coherent and faithful to the selected concept.
- Icons: Phosphor's regular/fill icon family supplies the calendar, gift, link, heart, and close controls with consistent optical weight.
- Accessibility: semantic buttons and dialogs, visible focus rings, alt text, keyboard-reachable controls, 44px mobile targets, responsive text wrapping, and reduced-motion support are present.

## Findings

- No actionable P0, P1, or P2 findings remain.
- [P3] The implementation's profile title is slightly bolder and the vertical profile rhythm is a little more compact than the generated mockup. This is acceptable: hierarchy and balance are preserved, and the adjustment improves resilience across laptop and tablet heights.
- [P3] The newly generated lifestyle assets are not pixel-identical to the mockup's embedded photos, but they match each subject, crop intent, palette, lighting, and editorial art direction.

## Comparison history

1. Initial desktop asset pass
   - Earlier finding: project-bound images were present but the plain local server did not expose the starter's `public/` alias, causing broken visual assets.
   - Fix: copied the generated project assets into the root `/assets` path used by both the vanilla preview and the future Vite path.
   - Post-fix evidence: `qa/implementation-desktop.png`; all portrait, product, and background assets render sharply.
2. Tablet pass at 1024 × 1024
   - Earlier finding: horizontal card anatomy was too dense for a two-column tablet grid, and the three-part introduction band compressed its progress copy.
   - Fix: changed tablet cards to image-over-content anatomy and reduced the introduction band to identity plus progress at 1080px and below.
   - Post-fix evidence: `qa/implementation-tablet.png`; two balanced columns render without horizontal overflow.
3. Mobile pass at 390 × 844
   - Earlier finding: the first mobile title and date treatment clipped inside the compact profile header.
   - Fix: reduced the small-screen title scale, constrained the title group, and enabled event-line wrapping.
   - Post-fix evidence: `qa/implementation-mobile.png`; title, date, progress, share action, primary gift, and next-card cue are visible without overlap.

## Interaction and console verification

- Opened the “Je l’offre” flow, filled the optional first name, and confirmed the reservation.
- Verified the reservation count updates from 1/6 to 2/6 and the success toast appears.
- Opened and closed a “Voir le cadeau” details dialog.
- Copied the wishlist link and verified the success toast.
- Checked browser console warnings and errors after the interaction pass: none.

## Implementation checklist

- [x] Selected desktop visual faithfully recreated.
- [x] Tablet and mobile layouts verified in browser.
- [x] Core reservation, details, and copy-link interactions verified.
- [x] Image assets and icon family complete.
- [x] No remaining P0/P1/P2 findings.

## Follow-up polish

- Optional P3: slightly reduce the desktop profile heading weight if a softer match is preferred after user review.

final result: passed
