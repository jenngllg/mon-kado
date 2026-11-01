# Règles communes aux dépôts MonKado

Ces règles s'appliquent à `mon-kado` et `mon-kado-front`, en complément des préférences globales de Jenn dans `~/.codex/AGENTS.md`. Les deux AGENTS.md demandent explicitement de lire ce fichier ; les chemins ci-dessous sont relatifs à son emplacement dans le dépôt backend.

## Organisation des dépôts

- Le frontend principal est le dépôt voisin `../mon-kado-front`. Appliquer les changements frontend et démarrer son serveur depuis ce dépôt, jamais depuis une copie sous les résultats de tests du backend.
- Avant d'intervenir dans un autre dépôt MonKado, lire et appliquer son AGENTS.md : [backend](AGENTS.md) et [frontend](../mon-kado-front/AGENTS.md).
- Exécuter PostgreSQL, API, worker, outils et tests nativement ; utiliser Docker uniquement si un besoin concret le rend nécessaire et expliquer ce besoin.

## Contrats, règles métier et confidentialité

- Conserver les contrats HTTP/JSON, règles d'accès et invariants métier lors d'un changement technique.
- Administration : utiliser uniquement le booléen vivant `users.is_admin`, exposé comme `isAdmin`. Ne jamais déduire les droits de chaînes `Admin` ou `Administrator`, même comme secours de compatibilité dans le frontend. Les migrations et preuves historiques restent conservées.
- Pour toute modification de page, vérifier si une vue équivalente existe dans les groupes ; lui appliquer aussi le changement pertinent et vérifier les deux parcours, en respectant leurs droits et règles de confidentialité.
- Identifiants membres : pseudo libre avec tag de quatre chiffres attribué automatiquement côté serveur, immuable pendant toute la vie du compte, jamais choisi ni généré dans le front. Afficher le tag pour information dans le profil ; le changement de pseudo ne transmet pas le tag. Garantir l'unicité de l'ensemble pseudo normalisé/tag ; refuser un renommage en conflit plutôt que changer le tag. Ne pas changer les UUID d'autorisation ni les pseudonymes invités.
- Impersonation administrateur (Jenn) : aucune limite de durée propre à la simulation ; arrêt manuel, avec maintien des contrôles de session, rôle et double authentification. Jenn autorise la consultation et les modifications réelles avec les droits du compte simulé ; conserver son identité admin comme auteur réel dans les journaux. Les opérations de sécurité du compte et les fonctions d’administration restent indisponibles en simulation.
- Secrets de partage dans les fragments frontend, transmis dans l'en-tête dédié, jamais dans les URLs serveur ou logs.

## Mises en production et données réelles

- La production contient plusieurs utilisateurs réels. Pour chaque MEP, y compris frontend ou configuration, la sauvegarde préalable doit couvrir PostgreSQL et les fichiers persistants nécessaires à la restauration. Réaliser un nouveau snapshot, vérifier son intégrité et sa copie chiffrée hors du VPS, puis conserver sa référence dans le compte rendu de déploiement.
- Une sauvegarde quotidienne antérieure ou réalisée seulement après la MEP ne remplace pas cette sauvegarde préalable. Un échec ou une incertitude de transfert distant bloque aussi la MEP avant toute bascule ou migration.
- L'autorisation de remise à zéro donnée pour la première bascule est caduque ; toute nouvelle remise à zéro ou purge destructive des données réelles exige une nouvelle demande explicite et ciblée de Jenn.
- Jenn autorise les publications demandées avec les documents juridiques actuels encore en brouillon, en utilisant la procédure de publication temporaire et en laissant le site en ligne ; ne plus lui redemander cette confirmation. Cette autorisation ne vaut pas validation juridique : ne pas déclarer ces documents validés sans validation explicite et conserver les autres contrôles de production.

## Git et conventions

- Commits, titres et descriptions de PR en anglais.
- Pour une règle de formatage, vérifier d'abord une option standard `.editorconfig` ; sinon documenter la règle dans le fichier AGENTS.md pertinent, sans conflit avec une règle existante.
- Si une préférence récurrente nécessite le choix de Jenn, poser une question courte avec deux exemples.
