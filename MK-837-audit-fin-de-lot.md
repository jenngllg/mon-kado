# #837 — Audit de fin de lot Identité et sessions

Date de l'audit : 24 août 2026  
Branche auditée : `develop`  
Commit audité : `f4db05e0bae1fb3f4354c510507bba5b0d2d6e0d` (`[MK-830] Add processed authentication email retention cleanup`)

## Verdict

Le lot est **fonctionnellement complet dans le dépôt** et aucun défaut critique n'a été trouvé dans les mécanismes JWT, refresh token, antiforgery, mots de passe ou Google OpenID Connect.

Après correction sur la branche `codex/MK-837-end-of-lot-audit-fixes`, la quality gate locale est entièrement verte : 1 181 tests, 100 % des 7 173 lignes et des 1 505 branches couvertes, build Release sans avertissement, formatage, modèle EF Core, audits NuGet, scan Gitleaks des 79 commits et configurations Compose.

Toutes les corrections réalisables dans le dépôt ont été appliquées. Le lot n'est toutefois **pas encore prêt pour une mise en production réelle**, car des prérequis externes ne peuvent pas être prouvés par le dépôt : configuration des domaines et clients Google/Gmail de production, politique de confidentialité, sauvegardes et smoke tests sur l'environnement déployé.

Le lot suivant peut commencer sans dette technique connue issue de cet audit dans le dépôt.

## Résultat par US

| US | Résultat | Preuves principales |
|---|---|---|
| #762 Inscription par e-mail | Conforme | `202 Accepted`, validation, anti-énumération, hash Identity, rôle `Member`, compte et outbox atomiques, expiration à 30 jours |
| #763 Confirmation d'adresse | Conforme | lien à durée limitée, confirmation idempotente, renvoi borné et réponse temporisée anti-énumération |
| #764 Envoi des e-mails d'authentification | Conforme avec réserve | outbox transactionnelle, client Gmail OAuth typé, aucun secret dans le dépôt, retries explicites ; absence d'un maximum d'essais détaillée plus bas |
| #765 Connexion par mot de passe | Conforme, remplacée par #824 | lockout Identity, égalisation temporelle, erreurs structurées et session par navigateur |
| #824 JWT et refresh rotatif | Conforme | JWT minimal HS256, refresh secret 256 bits hashé, rotation verrouillée, détection de réutilisation, cookies sécurisés, antiforgery et CORS |
| #766 Session courante | Conforme | données relues dans PostgreSQL, rôles absents du JWT, membre supprimé retourné en `401` |
| #767 Déconnexion | Conforme | révocation du seul appareil courant, `204` même sans JWT ou avec JWT expiré, cookie supprimé, access token résiduel documenté |
| #768 Profil | Conforme | modification du seul `displayName`, `PUT 200`, ETag/xmin, `428` sans `If-Match`, `412` en concurrence |
| #825 Adresse e-mail | Conforme | mot de passe courant, confirmation sur la nouvelle adresse, notification sur l'ancienne, conflit `409`, sessions révoquées après confirmation |
| #826 Mot de passe | Conforme | vérification du mot de passe courant, renouvellement du security stamp, notification, révocation des refresh sessions |
| #769 Mot de passe oublié | Conforme | demande non énumérante, token borné, reset atomique, révocation des sessions et notification |
| #770 Google | Conforme | Authorization Code + PKCE/state/nonce, `prompt=select_account`, liaison autoritaire bornée, aucun token Google persisté, smoke test réel de développement réussi |
| #829 Commits PostgreSQL ambigus | Conforme | vérification après commit ambigu pour login et refresh, résultats gagnants réutilisables et scénarios concurrents testés |
| #830 Rétention des e-mails traités | Conforme | suppression quotidienne bornée, rétention configurable de 1 à 365 jours, index partiel `(processed_at, id)` |

## Écarts détectés puis corrigés

### 1. Corrigé — retries bornés de l'outbox Gmail

Résolution : politique injectée et validée, maximum configurable, transition terminale persistée et testée, puis rétention par le nettoyage existant.

`AuthenticationEmailOutboxMessage` incrémente bien `AttemptCount`, et `AuthenticationEmailDispatcher` calcule une prochaine tentative exponentielle plafonnée à 24 heures. Aucune condition ne termine toutefois un message après un nombre maximal d'essais.

Conséquences :

- une erreur permanente Gmail peut provoquer un retry toutes les six heures sans fin ;
- une notification de sécurité sans expiration métier peut rester indéfiniment en attente ;
- la rétention de #830 ne supprime que les messages traités et ne résout donc pas ce cas ;
- le comportement ne respecte pas la convention du dépôt qui exige un nombre maximal d'essais explicite.

