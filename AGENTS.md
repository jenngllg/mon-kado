# Instructions du dépôt MonKado

## Technologie et organisation

- Le backend est en Go. Exécuter PostgreSQL, API, worker, outils et tests nativement ; utiliser Docker uniquement si un besoin concret le rend nécessaire et expliquer ce besoin.
- Conserver les contrats HTTP/JSON, règles d'accès et invariants métier lors d'un changement technique.
- Garder `GmailOAuthBootstrap` dans `src/Tools.GmailOAuthBootstrap` comme module autonome, sans déplacement sans demande explicite de Jenn. Les outils autonomes appelant un provider externe restent sous `src/`.
- Préserver les audits historiques, prototypes et outils locaux .NET de gestion de disque existants ; ils ne sont pas nécessaires au backend Go.
- Organiser les packages par responsabilité technique. Garder les composition roots `cmd/*` concises et injecter explicitement leurs dépendances.
- Utiliser `gofmt` et `go vet`. Préférer les sorties rapides et les opérations lisibles, sans imbrication inutile.
- Documenter en anglais les packages, types/interfaces exportés et leurs fonctions avec GoDoc. Les commentaires internes expliquent le pourquoi ; tout TODO référence un ticket.
- Définir les interfaces du côté consommateur pour substituer les dépendances externes. Ne pas créer d'abstractions de persistance triviales sans besoin.
- Les E/S acceptent `context.Context` en premier paramètre et le transmettent sans substitution. Ne pas créer de contexte de fond dans une opération métier.
- Injecter l'horloge dans les services temporels ; manipuler exclusivement l'UTC. Nommer les constantes métier répétées.
- Centraliser la validation du pipeline, agréger toutes les erreurs et conserver les chemins JSON camelCase complets, avec indices. Ne pas répéter les validations de présence/format dans les services.
- Distinguer avec des pointeurs les valeurs client absentes lorsque nécessaire. Ne pas partager de DTO entrants mutables entre requêtes.
- Centraliser les messages de validation récurrents en anglais et les codes métier dans `internal/domain/error_codes.go`.

## API HTTP

- Version dans l'URL `/api/v1`, ressources au pluriel, parent conservé pour les enfants dépendants ; verbes dans la route seulement pour les actions métier.
- Création : 201, Location et DTO complet. Modification : 200 et DTO complet. Suppression/commande sans résultat utile : 204 sans corps.
- Ressource absente, y compris DELETE : 404. Conflit métier : 409. Non authentifié : 401. Authentifié sans droit sur une ressource divulguable : 403.
- Une ressource privée étrangère retourne 404 ; centraliser l'autorisation et ne jamais considérer un UUID comme une autorisation.
- Administration : rôle vivant en base et session vérifiée avec la double authentification courante. Révocation/suppression des comptes auditées atomiquement.
- `ErrorResponse` contient exactement `statusCode`, `title`, `message`, `errorCode`, `validationErrors`. Chaque ValidationError contient exactement `propertyName` et `errorMessage`.
- Title/Message en anglais ; chaque erreur métier prévisible possède un code stable en UPPER_SNAKE_CASE préfixé par domaine. Le front traduit à partir de ce code.
- Identifiants membres : pseudo libre avec tag de quatre chiffres attribué automatiquement côté serveur, immuable pendant toute la vie du compte, jamais choisi ni généré dans le front. Afficher le tag pour information dans le profil ; le changement de pseudo ne transmet pas le tag. Garantir l'unicité de l'ensemble pseudo normalisé/tag ; refuser un renommage en conflit plutôt que changer le tag. Ne pas changer les UUID d'autorisation ni les pseudonymes invités.
- JSON camelCase ; propriétés nullables incluses même à null. Enums en chaînes camelCase ; rejeter nombres et valeurs inconnues avant validation métier.
- Timestamps UTC sans suffixe Utc ; dates calendaires yyyy-MM-dd. Ne pas convertir les décimales monétaires en flottants binaires.
- Centraliser les exceptions à la frontière HTTP et documenter statuts, schémas et autorisations dans OpenAPI. Vérifier les contrats avec des assertions ciblées sans snapshots globaux.
- Conserver CSRF, allowlists d'hôtes/origines et cookies de production __Host-, Secure, HttpOnly, SameSite. Faire confiance aux headers de proxy uniquement pour les pairs déclarés dans TRUSTED_PROXIES.
- Secrets de partage dans les fragments frontend, transmis dans l'en-tête dédié, jamais dans les URLs serveur ou logs.

## Persistance et fichiers

- PostgreSQL natif, pool borné, UUID v7 générés dans l'application, suppression physique par défaut ; anonymisation ciblée distincte pour les historiques conservés.
- CreatedAt à la création ; UpdatedAt null jusqu'à la première modification lorsque le contrat de l'entité le prévoit.
- Concurrence seulement pour les entités concernées : xmin exposé comme ETag fort, If-Match obligatoire ; 428 absent, 400 malformé, 412 périmé.
- Rechercher les enfants avec leur identifiant et celui du parent. Contraintes/transactions/verrouillages PostgreSQL garantissent notamment unicité et quantité des réservations.
- Toutes les mutations et outboxes d'une opération sont atomiques ; aucun commit caché dans un repository trivial.
- Migrations SQL explicites sous verrou PostgreSQL avant déploiement ; jamais automatiquement au démarrage API.
- Pagination lorsque nécessaire : page >= 1, pageSize 1..100, défauts 1 et 20 seulement si absents, 400 pour une valeur fournie invalide.
- PaginatedResponse[T] : items, currentPage, pageSize, totalCount, totalPages, hasPreviousPage, hasNextPage. Vide : tableau vide, zéro pages et deux indicateurs false. Page au-delà du total : 200, numéro demandé conservé, métadonnées réelles.
- Tri explicite lorsque le contrat le nécessite. Réconcilier les fichiers en attente avec les références vivantes avant suppression.
- Un échec d'accusé de commit ne prouve pas un rollback : conserver le fichier jusqu'à vérification sûre.

