// Runs only in the active MonKado tab after an explicit extension action.
globalThis.monKadoApplySuggestion = function (data) {
  const form = document.querySelector('.wish-create-view form[aria-label="Ajouter un souhait"]');
  const analysis = document.querySelector('.wish-create-view .wish-import form[aria-label="Analyser un lien produit"]');
  const importPanel = analysis?.closest('.wish-import');
  const link = analysis?.querySelector('input[inputmode="url"]');
  const name = form?.querySelector('input[name="name"]');
  const price = form?.querySelector('input[name="price"]');
  if (!form || form.hidden || !importPanel || importPanel.hidden || !link || link.disabled || !name || name.disabled || !price || price.disabled) return { message: "Reviens d’abord à l’étape du lien dans l’ajout d’un souhait." };
  if (link.value.trim() && link.value.trim() !== data.url) return { message: "Un autre lien est déjà saisi ; conserve-le ou efface-le avant de réessayer." };
  if (analysis.dataset.directImportPoc !== "1") return { message: "Ce formulaire ne prend pas encore en charge le transfert direct du POC." };
  const filled = (!name.value.trim() && !!data.name) || (!price.value.trim() && !!data.price);
  const accepted = !analysis.dispatchEvent(new CustomEvent("monkado-direct-import", { bubbles: true, cancelable: true, detail: data }));
  if (!accepted) return { message: "Transfert direct refusé. Vérifie les informations et réessaie." };
  return { message: filled ? "Champs remplis. Vérifie-les avant d’ajouter le souhait." : "Lien renseigné. Les champs déjà remplis ont été conservés." };
};

// Transfers an explicitly selected image; MonKado validates it as a local file.
globalThis.monKadoApplyImage = function ({ type, encoded, expectedURL }) {
  const form = document.querySelector('.wish-create-view form[aria-label="Ajouter un souhait"]');
  const panel = document.querySelector('.wish-create-view .wish-import__image-panel');
  const input = panel?.querySelector('input[type="file"]');
  const remove = panel?.querySelector('.wish-image-section__remove');
  if (!form || form.hidden || !input || input.disabled || !remove) return { message: "Attends la fin de l’analyse du lien avant d’ajouter une image." };
  if (expectedURL !== undefined) {
    const link = document.querySelector('.wish-create-view .wish-import form[aria-label="Analyser un lien produit"] input[inputmode="url"]');
    if (!link || link.value.trim() !== expectedURL) return { message: "Le lien du formulaire ne correspond pas à cette image." };
  }
  if (!remove.hidden) return { message: "Une image est déjà sélectionnée ; retire-la avant de coller une autre image." };
  if (!["image/png", "image/jpeg", "image/webp"].includes(type) || typeof encoded !== "string" || !encoded || encoded.length > 4 * Math.ceil(10 * 1024 * 1024 / 3)) return { message: "L’image copiée est invalide ou trop volumineuse." };
  try {
    const binary = atob(encoded);
    if (!binary.length || binary.length > 10 * 1024 * 1024) return { message: "L’image copiée est invalide ou trop volumineuse." };
    const bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0));
    const file = new File([bytes], "image", { type });
    const transfer = new DataTransfer(); transfer.items.add(file);
    input.files = transfer.files;
    input.dispatchEvent(new Event("change", { bubbles: true }));
    return { message: "Image transmise au formulaire : vérifie son aperçu." };
  } catch { return { message: "Impossible de transférer l’image copiée." }; }
};
