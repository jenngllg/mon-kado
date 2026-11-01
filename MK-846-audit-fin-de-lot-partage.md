# MK-846 — Audit de fin du lot 4

## Périmètre

- Révision auditée : `1d03e4c` (`develop`).
- Fonctionnalités du lot 4 :
  - #783 — partager une liste par lien ;
  - #784 — consulter une liste partagée ;
  - #785 — filtrer les cadeaux disponibles ;
  - #786 — voir les informations détaillées d’un cadeau ;
  - #789 — signaler une liste.
- Régressions vérifiées sur les parcours publics ajoutés ensuite : participant invité (#840) et réservations.
- Hors périmètre fonctionnel : nouvel audit métier complet du lot 5.

## Résultat

L’audit ne laisse aucun défaut bloquant connu après correction. Les contrats HTTP et OpenAPI, la sécurité du lien partagé, la persistance PostgreSQL, les logs, les tests et le packaging Docker sont cohérents avec les conventions du dépôt.

## Écarts corrigés

1. La documentation du dépôt ne recensait pas les routes du lot 4 et n’expliquait pas les responsabilités de sécurité du frontend pour le secret situé dans le fragment d’URL.
2. `WishlistSharing:FrontendOrigin` acceptait des espaces en début ou fin de valeur, ce qui pouvait produire une URL de partage malformée malgré une configuration apparemment valide.
3. Les opérations `PUT` et `DELETE` du lien de partage pouvaient retourner une erreur structurée `400` pour un `If-Match` malformé sans documenter ce statut dans OpenAPI.
4. `X-Robots-Tag` était ajouté dans les actions du contrôleur après leur succès : les réponses d’erreur produites avant ou autour de l’action pouvaient donc ne pas porter la directive `noindex`.
5. Les limites par adresse et lien pouvaient être contournées en variant continuellement l’identifiant du lien. Une limite agrégée par adresse couvre désormais toutes les routes `/api/v1/shared-wishlists`, en complément des limites spécifiques par lien.
6. Le contrat `X-Robots-Tag` des routes publiques n’était pas exposé dans OpenAPI.

## Vérifications fonctionnelles et techniques

- Secret de partage aléatoire de 256 bits ; seul son hash SHA-256 sert à la vérification.
- Comparaison du hash en temps constant.
- Valeur récupérable par le propriétaire protégée avec ASP.NET Core Data Protection.
- Secret placé dans le fragment de l’URL frontend et transmis à l’API avec `X-MonKado-Share-Token`.
- Rotation conservant l’identifiant du lien et invalidant l’ancien secret.
- Révocation invalidant l’accès public.
- Réponses publiques en `Cache-Control: no-store` et `X-Robots-Tag: noindex, nofollow, noarchive`, y compris les erreurs.
- Filtre `availableOnly` cohérent avec la réservation du participant courant.
- Signalement anonyme protégé par antiforgery, taille maximale du body et doubles limites de débit.
- Aucun secret, token, contenu de souhait ou adresse e-mail dans les logs applicatifs du périmètre.
- Transactions, contraintes, index et suppressions en cascade vérifiés.
- Migrations EF Core à jour et modèle sans changement en attente.

## Quality gate locale

- Format : conforme.
- Build Release : 0 warning, 0 erreur.
- Tests : 2 115 réussis, 0 échec.
- Couverture : 12 290 / 12 290 lignes (100 %) et 2 503 / 2 503 branches (100 %).
- Packages NuGet vulnérables : 0.
- Packages NuGet dépréciés : 0.
- Mises à jour majeures connues et différées : Microsoft.OpenApi, MediatR et xUnit ; mise à jour mineure disponible pour Testcontainers.PostgreSql.
- Smoke test Docker Compose isolé : réussi.
  - configurations locale et production valides ;
  - images API et Worker construites ;
  - liveness, readiness, OpenAPI et CSRF accessibles ;
  - conteneurs applicatifs non-root avec racine en lecture seule ;
  - migrations idempotentes ;
  - liveness à `200` et readiness à `503` lorsque PostgreSQL est indisponible ;
  - conteneurs, volumes et réseaux de test supprimés après exécution.

## Vérifications distantes

- CI et quality gate : réussies.
- SonarCloud : réussi, 0 nouvelle issue, 0 hotspot de sécurité et 100 % de couverture sur le nouveau code.
- CodeQL Actions et C# : réussis.
- Gitleaks : réussi dans la quality gate.
- Build et scans Trivy des images API et Worker : réussis.
- Commentaires de revue ligne par ligne : aucun.

## Risques résiduels et décisions futures

- Le frontend doit supprimer immédiatement le fragment contenant le secret, le conserver uniquement en mémoire et empêcher lui-même l’indexation de sa page HTML. L’en-tête de l’API ne protège pas le document frontend.
- Les limites de débit ASP.NET Core sont en mémoire et s’appliquent par instance d’API. Le déploiement actuel à une instance est protégé ; un déploiement horizontal nécessitera une stratégie distribuée ou une limitation au niveau du reverse proxy.
- Les signalements sont conservés jusqu’à la suppression de la liste. Une politique de rétention ou un parcours de modération pourra être défini lorsque le besoin fonctionnel existera.
- Les liens actifs n’expirent pas automatiquement, conformément au contrat actuel. La rotation et la révocation restent les mécanismes de retrait.
