# Règles communes aux dépôts MonKado

Ces règles s'appliquent à `mon-kado` et `mon-kado-front`, en complément des préférences globales de Jenn dans `~/.codex/AGENTS.md`. Les deux AGENTS.md demandent explicitement de lire ce fichier ; les chemins ci-dessous sont relatifs à son emplacement dans le dépôt backend.

## Organisation des dépôts

- Le frontend principal est le dépôt voisin `../mon-kado-front`. Appliquer les changements frontend et démarrer son serveur depuis ce dépôt, jamais depuis une copie sous les résultats de tests du backend.
- Avant d'intervenir dans un autre dépôt MonKado, lire et appliquer son AGENTS.md : [backend](AGENTS.md) et [frontend](../mon-kado-front/AGENTS.md).
- Exécuter PostgreSQL, API, worker, outils et tests nativement ; utiliser Docker uniquement si un besoin concret le rend nécessaire et expliquer ce besoin.

## Livraison et vérification locales

- Après chaque modification, la déployer dans l'environnement local utilisé par Jenn : reconstruire, redémarrer ou recharger les services concernés selon le besoin, puis garder le site accessible.
- Vérifier que le frontend et le backend actifs proviennent des bons dépôts et servent les versions attendues, avec les dernières modifications applicables. Contrôler la compatibilité de leurs contrats HTTP/JSON et la connexion effective du frontend à l'API locale.
- Vérifier le fonctionnement du site applicatif réel dans le navigateur et les parcours concernés avec leurs droits d'accès, ainsi que la disponibilité de l'API et de ses dépendances. Les tests automatisés et aperçus avec données simulées complètent cette vérification sans la remplacer.
- Avant d'annoncer une modification terminée ou de proposer un lien, confirmer que le frontend et le backend sont fonctionnels ensemble et à jour. Si une mise à jour locale ou une vérification est bloquée, donner le blocage précis et distinguer le code modifié du résultat effectivement disponible.

## Contrats, règles métier et confidentialité

- Conserver les contrats HTTP/JSON, règles d'accès et invariants métier lors d'un changement technique.
- Réservations et notifications, règle validée par Jenn le 2 novembre 2026 : hors mode surprise, le propriétaire peut voir le nom affiché de la personne ayant réservé et le souhait concerné, y compris dans une notification menant à sa liste. Cette autorisation ne rend pas ces informations publiques. En mode surprise, ne divulguer au propriétaire ni réservation ni identité, y compris par les notifications ou leur pastille ; masquer les anciennes notifications de réservation et les exclure de la pastille tant que ce mode est actif. Appliquer la confidentialité côté serveur selon le mode courant, dans les vues personnelles comme dans les groupes.
- Administration : utiliser uniquement le booléen vivant `users.is_admin`, exposé comme `isAdmin`. Ne jamais déduire les droits de chaînes `Admin` ou `Administrator`, même comme secours de compatibilité dans le frontend. Les migrations et preuves historiques restent conservées.
- L’administration des comptes affiche les créations les plus récentes en premier et les plus anciennes en dernier ; appliquer ce tri côté serveur avant pagination, y compris après recherche.
- Pour toute modification de page, vérifier si une vue équivalente existe dans les groupes ; lui appliquer aussi le changement pertinent et vérifier les deux parcours, en respectant leurs droits et règles de confidentialité.
- Identifiants membres : pseudo libre avec tag de quatre chiffres attribué automatiquement côté serveur, immuable pendant toute la vie du compte, jamais choisi ni généré dans le front. Afficher le tag pour information dans le profil ; le changement de pseudo ne transmet pas le tag. Garantir l'unicité de l'ensemble pseudo normalisé/tag ; refuser un renommage en conflit plutôt que changer le tag. Ne pas changer les UUID d'autorisation ni les pseudonymes invités.
- Impersonation administrateur (Jenn) : aucune limite de durée propre à la simulation ; arrêt manuel, avec maintien des contrôles de session, rôle et double authentification. Jenn autorise la consultation et les modifications réelles avec les droits du compte simulé ; conserver son identité admin comme auteur réel dans les journaux. Les opérations de sécurité du compte et les fonctions d’administration restent indisponibles en simulation.
- Secrets de partage dans les fragments frontend, transmis dans l'en-tête dédié, jamais dans les URLs serveur ou logs.
- Une liste publique et ses souhaits restent lisibles avec un lien valide lorsqu'une identité invitée facultative est expirée ou invalide. Ne pas créer d'identité ni de réservation à la lecture. Les réservations restent liées à leur participant vérifié ; seule une participation explicitement confirmée peut créer une nouvelle identité invitée, sans lui attribuer les anciennes réservations.

