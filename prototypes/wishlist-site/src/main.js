import "@fontsource-variable/nunito-sans";
import "@phosphor-icons/web/regular";
import "@phosphor-icons/web/fill";
import "./styles.css";

const gifts = [
  {
    id: "picnic",
    name: "Panier pique-nique en osier",
    price: "59,00 €",
    image: "/assets/gift-picnic-basket.png",
    alt: "Panier de pique-nique en osier posé dans un jardin",
    status: "available",
    featured: true,
    note: "Un joli panier pour nos déjeuners au parc.",
  },
  {
    id: "print",
    name: "Affiche botanique",
    price: "25,00 €",
    image: "/assets/gift-botanical-print.png",
    alt: "Affiche botanique encadrée dans des tons sauge et terracotta",
    status: "available",
    note: "Pour apporter un peu de douceur au salon.",
  },
  {
    id: "perfume",
    name: "Eau de parfum Fleur de coton",
    price: "65,00 €",
    image: "/assets/gift-cotton-perfume.png",
    alt: "Flacon de parfum rose pâle dans une lumière douce",
    status: "reserved",
    note: "Une senteur propre, florale et très légère.",
  },
  {
    id: "plant",
    name: "Pilea peperomioides avec pot",
    price: "28,00 €",
    image: "/assets/gift-pilea-plant.png",
    alt: "Pilea verte dans un petit pot en céramique",
    status: "available",
    note: "Une plante facile à vivre pour mon bureau.",
  },
  {
    id: "blanket",
    name: "Plaid en laine mérinos",
    price: "79,00 €",
    image: "/assets/gift-merino-blanket.png",
    alt: "Plaid en laine vert sauge soigneusement plié",
    status: "available",
    note: "Très doux, dans un vert sauge naturel.",
  },
  {
    id: "cooking",
    name: "Cours de cuisine à Paris",
    price: "80,00 €",
    image: "/assets/gift-cooking-class.png",
    alt: "Personne préparant des légumes pendant un atelier cuisine",
    status: "available",
    note: "Un atelier convivial autour de la cuisine végétale.",
  },
];

const app = document.querySelector("#app");
const sharePlatforms = {
  discord: { name: "Discord", url: "https://discord.com/channels/@me", instruction: "Collez le message dans le salon ou la conversation de votre choix." },
  x: { name: "X", url: "https://x.com/intent/tweet", instruction: "Collez le message dans votre publication avant de la publier." },
};
let pendingGiftId = null;
let toastTimeout = null;

function statusLabel(status) {
  return status === "reserved" ? "Réservé" : "Disponible";
}

function giftCard(gift) {
  const action = gift.status === "available"
    ? gift.featured
      ? `<button class="button button-primary reserve-button" data-gift-id="${gift.id}">Je l’offre</button>`
      : `<button class="button button-secondary details-button" data-gift-id="${gift.id}">Voir le cadeau</button>`
    : `<p class="reserved-note">Déjà réservé<br />Merci ! <i class="ph-fill ph-heart" aria-hidden="true"></i></p>`;

  return `
    <article class="gift-card${gift.featured ? " gift-card-featured" : ""}${gift.status === "reserved" ? " gift-card-reserved" : ""}" data-gift-card="${gift.id}">
      <div class="gift-media"><img src="${gift.image}" alt="${gift.alt}" /></div>
      <div class="gift-content">
        <span class="status status-${gift.status}"><span class="status-dot" aria-hidden="true"></span>${statusLabel(gift.status)}</span>
        <div class="gift-copy"><h2>${gift.name}</h2><p class="gift-price">${gift.price}</p></div>
        ${action}
      </div>
    </article>`;
}

