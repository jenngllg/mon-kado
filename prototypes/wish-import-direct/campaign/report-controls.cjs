// Summarize the five user-provided links from one local campaign.
const fs = require("node:fs");
const path = require("node:path");
const repo = path.resolve(__dirname, "../../..");
const [campaignFile, imagesFile, outputFile] = process.argv.slice(2).map((value) => value && path.resolve(value));
if (!campaignFile || !imagesFile || !outputFile || !outputFile.startsWith(path.resolve(repo, ".local") + path.sep)) throw new Error("Provide campaign, image results and a new output file under .local.");
const read = (file) => fs.readFileSync(file, "utf8").trim().split(/\r?\n/).map(JSON.parse);
const campaign = read(campaignFile).sort((a, b) => a.id - b.id);
const images = new Map(read(imagesFile).map((row) => [row.id, row]));
if (campaign.length !== 5 || images.size !== 5 || campaign.some((row, index) => row.id !== 101 + index || !images.has(row.id))) throw new Error("The five-link evidence is incomplete.");
const esc = (value) => String(value || "—").replace(/\s+/g, " ").replace(/[&<>"'\\|\[\]`*_~]/g, character => `&#${character.codePointAt(0)};`).trim();
const text = ["# POC MonKado — cinq liens fournis", "", "Essai local dans des sessions Chromium isolées, sans service d’extraction externe. Les protections des marchands et le contenu peuvent varier selon la session ; aucune vérification humaine n’a été effectuée dans cette campagne.", "",
  "Les noms et prix sont des suggestions, à vérifier. Les images sont téléchargées et décodées séparément sans les cookies du navigateur ; le POC demande une action de l’utilisateur pour les transférer. Les résultats ne prouvent pas que chaque image ou variante est correcte.", "",
  "| Marchand et lien | Nom proposé | Prix proposé | Image | État |", "| --- | --- | ---: | --- | --- |"];
for (const row of campaign) {
  const url = new URL(row.url).href.replace(/>/g, "%3E");
  const image = images.get(row.id);
  const label = row.error ? "Navigation échouée" : row.blocked ? "Page bloquée" : row.name && !row.nameFromURL && row.price && image.decoded ? "Trois champs candidats" : "Partiel";
  const imageText = image.decoded ? "Décodée séparément" : image.category === "no_candidate" ? "Aucune" : "Non décodée séparément";
  text.push(`| [${esc(row.merchant)}](<${url}>) | ${esc(row.name)}${row.nameFromURL ? " (déduit de l’URL)" : ""} | ${row.price ? `${esc(row.price)} €` : "—"} | ${imageText} | ${label} |`);
}
text.push("", "Les pages Fnac France et Belgique ont également été consultées après vérification humaine dans une autre session. L’extension a été testée sur des pages simulant les structures observées, pas dans cette session réelle.");
fs.writeFileSync(outputFile, text.join("\n") + "\n", { flag: "wx" });
console.log(JSON.stringify({ total: campaign.length, outputFile }));
