// Summarize one completed local campaign; suggestions are not product-identity assertions.
const fs = require("node:fs");
const path = require("node:path");
const repo = path.resolve(__dirname, "../../..");
const [campaignFile, imagesFile, outputFile] = process.argv.slice(2).map((value) => value && path.resolve(value));
if (!campaignFile || !imagesFile || !outputFile || !outputFile.startsWith(path.resolve(repo, ".local") + path.sep)) throw new Error("Provide campaign, image results and a new output file under .local.");
const read = (file) => fs.readFileSync(file, "utf8").trim().split(/\r?\n/).map(JSON.parse);
const campaign = read(campaignFile).sort((a, b) => a.id - b.id);
const images = new Map(read(imagesFile).map((row) => [row.id, row]));
if (campaign.length !== 100 || new Set(campaign.map((row) => row.id)).size !== 100 || images.size !== 100 || campaign.some((row) => !images.has(row.id))) throw new Error("The campaign or image evidence is incomplete.");
const complete = (row) => !row.blocked && !row.nameFromURL && row.name && row.price && images.get(row.id).decoded;
const someReal = (row) => !row.blocked && !row.nameFromURL && (row.name || row.price || images.get(row.id).decoded);
const esc = (value) => String(value || "—").replace(/\s+/g, " ").replace(/[&<>"'\\|\[\]`*_~]/g, character => `&#${character.codePointAt(0)};`).trim();
const imageLabel = (row) => ({ decoded: "Décodée", no_candidate: "Aucune", download_unavailable: "Indisponible en accès direct", decode_or_budget: "Non décodée dans le délai" })[images.get(row.id).category] || "Non vérifiée";
const summary = { total: campaign.length, complete: campaign.filter(complete).length, partial: campaign.filter((row) => someReal(row) && !complete(row)).length,
  blocked: campaign.filter((row) => row.blocked).length, errors: campaign.filter((row) => row.error).length,
  empty: campaign.filter((row) => !row.error && !row.blocked && !someReal(row)).length,
  urlNames: campaign.filter((row) => row.nameFromURL).length, decodedImages: [...images.values()].filter((row) => row.decoded).length,
  imageCandidates: campaign.filter((row) => row.imageCandidate).length, overTenSeconds: campaign.filter((row) => row.durationMs > 10000).length,
  maxPageMs: Math.max(...campaign.map((row) => row.durationMs)) };
const text = ["# POC MonKado — essai direct sur 100 liens", "", "Campagne locale du 28 octobre 2026 sur l’inventaire original de 100 liens, y compris Rakuten. Les pages sont ouvertes par Chromium dans des sessions isolées, sans service d’extraction externe. Un garde réseau local filtre les destinations et épingle les IP publiques ; les images, polices, médias, WebSockets et requêtes autres que GET/HEAD sont bloqués pendant la lecture des pages.", "",
  `Sur 100 liens : **${summary.complete}** présentent un nom suggéré, un prix suggéré et une image décodée ; **${summary.partial}** fournissent au moins un champ sans réunir les trois ; **${summary.blocked}** pages sont classées bloquées, **${summary.empty}** ne fournissent aucune donnée produit et **${summary.errors}** navigation(s) ont échoué. Les catégories de pages bloquées ou en échec sont distinctes. ${summary.urlNames} liens ont en outre un intitulé approximatif tiré de leur URL.`, "",
  `Le POC a trouvé ${summary.imageCandidates} adresses d’image ; ${summary.decodedImages} fichiers ont pu être téléchargés directement, décodés et normalisés séparément, avec les limites du backend. Les résultats ne démontrent pas que chaque titre, prix, variante ou image correspond au bon produit. Les suggestions restent à vérifier ; l’extension propose de copier ou de garder l’image visible avant de la transférer dans MonKado. Le chargement d’une image avec les cookies du navigateur peut différer de cette vérification indépendante.`, "",
  `La navigation et l’extraction ont dépassé 10 secondes pour ${summary.overTenSeconds} pages ; le maximum observé est ${summary.maxPageMs} ms. La vérification séparée des images ajoute son propre délai. Ce n’est donc pas une mesure de bout en bout sous 10 secondes. Les résultats peuvent varier selon les protections, disponibilités, prix et connexion.`, "",
  "Les deux liens Fnac Wednesday fournis séparément ne font pas partie du score ci-dessus. Après vérification humaine dans le navigateur, la page française affichait 12,34 € et la page belge 24,48 €, avec leurs images. La fiche Fnac du jeu des 100 liens affichait 25 € et 23,75 € pour les adhérents. L’extension a été testée sur des pages simulant ces structures ; Jenn a ensuite montré ses résultats sur les deux fiches réelles : noms, 12,34 € et 24,48 €, ainsi que des adresses d’images, dont les champs longs sont partiellement tronqués sur les captures. Le transfert des images Fnac au formulaire reste à vérifier en session réelle.", "",
  "| N° | Marchand et lien | Nom proposé | Prix proposé | Image vérifiée séparément | État |", "| ---: | --- | --- | ---: | --- | --- |"];
for (const row of campaign) {
  const link = new URL(row.url).href.replace(/>/g, "%3E");
  const label = row.error ? "Navigation échouée" : row.blocked ? "Page bloquée" : complete(row) ? "Trois champs" : someReal(row) ? "Partiel" : "Aucune donnée produit";
  text.push(`| ${row.id} | [${esc(row.merchant)}](<${link}>) | ${esc(row.name)}${row.nameFromURL ? " (déduit de l’URL)" : ""} | ${row.price ? `${esc(row.price)} €${row.displayedVariantPrice ? " (variante affichée)" : ""}` : "—"} | ${imageLabel(row)} | ${label}${row.storeOnly ? " ; magasin uniquement" : ""} |`);
}
fs.writeFileSync(outputFile, text.join("\n") + "\n", { flag: "wx" });
console.log(JSON.stringify({ summary, outputFile }));