function render() {
  const reservedCount = gifts.filter((gift) => gift.status === "reserved").length;
  app.innerHTML = `
    <div class="wishlist-shell">
      <aside class="profile-panel" aria-label="Présentation de la liste">
        <div class="profile-identity">
          <img class="profile-avatar" src="/assets/lea-portrait.png" alt="Portrait de Léa" />
          <div class="profile-title-group">
            <p class="eyebrow">Une wishlist à partager</p>
            <h1>La liste de Léa</h1>
            <p class="event-line"><i class="ph ph-calendar-blank" aria-hidden="true"></i> Anniversaire · 24 septembre</p>
          </div>
        </div>
        <div class="profile-message">
          <span class="accent-line" aria-hidden="true"></span>
          <p class="wish-count">6 idées pour me faire plaisir</p>
          <p class="thanks">Merci d’être là <i class="ph ph-heart" aria-hidden="true"></i></p>
        </div>
        <div class="profile-actions">
          <div class="progress-copy" aria-live="polite">
            <span class="progress-icon"><i class="ph ph-gift" aria-hidden="true"></i></span>
            <p><strong>${reservedCount} / 6 déjà réservé${reservedCount > 1 ? "s" : ""}</strong><br /><span>Plus il y en a, plus je suis gâtée !</span></p>
          </div>
          <button class="button button-copy" id="copy-link"><i class="ph ph-link-simple" aria-hidden="true"></i>Copier le lien</button>
          <div class="social-share" role="group" aria-label="Partager la wishlist">
            <button class="button button-secondary" data-share-platform="discord" aria-label="Partager sur Discord"><i class="ph ph-discord-logo" aria-hidden="true"></i>Discord</button>
            <button class="button button-secondary" data-share-platform="x" aria-label="Partager sur X"><i class="ph ph-x-logo" aria-hidden="true"></i>X</button>
          </div>
        </div>
      </aside>
      <main class="gift-area">
        <header class="mobile-list-header">
          <p>La wishlist de Léa</p>
          <button class="icon-button" id="mobile-copy-link" aria-label="Copier le lien de la wishlist"><i class="ph ph-link-simple" aria-hidden="true"></i></button>
        </header>
        <section class="gift-grid" aria-label="Idées cadeaux">${gifts.map(giftCard).join("")}</section>
      </main>
    </div>
    <dialog class="dialog" id="reserve-dialog" aria-labelledby="reserve-title">
      <form method="dialog" id="reserve-form">
        <button class="dialog-close" value="cancel" aria-label="Fermer"><i class="ph ph-x" aria-hidden="true"></i></button>
        <span class="dialog-icon"><i class="ph ph-gift" aria-hidden="true"></i></span>
        <p class="eyebrow">Bonne idée</p>
        <h2 id="reserve-title">Réserver ce cadeau ?</h2>
        <p>Léa ne verra pas votre nom, mais ses proches sauront que le cadeau est déjà choisi.</p>
        <label for="guest-name">Votre prénom <span>(facultatif)</span></label>
        <input id="guest-name" name="guestName" autocomplete="given-name" placeholder="Camille" />
        <div class="dialog-actions">
          <button class="button button-secondary" value="cancel">Annuler</button>
          <button class="button button-primary" type="submit" value="confirm">Confirmer la réservation</button>
        </div>
      </form>
    </dialog>
    <dialog class="dialog" id="details-dialog" aria-labelledby="details-title">
      <form method="dialog">
        <button class="dialog-close" value="cancel" aria-label="Fermer"><i class="ph ph-x" aria-hidden="true"></i></button>
        <p class="eyebrow">L’idée de Léa</p>
        <h2 id="details-title"></h2>
        <p id="details-note"></p>
        <p class="details-price" id="details-price"></p>
        <button class="button button-primary" value="cancel">C’est noté</button>
      </form>
    </dialog>
    <dialog class="dialog" id="share-dialog" aria-labelledby="share-title" aria-describedby="share-instruction">
      <form method="dialog">
        <button class="dialog-close" value="cancel" aria-label="Fermer"><i class="ph ph-x" aria-hidden="true"></i></button>
        <p class="eyebrow">À vos proches</p>
        <h2 id="share-title"></h2>
        <p id="share-instruction"></p>
        <label for="share-message">Message à partager</label>
        <textarea id="share-message" rows="4" readonly spellcheck="false"></textarea>
        <p class="share-status" id="share-status" role="status" aria-live="polite"></p>
        <div class="dialog-actions">
          <button class="button button-secondary" id="copy-share-message" type="button">Copier le message</button>
          <a class="button button-primary" id="open-share-platform" target="_blank" rel="noopener noreferrer" referrerpolicy="no-referrer"></a>
        </div>
      </form>
    </dialog>
    <div class="toast" id="toast" role="status" aria-live="polite"></div>`;
  bindInteractions();
}

