# Mon Kado wishlist prototype

This directory preserves the responsive frontend prototype selected during the initial Mon Kado design exploration. It is intentionally isolated from the production API until a frontend architecture and integration strategy are selected.

## Contents

- `src/`: vanilla JavaScript and CSS implementation.
- `public/assets/`: generated images used by the running prototype.
- `design/mockups/`: the three initial visual directions and six additional product screens.
- `qa/`: desktop, tablet, and mobile validation captures.
- `reference-assets/`: crops from the selected visual direction used while implementing the prototype.
- `asset-prompts.md`: prompts and constraints used to create the visual assets.
- `design-qa.md`: implementation comparison and responsive review notes.

The selected direction is [`design/mockups/03-shared-wishlist-selected-concept.png`](design/mockups/03-shared-wishlist-selected-concept.png).

## Sharing

Discord and X buttons prepare a message with the complete current link, including its fragment. The message is copied locally, then the user opens the selected platform and pastes it into a conversation or post. External links have no message or share-token parameters and send no referrer. If clipboard access is unavailable, the dialog selects the text for manual copying. This remains a frontend prototype: production share-link creation and API integration are not connected yet.

## Run locally

```powershell
pnpm install
pnpm run dev
```

## Validate

```powershell
pnpm run build
pnpm run test:sites
```

The generated `dist/` directory and `node_modules/` are intentionally not versioned.