## Mises en production et données réelles

- Depuis le 1er novembre 2026, tout merge sur `main` déclenche automatiquement le déploiement sur le VPS existant après les contrôles CI et la sauvegarde cohérente, vérifiée et chiffrée hors du VPS, sans approbation manuelle ni interruption de service. Conserver l'environnement production limité à `main`, les protections de branche et les contrôles des révisions et artefacts ; `develop` reste une branche d'intégration. Aucun service payant supplémentaire.
- Les déploiements doivent être rapides et maintenir le service disponible : réutiliser les artefacts qualifiés, sauvegarder sans arrêter l'API ni Caddy, vérifier l'API candidate avant la bascule atomique et laisser finir les requêtes en cours. Ne pas recharger le proxy pendant les MEP courantes. Bloquer les changements SQL ou de contrat incompatibles jusqu'à une procédure compatible revue ; conserver les sauvegardes cohérentes, vérifiées et chiffrées hors du VPS.

- La production contient plusieurs utilisateurs réels. Pour chaque MEP, y compris frontend ou configuration, la sauvegarde préalable doit couvrir PostgreSQL et les fichiers persistants nécessaires à la restauration. Réaliser un nouveau snapshot, vérifier son intégrité et sa copie chiffrée hors du VPS, puis conserver sa référence dans le compte rendu de déploiement.
- Une sauvegarde quotidienne antérieure ou réalisée seulement après la MEP ne remplace pas cette sauvegarde préalable. Un échec ou une incertitude de transfert distant bloque aussi la MEP avant toute bascule ou migration.
- L'autorisation de remise à zéro donnée pour la première bascule est caduque ; toute nouvelle remise à zéro ou purge destructive des données réelles exige une nouvelle demande explicite et ciblée de Jenn.
- Jenn autorise les publications demandées avec les documents juridiques actuels encore en brouillon, en utilisant la procédure de publication temporaire et en laissant le site en ligne ; ne plus lui redemander cette confirmation. Cette autorisation ne vaut pas validation juridique : ne pas déclarer ces documents validés sans validation explicite et conserver les autres contrôles de production.

## Git et conventions

- Discord : Jenn demande la publication automatique des notes de version après les livraisons réussies, ainsi que du ZIP et du guide correspondant pour chaque nouvelle version de l’extension. Conserver les salons `releases` et `extension` en lecture seule pour les membres, les webhooks limités à leur salon et leurs URL dans les secrets privés. Mettre à jour `docs/discord-release-notes.md` avant chaque livraison ; ne jamais annoncer une simple création d’artefact comme une mise en ligne réussie. Voir [la procédure](docs/discord-publication.md).

- Commits, titres et descriptions de PR en anglais.
- Pour une règle de formatage, vérifier d'abord une option standard `.editorconfig` ; sinon documenter la règle dans le fichier AGENTS.md pertinent, sans conflit avec une règle existante.
- Si une préférence récurrente nécessite le choix de Jenn, poser une question courte avec deux exemples.

- Notifications mobiles, précision de Jenn le 4 novembre 2026 : Compte → Notifications présente un seul tableau des types avec exactement une colonne Dans MonKado et une colonne Notifications push, avec un interrupteur pour chaque ligne. Tous les types push sont activés par défaut ; conserver les choix désactivés en base indépendamment de la permission navigateur. Ne pas demander de nom d’appareil ni ajouter de colonnes ou de gestion d’appareils. L’autorisation navigateur est demandée lors du premier enregistrement d’un choix push sur l’appareil courant. Préciser l’ajout à l’écran d’accueil pour les push, obligatoire sur iPhone/iPad.

- Sécurité HTTP, demande de Jenn du 6 novembre 2026 : ne pas exposer la documentation API en production, y compris aux administrateurs ; la conserver en local et dans les futurs environnements hors production. HSTS de production inclut `includeSubDomains` pour l'API et le frontend.

- Préférence de Jenn du 6 novembre 2026 : maintenir et corriger les documents légaux lorsque demandé, sans les mentionner ni rappeler leur statut avant ou pendant une mise en production, y compris dans le récapitulatif de livraison et les demandes d'approbation. En parler seulement si Jenn le demande explicitement. Cette préférence ne vaut pas validation juridique et ne supprime pas les autres contrôles de déploiement.