function showToast(message) {
  const toast = document.querySelector("#toast");
  toast.textContent = message;
  toast.classList.add("toast-visible");
  clearTimeout(toastTimeout);
  toastTimeout = setTimeout(() => toast.classList.remove("toast-visible"), 2600);
}

async function copyListLink() {
  try {
    await navigator.clipboard.writeText(window.location.href);
    showToast("Lien copié — prêt à être partagé !");
  } catch {
    openShareDialog();
    selectShareMessage();
  }
}

function selectShareMessage() {
  const message = document.querySelector("#share-message");
  message.focus();
  message.select();
  document.querySelector("#share-status").textContent = "Copiez le texte sélectionné manuellement.";
}

async function copyShareMessage() {
  try {
    await navigator.clipboard.writeText(document.querySelector("#share-message").value);
    document.querySelector("#share-status").textContent = "Message copié — il ne reste qu’à le coller !";
  } catch {
    selectShareMessage();
  }
}

function openShareDialog(platformKey) {
  const platform = sharePlatforms[platformKey];
  const dialog = document.querySelector("#share-dialog");
  const openLink = document.querySelector("#open-share-platform");
  document.querySelector("#share-title").textContent = platform ? `Partager sur ${platform.name}` : "Copier le lien";
  document.querySelector("#share-instruction").textContent = platform ? platform.instruction : "Copiez ce lien pour partager la wishlist avec vos proches.";
  // Keep the complete link, including its secret fragment, out of external request URLs.
  document.querySelector("#share-message").value = platform ? `Découvrez la liste de Léa sur MonKado !\n${window.location.href}` : window.location.href;
  document.querySelector("#share-status").textContent = "";
  openLink.hidden = !platform;
  if (platform) {
    openLink.href = platform.url;
    openLink.textContent = `Ouvrir ${platform.name}`;
  } else {
    openLink.removeAttribute("href");
  }
  dialog.showModal();
  if (platform) void copyShareMessage();
}

function bindInteractions() {
  const reserveDialog = document.querySelector("#reserve-dialog");
  const detailsDialog = document.querySelector("#details-dialog");
  document.querySelectorAll(".reserve-button").forEach((button) => {
    button.addEventListener("click", () => {
      pendingGiftId = button.dataset.giftId;
      reserveDialog.showModal();
      document.querySelector("#guest-name").focus();
    });
  });
  document.querySelectorAll(".details-button").forEach((button) => {
    button.addEventListener("click", () => {
      const gift = gifts.find((item) => item.id === button.dataset.giftId);
      document.querySelector("#details-title").textContent = gift.name;
      document.querySelector("#details-note").textContent = gift.note;
      document.querySelector("#details-price").textContent = gift.price;
      detailsDialog.showModal();
    });
  });
  document.querySelector("#reserve-form").addEventListener("submit", (event) => {
    event.preventDefault();
    const gift = gifts.find((item) => item.id === pendingGiftId);
    if (!gift) return;
    gift.status = "reserved";
    gift.featured = false;
    reserveDialog.close();
    render();
    showToast("Cadeau réservé — merci pour Léa !");
  });
  document.querySelector("#copy-link").addEventListener("click", copyListLink);
  document.querySelector("#mobile-copy-link").addEventListener("click", copyListLink);
  document.querySelectorAll("[data-share-platform]").forEach((button) => {
    button.addEventListener("click", () => openShareDialog(button.dataset.sharePlatform));
  });
  document.querySelector("#copy-share-message").addEventListener("click", copyShareMessage);
  document.querySelectorAll("dialog").forEach((dialog) => {
    dialog.addEventListener("click", (event) => {
      if (event.target === dialog) dialog.close();
    });
  });
}

render();
