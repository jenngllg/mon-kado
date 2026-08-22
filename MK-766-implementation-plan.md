# [MK-766] Connaître ma session courante

## Résumé

Ajouter un endpoint authentifié permettant au front de récupérer les informations du membre courant depuis PostgreSQL.

Le JWT reste strictement minimal. L'identifiant est lu depuis le claim `sub`, tandis que l'adresse e-mail, le nom affiché et les rôles sont systématiquement chargés depuis la base de données.

## Contrat HTTP

Ajouter :

```http
GET /api/v1/auth/sessions/current
Authorization: Bearer <access-token>
```

Retourner `200 OK` :

```json
{
  "id": "0198d027-51c0-7000-8000-000000000001",
  "email": "jenn@example.fr",
  "displayName": "Jenn",
  "roles": [
    "Member"
  ]
}
```

- Retourner une classe immuable `CurrentSessionResponse`.
- Trier les rôles par ordre alphabétique afin de garantir une réponse déterministe.
- Ne pas retourner `expiresAt` : l'expiration est déjà portée par le claim `exp` du JWT.
- Ne retourner aucun nouveau token et ne pas renouveler le refresh token.
- Ajouter `Cache-Control: no-store`.
- Ne pas imposer l'antiforgery sur ce `GET`.
- Ne jamais ajouter l'adresse e-mail, le nom affiché ou les rôles au JWT.

## Authentification

- Protéger l'action avec une politique d'autorisation nommée exigeant un Bearer authentifié.
- Lire exclusivement l'identifiant du membre depuis le claim `sub`.
- Refuser avec `401 Unauthorized` un `sub` absent, vide ou qui ne représente pas un `Guid` valide.
- Ne jamais accepter l'identifiant du membre depuis la route, la query string ou le body.
- Ne pas convertir les rôles PostgreSQL en claims dans le `ClaimsPrincipal`.
- Les futures politiques métier continueront à charger les rôles et permissions depuis PostgreSQL au moyen d'authorization handlers dédiés.

## Couche Application

Ajouter :

- `GetCurrentSessionQuery` et son handler dans le même fichier ;
- `GetCurrentSessionQueryValidator` ;
- une classe immuable `CurrentSession` ;
- `ICurrentSessionService` dans `Application/Abstractions`.

Le handler devra :

1. recevoir l'identifiant provenant du claim `sub` ;
2. appeler `ICurrentSessionService` ;
3. retourner la session courante ;
4. lever `InvalidAuthenticationSessionException` si le membre n'existe plus.

La validation de la query devra utiliser FluentValidation et produire une erreur d'authentification, et non une erreur métier `400`, lorsque l'identifiant issu du JWT est invalide.

## Persistance

Implémenter `CurrentSessionService` dans `Infrastructure.Persistence.PostgreSql/Services`.

- Utiliser une lecture `AsNoTracking()`.
- Projeter uniquement `Id`, `Email`, `DisplayName` et les noms des rôles.
- Charger les rôles depuis `users`, `user_roles` et `roles`.
- Placer la requête EF Core dans un repository spécialisé.
- N'appeler ni `SaveChanges()` ni `SaveChangesAsync()` pour cette lecture.
- Traduire une indisponibilité PostgreSQL en `DependencyUnavailableException` afin de retourner `503 Service Unavailable`.

## Rôle `Member`

Ajouter une migration de données qui :

- insère le rôle `Member` avec un identifiant déterministe ;
- attribue ce rôle à tous les comptes existants ;
- évite les doublons dans `user_roles` ;
- supprime proprement ces associations et le rôle dans la migration descendante.

Adapter ensuite l'inscription afin d'attribuer `Member` à chaque nouveau compte dans la transaction existante.

- Le repository ajoute seulement l'association au contexte.
- Le repository n'appelle pas `SaveChangesAsync()`.
- La création du compte, l'attribution du rôle et la création du message d'outbox restent atomiques.
- Une modification ultérieure des rôles en base doit être immédiatement visible dans `/current`, sans attendre le renouvellement du JWT.