Correction recommandée : rendre le maximum configurable et validé, ajouter un état terminal ou une dead-letter, conserver une catégorie d'échec non sensible, puis tester la transition terminale et la rétention associée.

Références :

- [`AuthenticationEmailDispatcher.cs`](src/Infrastructure.Persistence.PostgreSql/Services/AuthenticationEmailDispatcher.cs), notamment `GetRetryDelay` ;
- [`AuthenticationEmailOutboxMessage.cs`](src/Infrastructure.Persistence.PostgreSql/Entities/AuthenticationEmailOutboxMessage.cs), notamment `Claim` et `ScheduleRetry` ;
- [`AGENTS.md`](AGENTS.md), section « Providers externes ».

### 2. Corrigé — CorrelationId et TraceId des Workers

Résolution : une `Activity` W3C et un `CorrelationId` Guid v7 sont créés pour chaque passe logique de Worker, sans englober l'intervalle d'attente entre deux passes.

L'API crée correctement un scope contenant `CorrelationId` et `TraceId` pour chaque requête. Le Worker émet des logs structurés avec des `EventId`, mais ne crée aucun scope équivalent pour une livraison ou une passe de nettoyage.

Conséquences : les événements d'un traitement d'outbox ou d'un nettoyage ne peuvent pas être reliés de façon fiable entre eux dans ELK. Cela contrevient à la convention qui rend les deux identifiants obligatoires dans tous les logs.

Correction recommandée : créer une `Activity` et un `CorrelationId` Guid v7 par traitement logique, ouvrir un scope commun à la frontière de chaque worker et le propager pendant toute l'opération.

Références :

- [`CorrelationIdMiddleware.cs`](src/API/Middleware/CorrelationIdMiddleware.cs) ;
- [`Worker/Program.cs`](src/Worker/Program.cs) et les workers de [`src/Worker/Workers`](src/Worker/Workers) ;
- [`AGENTS.md`](AGENTS.md), section « Journalisation ».

### 3. Corrigé — logs métier des parcours d'authentification

Résolution : événements `LoggerMessage` de début et de succès pour l'inscription, la confirmation et sa relance, la connexion par mot de passe et le refresh, sans donnée sensible.

Les opérations session courante, logout, profil, changement d'e-mail, changement/reset du mot de passe et Google ont leurs événements applicatifs. L'inscription, la confirmation/relance d'e-mail, la connexion par mot de passe et le refresh n'ont pas d'événement métier de début ou de succès.

Le log HTTP permet de retrouver la requête, mais pas de distinguer proprement les résultats métier ni de construire des dashboards opérationnels cohérents sans dépendre des routes et statuts.

Correction recommandée : ajouter des événements `LoggerMessage` bornés, sans e-mail, mot de passe ni token, avec des `EventId` uniques. Conserver les erreurs HTTP à la frontière existante pour éviter le double log.

Références :

- [`ApplicationLogMessages.cs`](src/Application/Logging/ApplicationLogMessages.cs) ;
- [`RegisterAccountCommand.cs`](src/Application/Commands/RegisterAccountCommand.cs) ;
- [`LoginCommand.cs`](src/Application/Commands/LoginCommand.cs) ;
- [`RefreshSessionCommand.cs`](src/Application/Commands/RefreshSessionCommand.cs) ;
- [`ConfirmEmailCommand.cs`](src/Application/Commands/ConfirmEmailCommand.cs).

### 4. Corrigé — politiques opérationnelles Gmail/Worker configurables

Résolution : options immuables liées à la configuration, valeurs par défaut documentées, bornes et cohérence validées au démarrage, puis mapping Compose complet.

Le timeout Gmail, les tailles de batch, la durée de lease, les intervalles de polling et d'échec, ainsi que l'échelle de retry sont des constantes dans le code. Seule la rétention des messages traités est actuellement configurable et validée.

Cela complique l'exploitation et ne respecte pas la convention du dépôt qui exige des options immuables injectées avec `IOptions<T>` et validées au démarrage pour les timeouts et politiques de résilience externes.

Correction recommandée : regrouper ces valeurs dans des options Worker/Gmail fortement typées, avec bornes de validation et valeurs par défaut documentées. Cette correction peut être réalisée avec celle du maximum d'essais.

Références :

- [`GmailApiClient.cs`](src/Worker/Services/GmailApiClient.cs) ;
- [`AuthenticationEmailDeliveryWorker.cs`](src/Worker/Workers/AuthenticationEmailDeliveryWorker.cs) ;
- [`AuthenticationEmailDispatcher.cs`](src/Infrastructure.Persistence.PostgreSql/Services/AuthenticationEmailDispatcher.cs) ;
- les workers de nettoyage dans [`src/Worker/Workers`](src/Worker/Workers).

