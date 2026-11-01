# #838 — Audit de fin de lot : listes de souhaits

Date de l'audit : 25 août 2026  
Branche : `codex/MK-838-wishlist-lot-audit`  
Révision `develop` auditée : `3a4a7f4e88ab0ecfbd44316b47f65e2e130509f1`  
Périmètre : #771 à #775 et leurs dépendances directes.

## Verdict

Le lot de gestion des listes privées est fonctionnel, cohérent avec les conventions du dépôt et prêt à être proposé en merge request après application des corrections de cet audit. Aucun constat actionnable connu ne reste ouvert dans le périmètre audité.

Les contrôles couvrent les contrats HTTP, l'authentification, l'autorisation propriétaire, la non-divulgation des ressources privées, la validation, la concurrence optimiste, PostgreSQL, les journaux, OpenAPI, les conteneurs et la quality gate.

## Traçabilité fonctionnelle

| Tâche | Fonctionnalité | État après audit |
|---|---|---|
| #771 | Créer une liste de souhaits | Conforme après sécurisation des commits PostgreSQL ambigus |
| #772 | Consulter mes listes de souhaits | Conforme |
| #773 | Consulter une de mes listes de souhaits | Conforme ; la fonctionnalité a été livrée avec #771 |
| #774 | Modifier une liste de souhaits | Conforme après sécurisation des commits PostgreSQL ambigus |
| #775 | Supprimer une liste de souhaits | Conforme après sécurisation des commits PostgreSQL ambigus |

Le regroupement technique de #773 dans la livraison de #771 est un point de traçabilité, pas un défaut de code.

## Constats corrigés

### 1. Résultat ambigu après une écriture PostgreSQL

La création, la modification et la suppression pouvaient retourner `503 Service Unavailable` lorsque PostgreSQL avait réellement validé la transaction mais que l'accusé de réception du commit avait été perdu. Un rejeu aveugle aurait créé des conflits ou des réponses incorrectes.

Le service relit désormais l'état persistant après une erreur transitoire survenue pendant la sauvegarde :

- une création est confirmée uniquement lorsque la ressource retrouvée correspond exactement à la tentative ;
- une modification est confirmée uniquement lorsque l'état demandé est réellement persisté ;
- un état resté identique après une modification ambiguë retourne `503`, car le commit n'est pas confirmé ;
- un troisième état, produit par une modification concurrente, retourne `412 Precondition Failed` ;
- une suppression est confirmée lorsque la ressource n'est plus accessible au propriétaire ;
- la disparition du membre conserve la réponse `401 Unauthorized` attendue ;
- une nouvelle indisponibilité pendant la vérification reste traduite en `503` ;
- aucune écriture ambiguë n'est rejouée automatiquement.

Des tests unitaires couvrent chaque branche de décision. Trois tests d'intégration utilisent un interceptor transactionnel qui déclenche une erreur après le commit réel afin de vérifier la création unique, la modification persistée et la suppression effective.

### 2. Tests JWT dépendants de l'heure réelle

Les tests d'intégration Wishlist créaient leurs JWT avec l'horloge métier fixe du 25 août 2026 à 10:00 UTC. Une fois cette heure dépassée de plus de quinze minutes, tous les JWT du groupe devenaient expirés et les tests retournaient `401`.

Les clients d'intégration émettent désormais leurs jetons techniques avec une horloge adaptée à l'authentification, sans modifier l'horloge métier fixe utilisée pour vérifier les dates et les champs d'audit. Les 23 scénarios PostgreSQL Wishlist restent ainsi exécutables après l'heure de référence métier.

### 3. Messages de validation dupliqués

Les messages relatifs au nom, à l'occasion et au message d'une liste étaient répétés dans les validateurs de création et de modification. Ils sont maintenant centralisés dans `ValidationMessages`, conformément aux conventions du dépôt.

### 4. Documentation générale incomplète

Le README ne recensait pas les routes Wishlist. Les cinq opérations `GET`, `POST`, `GET /{wishlistId}`, `PUT /{wishlistId}` et `DELETE /{wishlistId}` y sont maintenant décrites, avec les usages de `ETag` et `If-Match`.

### 5. Documentation interne du service

Les méthodes privées de rapprochement PostgreSQL et les helpers existants du service Wishlist possèdent maintenant leur documentation XML en anglais, conformément à la règle appliquée aux services et à leurs méthodes.

## Contrats HTTP et sécurité