## API et OpenAPI

Ajouter `GetCurrentAsync` dans `AuthSessionsController` avec :

- `[Authorize]` utilisant la politique nommée ;
- `200 CurrentSessionResponse` ;
- `401 ErrorResponse` ;
- `503 ErrorResponse` ;
- un summary XML explicite ;
- le mapping du modèle Application vers le DTO API ;
- un log structuré `Information` lors d'une lecture réussie ;
- un log structuré `Debug` dans la couche Application.

Les logs ne doivent contenir ni l'adresse e-mail ni la liste des rôles. Le `CorrelationId` et le `TraceId` restent fournis automatiquement par le scope HTTP.

OpenAPI devra documenter :

- la sécurité JWT Bearer ;
- le schéma exact de `CurrentSessionResponse` ;
- les réponses structurées `401`, `503` et `500` ;
- l'absence de cookie ou d'antiforgery requis pour cet endpoint.

## Gestion des erreurs

- JWT absent, expiré, mal signé ou invalide : `401 Unauthorized`.
- Claim `sub` absent ou invalide : `401 Unauthorized`.
- Membre supprimé : `401 Unauthorized` avec suppression du refresh cookie.
- PostgreSQL indisponible : `503 Service Unavailable`.
- Erreur inattendue : `500 Internal Server Error`.

Un membre supprimé produit `401 Unauthorized` avec suppression du refresh cookie, car il n'existe plus de session authentifiée valide. Le `404` reste réservé aux ressources privées appartenant éventuellement à un autre membre.

## Tests

### Tests unitaires

- validation de `GetCurrentSessionQuery` ;
- retour du modèle lorsque le membre existe ;
- erreur d'authentification lorsque le membre n'existe plus ;
- propagation du `CancellationToken` ;
- interactions vérifiées avec des mocks stricts ;
- mapping exact vers `CurrentSessionResponse` ;
- extraction et validation du claim `sub`.

### Tests fonctionnels

- contrat JSON exact du `200 OK` ;
- `Cache-Control: no-store` ;
- Bearer absent, expiré, mal signé ou avec un mauvais issuer ou audience ;
- claim `sub` absent, vide ou invalide ;
- réponses structurées `401` et `503` ;
- absence d'antiforgery ;
- contrat OpenAPI ;
- absence de données personnelles ou de rôles dans le JWT et dans les logs.

### Tests d'intégration

- lecture réelle de `Id`, `Email`, `DisplayName` et des rôles depuis PostgreSQL ;
- attribution de `Member` aux nouveaux comptes ;
- reprise des comptes existants par la migration ;
- ordre alphabétique des rôles ;
- modification des rôles visible immédiatement avec le même JWT ;
- comportement lorsque le membre est supprimé ;
- indisponibilité PostgreSQL ;
- migration montante et descendante.

## Quality gate

- Maintenir 100 % de couverture des lignes et des branches sur le code concerné.
- Exécuter `dotnet format` en vérification.
- Compiler en Release sans warning.
- Exécuter tous les tests unitaires, fonctionnels, d'intégration et de migration.
- Vérifier l'absence de migration EF Core en attente.
- Vérifier les packages vulnérables ou dépréciés.
- Exécuter les validations Docker Compose concernées.
- Effectuer une review complète avant la PR.

## Git et livraison

- Branche : `codex/MK-766-current-session`.
- Commits en anglais au format `[MK-766] ...`.
- Titre de PR : `[MK-766] Add current member session endpoint`.
- Attendre la CI, SonarCloud, CodeQL et les validations des conteneurs.
- Effectuer un squash merge dans `develop`.

## Hors périmètre

- Logout et révocation manuelle d'une session.
- Ajout de données personnelles ou de rôles dans le JWT.
- Gestion des permissions métier.
- Modification du profil courant.
- Liste et révocation des sessions des autres appareils.

## Décision retenue

Si le JWT est valide mais que le membre a été supprimé, retourner `401 Unauthorized` et supprimer le refresh cookie.