### 5. Corrigé — rollback sûr de `AddAuditableUtcDates`

Résolution : backfill de `updated_at`, suppression explicite du défaut résiduel lors de la remontée de migration et test du cycle down/up avec données.

La migration rend `users.updated_at` nullable dans `Up`. Son `Down` repasse directement la colonne en `NOT NULL`, sans convertir les lignes devenues `NULL`. Un rollback après création ou modification de données sous le nouveau modèle peut donc échouer.

La migration complète vers l'avant est testée, mais le test de rollback ne couvre pas ce cas de données. Sonar exclut volontairement les migrations de son analyse, ce qui explique que la quality gate reste verte.

Correction recommandée : définir explicitement la stratégie de rollback avant le `ALTER COLUMN` — backfill déterministe ou rollback déclaré non supporté — et ajouter un test de migration avec un utilisateur dont `updated_at` vaut `NULL`.

Référence : [`20260821115421_AddAuditableUtcDates.cs`](src/Infrastructure.Persistence.PostgreSql/Migrations/20260821115421_AddAuditableUtcDates.cs).

## Écarts secondaires corrigés

### OpenAPI incomplet sur quatre endpoints historiques

Résolution : schéma `ErrorResponse` ajouté aux réponses `413` et `415`, avec test du contrat OpenAPI.

Les réponses runtime `413` et `415` utilisent bien `ErrorResponse`, mais le schéma OpenAPI n'est pas déclaré pour :

- inscription ;
- confirmation d'e-mail ;
- demande d'une nouvelle confirmation ;
- connexion par mot de passe.

Les endpoints plus récents déclarent correctement `typeof(ErrorResponse)`. Les tests OpenAPI historiques vérifient surtout antiforgery et les champs de requête, pas ces schémas d'erreur.

Références :

- [`AuthRegistrationsController.cs`](src/API/Controllers/AuthRegistrationsController.cs) ;
- [`AuthSessionsController.cs`](src/API/Controllers/AuthSessionsController.cs) ;
- [`EmailConfirmationsController.cs`](src/API/Controllers/EmailConfirmationsController.cs).

### Documentation obsolète autour de #764

Résolution : README aligné sur l'implémentation actuelle et sur les nouveaux chemins `src/Tools.GmailOAuthBootstrap` et `deployments/caddy`.

Le README parle encore de #764 au futur alors que l'envoi fiable est implémenté. Ces passages doivent décrire l'état actuel sans référence à une future tâche.

Référence : [`README.md`](README.md), sections inscription et confirmation d'adresse.

### Secret scanning non démontré par la CI du dépôt

Résolution : scan Gitleaks de l'historique complet, action épinglée par SHA et allowlist limitée aux deux valeurs déterministes utilisées comme vecteurs de test.

La revue n'a trouvé aucun secret correspondant aux formats courants dans les fichiers suivis. Les secrets de production sont correctement attendus dans l'environnement ou le `.env` non suivi, et les workflows ont des permissions minimales.

La CI audite les dépendances, exécute CodeQL et scanne les images avec Trivy, mais aucun job versionné ne démontre un scan de secrets de l'historique ou du diff. Activer GitHub Secret Scanning si disponible, ou ajouter un outil spécialisé tel que Gitleaks, renforcerait la barrière préventive.

### Petites dettes de dépôt

Résolution : constante morte retirée, premier test Domain ajouté et structure de dépôt harmonisée.

- `TechnicalInternalServerError` n'est pas utilisé, l'erreur inattendue retournant volontairement un `errorCode` nul ;
- `Domain.UnitTests` est vide et produit « No test is available » dans la CI ; ce n'est pas un échec tant que le domaine ne contient pas de logique testable ;
- le renommage futur de `deploy/` vers `deployments/` et le rangement du bootstrap Gmail sous `src` restent des décisions de structure déjà identifiées, non bloquantes pour ce lot.

## Sécurité vérifiée