- Les routes sont versionnées sous `/api/v1/wishlists`.
- La création retourne `201 Created`, le DTO complet, un en-tête `Location` et un `ETag` fort.
- La lecture individuelle retourne le DTO complet et son `ETag`.
- La modification retourne `200 OK`, le DTO mis à jour et le nouvel `ETag`.
- La suppression retourne `204 No Content`.
- `If-Match` est obligatoire pour les modifications et suppressions : absence `428`, format invalide `400`, version périmée `412`.
- Les opérations nécessitent un Bearer JWT valide.
- L'autorisation propriétaire est centralisée dans une politique nommée et un `AuthorizationHandler`.
- Une liste absente ou appartenant à un autre membre retourne `404`, sans révéler son existence.
- Un membre supprimé après émission du JWT reçoit `401`.
- Les opérations mutantes conservent la protection antiforgery attendue.
- Les réponses métier utilisent `ErrorResponse` et des `ErrorCode` stables.
- Les réponses sont `no-store` et les tailles ainsi que les types des requêtes sont contraints.
- OpenAPI documente les corps, statuts, ETag et préconditions.

## Persistance et concurrence

- Les identifiants sont des GUID v7 générés dans l'application.
- Le propriétaire est protégé par une clé étrangère PostgreSQL avec suppression en cascade.
- Le nom normalisé est unique par propriétaire.
- Les contraintes PostgreSQL valident les bornes de longueur et les valeurs d'occasion.
- `CreatedAt` et `UpdatedAt` sont alimentés par l'interceptor d'audit avec `TimeProvider`.
- La version repose sur `xmin` et est exposée comme `ETag` fort.
- Les lectures ordinaires utilisent `AsNoTracking` ; les écritures relisent une entité suivie.
- Les suppressions sont physiques, conformément au besoin actuel.
- Les commits ambigus sont rapprochés par lecture de l'état réel, sans retry d'écriture.

## Journalisation et exploitation

- Les événements Wishlist utilisent des méthodes `LoggerMessage` et des `EventId` réservés dans la plage du domaine.
- Les succès métier et les lectures réussies sont journalisés au niveau attendu.
- Les erreurs métier sont journalisées sans dupliquer les exceptions techniques.
- Aucun nom de liste, message, adresse e-mail, jeton ou secret n'est journalisé.
- `CorrelationId` et `TraceId` restent ajoutés par le scope HTTP commun.
- Les configurations Compose locale et production ont été validées.
- Un démarrage isolé de la pile API, Worker, migrations, PostgreSQL et Caddy a été vérifié.
- La migration bundle est idempotente et la disponibilité réagit comme attendu à l'arrêt de PostgreSQL : liveness `200`, readiness `503`.
- Les images temporaires et l'environnement isolé créés pour l'audit ont été supprimés après vérification ; aucune donnée persistante du projet n'a été touchée.

## Quality gate exécutée

- Build Release : réussi, 0 warning, 0 erreur.
- `dotnet format --verify-no-changes` : réussi.
- `git diff --check` : réussi.
- Tests : 1 394 réussis, 0 échec, 0 ignoré.
- Couverture : 8 046 / 8 046 lignes, soit 100 %.
- Couverture : 1 695 / 1 695 branches, soit 100 %.
- Migrations EF Core : aucune différence entre le modèle et la dernière migration.
- Test de rollback/recréation de la migration Wishlist : réussi.
- Audit NuGet des packages vulnérables : aucun package vulnérable.
- Audit NuGet des packages dépréciés : aucun package déprécié.
- Gitleaks : aucun secret détecté sur les 78 commits analysés.
- Trivy sur les images API et Worker : 0 vulnérabilité corrigible `HIGH` ou `CRITICAL`.

Les mises à jour majeures signalées par la commande des packages obsolètes ne constituent pas une vulnérabilité du lot : MediatR 14, Microsoft.OpenApi 3 et xUnit 4 nécessitent des migrations dédiées. Testcontainers 4.14 peut être traité dans une maintenance de dépendances séparée.

## État de la CI de référence

Les workflows du commit `develop` audité étaient verts avant les corrections locales :

- CI : <https://github.com/jenngllg/mon-kado/actions/runs/32832779825>
- Containers : <https://github.com/jenngllg/mon-kado/actions/runs/32832779914>
- CodeQL sur push : <https://github.com/jenngllg/mon-kado/actions/runs/32832778787>
- CodeQL planifié : <https://github.com/jenngllg/mon-kado/actions/runs/32833743535>

Une nouvelle CI devra valider les corrections de #838 sur la merge request.

## Risques acceptés et hors périmètre

- La collection des listes n'est pas paginée. Le volume attendu reste faible et la convention du dépôt réserve la pagination aux collections potentiellement volumineuses, notamment les souhaits d'une liste.
- Les souhaits, le partage, les participants et les réservations appartiennent aux lots suivants.
- Les opérations de production et les providers externes ne sont pas modifiés par ce lot.
- Le rapport d'audit reste volontairement local et ne doit pas être inclus dans le commit ni dans la merge request.