## Providers, worker et secrets

- Google OAuth, Gmail et sites marchands sont des providers externes, encapsulés dans des clients typés. Confiner les types SDK à l'infrastructure.
- Centraliser les budgets HTTP et les stratégies de résilience. Retry seulement pour une opération idempotente après incident transitoire, jamais une erreur client non transitoire.
- Gmail users.messages.send est un POST sans clé d'idempotence : aucun retry transparent. L'outbox pilote chaque tentative, compteur, échéance, plafond et délai exponentiel.
- Leases avec FOR UPDATE SKIP LOCKED, renouvellement des exports, identité de lease vérifiée à la finalisation et cycles bornés. Limites/délais configurables et validés au démarrage.
- Purger les données protégées expirées même si le provider est désactivé ; garder des états terminaux traçables sans exposer de secrets.
- Imports : refuser les IP privées/spéciales, DNS mixtes et ports interdits, vérifier chaque redirection, épingler l'IP validée et ignorer les proxies de processus.
- Borner corps HTTP, octets/pixels des images, concurrence, tailles des ZIP ; corriger l'orientation et supprimer les métadonnées d'images.
- Exports : snapshot cohérent, stockage privé, téléchargement autorisé, expiration/nettoyage. Exclure credentials, jetons, codes de récupération, secrets de partage et réservations privées d'autres participants.
- Aucun secret versionné : environnement, gestionnaire de secrets ou `.local/environment.json` ignoré en local.

## Journalisation

- Logs structurés, modèles stables et propriétés nommées ; EventId constants uniques avec plages par domaine.
- Inclure CorrelationId et TraceId W3C dans les logs de requêtes/jobs ; retourner X-Correlation-ID et conserver une valeur client valide, sinon en générer une.
- Opérations métier réussies, y compris GET, au niveau Information. Erreurs HTTP métier au niveau Error sans stack trace. Exceptions inattendues une seule fois à la frontière avec stack trace.
- Liste blanche : identifiants techniques nécessaires ; jamais mots de passe, jetons, secrets, emails, corps de requête ni contenu des souhaits. Les erreurs CLI ne dévoilent ni URL de connexion ni réponse brute provider.

## Tests et qualité

- Standard testing, suite par type/composant, noms TestMethod_WhenCondition_ExpectedResult, sections Arrange/Act/Assert.
- Tests tabulaires pour mêmes comportements/valeurs différentes ; helpers simples par scénario, valeurs déclenchant le comportement explicites.
- Mocker uniquement les dépendances externes/à effets de bord ; vrais validateurs/value objects déterministes. Vérifier appels attendus, absence d'appels supplémentaires et propagation du contexte.
- Vrai PostgreSQL natif en intégration, fixture centralisée migrations/reset, tests séquentiels quand la base est partagée ; aucune dépendance Testcontainers.
- Vérifier la persistance par contrats publics ; accès direct à la base seulement pour un invariant technique non observable (hash, outbox, lease, contrainte).
- Providers remplacés par des fakes mémoire, aucun réseau réel dans la quality gate ; appels réels uniquement explicitement activés via configuration privée/workflow manuel sécurisé.
- Tester les transports avec réponses simulées. Vérifier statuts et schémas JSON/OpenAPI explicitement, sans snapshot global.
- Méthodes privées testées via contrats publics, sans réflexion ni modification de visibilité pour tester.
- Conserver l'objectif initial de couverture 100% lignes/branches ; seuls code généré et types descriptifs sans logique sont exclus. Toute autre exclusion/réduction nécessite un cas concret documenté.
- Exception documentée pour la migration Go dans README.md, « Coverage exception for the Go migration » : minimum automatisé de 80% des statements dans chacun des deux modules, sans exclusion de code exécutable. Les retours défensifs sur fichiers embarqués immuables, sérialisation de types JSON connus et paramètres cryptographiques fixes ne justifient pas de modifier les invariants de production pour les rendre atteignables ; les autres lacunes restent également mesurées. Conserver l'objectif de 100% pour les comportements atteignables et les scénarios explicites pour les branches critiques.
- Go mesure les statements ; compléter les branches importantes avec des scénarios explicites et ne pas présenter ce rapport comme une mesure de branches.
- Aucun retry masquant l'instabilité CI. Horloge contrôlée, signaux/canaux déterministes, pas de sleeps/polling arbitraires ; t.Context() transmis aux E/S. Race detector sur Linux natif.

## Git et conventions

- Commits, titres et descriptions de PR en anglais. Ne pas versionner de plans d'implémentation ; garder *-implementation-plan.md parmi les fichiers locaux ignorés.
- Préserver le travail utilisateur et ne pas reformater de fichiers sans rapport.
- Pour une règle de Jenn, vérifier d'abord une option standard .editorconfig ; sinon documenter ici sans conflit avec une règle existante.
- Ne pas interrompre le travail pour des détails sans conséquence. Si une préférence récurrente exige son choix, une question courte avec deux exemples suffit ; conserver sa réponse ici.
- Un signalement de bug par Jenn vaut demande de correction : diagnostiquer, corriger et vérifier dans le périmètre concerné sans redemander si elle souhaite le correctif. Demander uniquement une information ou une autorisation réellement manquante.