- JWT HS256 avec clé Base64 d'au moins 256 bits, issuer/audience obligatoires, durée 15 minutes et clock skew 30 secondes ;
- claims limités à `sub`, `jti`, `iat`, `exp`, `iss` et `aud` ; aucune adresse e-mail, nom, rôle ou permission ;
- JWT non persisté et non journalisé ;
- refresh token avec identifiant de session et secret aléatoire 256 bits ; seul SHA-256 est stocké et la comparaison est en temps constant ;
- rotation sous verrou PostgreSQL, révocation sur ancien token réutilisé, sessions indépendantes par appareil ;
- gestion vérifiée des acquittements de commit PostgreSQL ambigus ;
- cookies host-only, `HttpOnly`, `Path=/`, `SameSite=Strict`, `Secure` en production, sans domaine ;
- cookie persistant uniquement pour `rememberMe`, avec échéance absolue à 30 jours ;
- antiforgery sur toutes les opérations qui reposent sur un cookie navigateur ; Bearer seul exempté ;
- CORS limité à des origines exactes, HTTPS obligatoire en production et en-tête `Authorization` autorisé ;
- erreurs et réponses de token en `Cache-Control: no-store` ;
- Google OIDC avec Authorization Code, PKCE S256, state, nonce, correlation, issuer/audience/signature/lifetime, `SaveTokens=false`, sans scope Gmail ni offline access ;
- aucun code ou token Google, JWT, refresh token, e-mail, display name ou `sub` dans les logs applicatifs ;
- secrets absents des fichiers `appsettings` et placeholders seulement dans `.env.example` ;
- conteneurs API/Worker non-root, read-only, capabilities supprimées et ports API/PostgreSQL privés.

## Risques résiduels acceptés et documentés

- après logout, changement d'e-mail, changement ou reset du mot de passe, un JWT déjà émis reste cryptographiquement valide pendant au plus 15 minutes ; les refresh sessions sont révoquées ;
- Gmail est livré « at least once » : une duplication rare reste possible si Gmail accepte le message mais que son accusé de réception ou le commit local est perdu ;
- `SameSite=Strict` impose que le frontend et l'API de production partagent le même domaine enregistrable ; GitHub Pages sans domaine personnalisé n'est pas compatible ;
- les tests réels Google/Gmail restent manuels et exclus de la CI afin de ne pas exposer de secrets ni dépendre d'un provider externe.

## Preuves de quality gate

- [CI develop — succès](https://github.com/jenngllg/mon-kado/actions/runs/32751945834) : 1 150 tests, 0 échec, 6 988/6 988 lignes et 1 463/1 463 branches ; SonarQube Cloud `PASSED` ; aucun package vulnérable ou déprécié ; formatage, build, migrations, modèle EF et Compose validés.
- [Containers develop — succès](https://github.com/jenngllg/mon-kado/actions/runs/32751945791) : images API/Worker, stack isolée, migrations idempotentes, Data Protection persistante, comportement PostgreSQL indisponible et scans Trivy.
- [CodeQL develop — succès](https://github.com/jenngllg/mon-kado/actions/runs/32751945156).
- [MR #37 Google — smoke test réel de développement](https://github.com/jenngllg/mon-kado/pull/37) : challenge, callback, refresh, session courante, reconnexion, persistance, logout, annulation et absence de fuite vérifiés avec le vrai provider.

## Checklist avant production

### Corrections du dépôt

- [x] borner les retries de l'outbox et définir le traitement terminal ;
- [x] configurer et valider les timeouts, batches et délais Worker/Gmail ;
- [x] ajouter les scopes `CorrelationId`/`TraceId` du Worker ;
- [x] compléter les logs métier des premiers parcours d'authentification ;
- [x] corriger les schémas OpenAPI `413`/`415` ;
- [x] sécuriser et tester le rollback `updated_at` ;
- [x] actualiser les passages README relatifs à #764 ;
- [x] automatiser le scan de secrets sur l'historique Git.

### Environnement réel

- [ ] déployer frontend et API HTTPS sous un même domaine enregistrable ;
- [ ] configurer les origines, hosts, proxy connu, clé JWT et permissions du `.env` ;
- [ ] créer/configurer le client Google Web de production avec l'URI de callback exacte ;
- [ ] configurer le client Gmail dédié, publier l'écran de consentement si nécessaire et stocker le refresh token en secret ;
- [ ] publier la politique de confidentialité et les informations cookies couvrant Google, Gmail, durées de conservation et sous-traitants ;
- [ ] définir la purge opérationnelle des messages dans la boîte Gmail dédiée ;
- [ ] sauvegarder PostgreSQL et le volume Data Protection séparément, puis tester une restauration ;
- [ ] exécuter les smoke tests production Google, e-mails, login/refresh/logout, changement et reset de mot de passe ;
- [ ] mettre en place la centralisation des logs, dashboards et alertes dans le lot Déploiement/exploitation.

## Conclusion

Le cœur Identité et sessions est solide, cohérent et entièrement couvert. Aucun remaniement d'architecture JWT/refresh/Google n'est nécessaire et tous les écarts de dépôt détectés pendant l'audit sont corrigés. Le passage en production dépend désormais uniquement de la checklist externe exécutée sur l'environnement réel.
