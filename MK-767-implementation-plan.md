# [MK-767] Me déconnecter

## Résumé

Ajouter un endpoint idempotent qui révoque uniquement la refresh session du navigateur courant et supprime son refresh cookie.

Le JWT d'accès ne peut pas être révoqué : le front devra le supprimer de sa mémoire après le logout. Il expirera naturellement après 15 minutes.

## Contrat HTTP

Ajouter :

```http
DELETE /api/v1/auth/sessions/current
X-CSRF-TOKEN: <token>
Cookie: MonKado.Refresh=<refresh-token>
```

Réponses :

- `204 No Content` après une déconnexion réussie ;
- `204` également si le cookie est absent, invalide, expiré, déjà révoqué ou ne correspond plus à une session ;
- `400 Bad Request` si l'antiforgery est absent ou invalide ;
- `429 Too Many Requests` en cas de dépassement de la limite ;
- `503 Service Unavailable` lorsque PostgreSQL est indisponible.

Toujours ajouter `Cache-Control: no-store`.

Après un `204`, supprimer le refresh cookie. Après un `503`, le conserver afin que le front puisse réessayer.

## Couche API

Ajouter `LogoutAsync` dans `AuthSessionsController` :

- `[HttpDelete("current")]` ;
- aucun body ;
- `[ValidateAntiForgeryToken]` ;
- `[RefreshTokenCookie]` pour OpenAPI ;
- réutiliser la politique de rate limiting du refresh ;
- lire le refresh token avec `IRefreshTokenCookieService` ;
- envoyer `LogoutCommand` ;
- supprimer le cookie uniquement après le succès de la commande ;
- retourner `NoContent()`.

L'action retournera un `Task<IActionResult>` puisqu'il n'y a aucun DTO de succès.

## Couche Application

Ajouter dans un même fichier :

- `LogoutCommand` ;
- `LogoutCommandHandler`.

Étendre `IAccountSessionService` avec :

```csharp
Task LogoutAsync(
    string? refreshToken,
    CancellationToken cancellationToken);
```

Le token reste nullable : l'absence ou un format incorrect représente une session déjà déconnectée, pas une erreur de validation. Aucun validateur FluentValidation n'est donc nécessaire pour cette commande idempotente.

Ajouter des logs structurés :

- `Debug` au début du traitement ;
- `Information` lorsque le logout est terminé ;
- ne jamais journaliser le refresh token, son hash, l'e-mail ou les rôles.

## Persistance

Implémenter `LogoutAsync` dans `AccountSessionService` :

1. accepter immédiatement un cookie absent ou mal formé ;
2. extraire l'identifiant de session du refresh token ;
3. ouvrir une transaction avec la stratégie d'exécution EF Core ;
4. verrouiller la session avec `GetByIdForUpdateAsync()` ;
5. accepter une session inexistante ou déjà révoquée ;
6. vérifier le hash en temps constant ;
7. révoquer la session identifiée ;
8. appeler une seule fois `SaveChangesAsync()` si son état a changé ;
9. valider la transaction.

Un ancien token réutilisé ou un token altéré contenant l'identifiant d'une session existante révoquera cette session, comme le mécanisme de détection de réutilisation du refresh.

Une indisponibilité PostgreSQL sera traduite en `DependencyUnavailableException`.

Aucune migration n'est nécessaire. Les sessions révoquées seront conservées jusqu'à leur expiration, puis supprimées par le Worker existant.

## Comportement multi-appareils

Le logout révoque uniquement la session identifiée par le cookie du navigateur courant.

Les refresh sessions des autres appareils restent actives. La déconnexion de tous les appareils reste hors périmètre.

## Tests

### Tests unitaires

- transmission du refresh token et du `CancellationToken` par le handler ;
- cookie absent ou format invalide ;
- session inexistante ou déjà révoquée ;
- révocation d'une session valide ;
- détection d'un token altéré ou réutilisé ;
- traduction d'une indisponibilité PostgreSQL ;
- logs sans token ni données personnelles ;
- mocks stricts et vérifications explicites.

### Tests fonctionnels

- `204` sans body ;
- suppression correcte du cookie local et du cookie `__Host-` en production ;
- comportement idempotent sans cookie ou avec un cookie invalide ;
- cookie conservé après un `503` ;
- antiforgery obligatoire ;
- rate limiting et réponse `429` structurée ;
- `Cache-Control: no-store` ;
- contrat OpenAPI exact ;
- aucune fuite du refresh token dans les réponses ou les logs.

### Tests d'intégration

- révocation effective en PostgreSQL ;
- impossibilité de rafraîchir après le logout ;
- autres appareils toujours actifs ;
- logout répété idempotent ;
- session expirée ou déjà révoquée ;
- membre supprimé ;
- concurrence entre refresh et logout avec verrouillage ;
- JWT déjà émis encore techniquement valide jusqu'à son expiration.

## Quality gate et Git

- Branche : `codex/MK-767-logout`.
- Commit et PR : `[MK-767] Add current session logout`.
- 100 % de couverture lignes et branches.
- Quality gate complète, migrations EF, audits NuGet et Compose.
- Revue complète puis squash merge dans `develop`.

## Hors périmètre

- révocation immédiate des JWT d'accès ;
- blacklist de JWT ;
- déconnexion de tous les appareils ;
- liste et administration des sessions ;
- modification du profil.

## Décision retenue

Le logout reste possible lorsque le JWT d'accès est absent ou expiré, avec uniquement le refresh cookie et l'antiforgery. L'endpoint ne requiert donc aucune authentification Bearer. Cela évite qu'un utilisateur dont le JWT vient d'expirer ne puisse plus se déconnecter proprement.
