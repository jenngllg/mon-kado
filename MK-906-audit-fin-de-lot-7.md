# MK-906 — Audit de fin du lot 7

**Statut : audit backend terminé le 14 septembre 2026. Les huit MR sont mergées et la validation finale de `develop` est verte. Aucune anomalie fonctionnelle confirmée sur le périmètre audité.**

## Périmètre et méthode

- Comptes, profils, recherche publique, exports personnels et administratifs, effacement, signalements, modération, audit administratif, révocation des sessions et MFA.
- Les huit MR Dependabot #78 à #85 sont intégrées. Commit `develop` audité : `817432313e352a362d15f4df3d7aeb55023929bf`.
- Le contenu suivi de ce commit est strictement identique à l'ensemble testé localement : arbre Git `3ff4e02c0207bd774bae21bf027409945ff0ab0f`, comparaison complète sans différence. Aucun changement métier n'a été importé pendant l'intégration des dépendances.
- Relecture du code, des contraintes PostgreSQL, des contrats HTTP/OpenAPI, des tests et des guides d'exploitation. Tests avec PostgreSQL isolé et providers simulés, sans intervention en production.
- Constats avant corrections métier. Les adaptations de compatibilité Dependabot sont seules corrigées pendant cette phase. Aucun déploiement ni migration sur une base utilisateur.

## Dépendances

| MR | Mise à jour | État vérifié |
|---|---|---|
| #84 | SDK de construction Docker 10.0.401 | Squash mergé `b88f64d3545f3832a580605ffc279d89a7cb9fde`, CI, conteneurs, CodeQL et Sonar verts |
| #85 | Runtime API/Worker 10.0.12 | Squash mergé `88e608fa4061fd8ed6306ee7fa44d5a542869de5`, CI, conteneurs, CodeQL et Sonar verts |
| #83 | SDK du dépôt 10.0.401 | Squash mergé `b031ceac7a5b9100bbca8e789c772bd862a0dff5`, CI, conteneurs, CodeQL et Sonar verts |
| #78 | Packages Microsoft 10.0.12 et SDK de test 18.10.0 | Squash mergé `98e9a1ac749ace5ddf4745d8cc9393350b36a7e1`, contrôles verts et couverture 100 % |
| #81 | Outil EF Core 10.0.12 | Squash mergé `74916eedb55914c9d7b0a4a13f46cf0c8e0d9e92`, contrôles verts et couverture 100 % |
| #80 | AngleSharp 1.8.1 | Squash mergé `4bddaac1bd3ef606c4c60077b1ac82e77b730aa5`, contrôles verts et couverture 100 % |
| #82 | MimeKit 4.18.0 | Squash mergé `908a1a9000ff1b0503c646aa217b194d1c5c4c11`, contrôles verts et couverture 100 % |
| #79 | Testcontainers 4.15 et xUnit 4 | Squash mergé `817432313e352a362d15f4df3d7aeb55023929bf`, contrôles verts et couverture 100 % |

La variante officielle `xunit.v3.mtp-off` 4.0.1 conserve le moteur VSTest et Coverlet/OpenCover sans diminuer la version du framework ni modifier les exclusions. Voir la [documentation officielle xUnit](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform).

Dependabot a ouvert #87 pendant l'opération. Ses onze paires package/version ont été comparées au `develop` résultant de #78 : elles sont toutes déjà présentes. #87 a donc été fermée comme doublon, avec justification, sans second merge ni rétrogradation.

## Garanties examinées

| Domaine | Éléments examinés | Sources et tests examinés |
|---|---|---|
| Autorisations | Politiques Bearer explicites ; rôle administrateur relu dans PostgreSQL ; pas de rôles JWT faisant autorité | `JwtAuthenticationExtensions`, `AdministratorAuthorizationHandler`, tests de refus cookie seul et retrait de rôle |
| Sessions et MFA | Filtre partagé entre JWT/refresh ; administrateurs et comptes déjà enrôlés exigent la version MFA courante ; promotion n'améliorant pas les anciennes sessions | `AuthenticationSessionQueries`, `TwoFactorChallengeIssuer`, suites `TwoFactor*IntegrationTests` |
| Révocation administrative | Comptes verrouillés dans un ordre déterministe, un seul commit, événement exact confirmé après commit ambigu, pas de rejeu touchant une reconnexion ultérieure | `AdministrativeSessionRevocationService`, courses login/refresh/révocation dans les tests PostgreSQL |
| Effacement | Suppression dans la transaction, outbox des images de cadeaux/profil, participants supprimés, noms de contenus effacés des historiques des autres membres | `MemberAccountDataRemovalService`, `AdministrativeAccountErasureIntegrationTests` |
| Exports | Projections explicites et snapshot RepeatableRead ; isolation du membre ; contrôle du rôle, de la cible et de l'expiration avant remise du flux ; audit durable du début de téléchargement | `PersonalDataExportSnapshotReader`, `PersonalDataExportArchiveReader`, `AdministrativeDataExportService`, tests d'archives et de commits ambigus |
| Photos de profil | ETag vérifié avant mutation, ancienne image dans l'outbox, URL publique uniquement pour une image courante d'un compte confirmé, aucune conservation de l'original | `ProfileImageService`, commandes et requêtes de profil, `ProfileImageIntegrationTests` |
| Signalements | Motif/statut filtrés avant agrégation ; pagination SQL ; lecture des contenus signalés distincte des accès publics | `ReportedWishlistService`, contrats et tests de signalements |
| Traitement | Compte administrateur, liste puis signalement verrouillés ; ETag et événement exact pour les résultats ambigus ; historique paginé en RepeatableRead | `WishlistReportReviewService` et suites associées |
| Suspension/réactivation | Compte administrateur verrouillé et rôle revérifié ; liste verrouillée, ETag contrôlé ; décision et état persistés ensemble ; résultat ambigu vérifié sans rejouer la décision | `WishlistModerationService`, `WishlistModerationIntegrationTests`, `WishlistModerationEmailIntegrationTests` |
| Journal administratif | UNION ALL de quatre sources ; filtres avant comptage/page ; tri déterministe ; acteur joint extérieurement ; bornes sub-microseconde traitées explicitement | `AdministrativeAuditService`, tests de suppression d'acteur, compteurs et écritures concurrentes |

Les assertions d'export relues contrôlent notamment le ZIP, les images, le membre cible, l'absence du contenu et de l'adresse d'un autre membre, des hashes de mot de passe/refresh, du security stamp et des chemins de stockage. Les contrôles de journal administratif vérifient les propriétés applicables/nulles et l'absence d'adresse e-mail. Ces tests ne constituent pas à eux seuls une preuve d'absence de toute fuite possible.

Le contrôle des exclusions ne trouve aucun attribut `ExcludeFromCodeCoverage` sur les commandes ou requêtes MediatR ni de recours à `InternalsVisibleTo` dans les sources recherchées. Les exclusions du lot examinées concernent des DTO, constantes ou conteneurs de propriétés sans règle métier. Les filtres globaux de Coverlet concernent les tests, migrations et fichiers générés, pas les services applicatifs. Les mises à jour de dépendances ne modifient pas ces règles.

### Sessions, récupération et conservation

- `AuthenticatedMemberValidationService` vérifie le `jti` enregistré, la session courante, son propriétaire, son expiration et sa révocation en base. Le filtre MFA commun s'applique aussi aux refresh. `OptionalBearerMiddleware` empêche qu'un Bearer refusé devienne silencieusement une identité invitée.
- `TwoFactorService` verrouille le compte avant l'état MFA ; les échecs de vérification sont persistés, les opérations sont liées à une preuve temporaire et les commits ambigus sont confirmés par leur reçu exact. Les changements de facteur/récupération révoquent les autres sessions et preuves dans la transaction. La récupération par code exige le remplacement du facteur avant de créer une session.
- `TwoFactorCryptography` protège le secret avec des finalités Data Protection liées au membre et à la version du facteur ; les codes de récupération sont aléatoires et seuls leurs hashes sont stockés. Un pas TOTP déjà consommé n'est pas accepté à nouveau. Une clé indisponible ne permet pas de contourner le MFA.
- `PersonalDataExportJobs` lie la publication à la tentative et au lease courant. La suppression du compte empêche une publication tardive ; les fichiers sont réconciliés séparément. Le snapshot exporte uniquement les participations et réservations du membre, et jamais les preuves MFA.

| Donnée | Règle vérifiée dans le dépôt | Sources |
|---|---|---|
| Archive personnelle | Disponible 24 h après publication par défaut ; expiration vérifiée avant téléchargement, puis suppression physique différée | `PersonalDataExportOptions`, `PersonalDataExportArchiveReader`, `PersonalDataExportJobs` |
| Métadonnées d'export terminales | Suppression après nettoyage des fichiers et fin de leur utilité pour le quota glissant de 24 h ; compte supprimé traité sans attendre ce quota | `PersonalDataExportJobs.CleanupAsync` |
| Audits export, effacement, révocation | Six mois calendaires, puis purge par lots | Constantes `Administrative*Constraints`, `PersonalDataExportJobs`, `AccountErasureMaintenance`, `ExpiredAuthenticationSessionCleanup` |
| Preuves MFA | Échéance absolue de cinq minutes ; nettoyage des preuves expirées après une heure de grâce | `TwoFactorChallenge`, `TwoFactorConstraints`, `ExpiredAuthenticationSessionCleanup` |
| Métadonnées JWT | Purge après expiration augmentée de la tolérance de 30 secondes, indépendamment de la durée de session | `ExpiredAuthenticationSessionCleanup` |

Ces durées décrivent le code et ses valeurs par défaut, pas une attestation de la configuration de production. La disponibilité logique ne doit pas être confondue avec la date de suppression physique lorsque le Worker ou le volume est indisponible.

## Constats

Aucune anomalie fonctionnelle confirmée à l'issue des relectures sur le périmètre backend du lot 7 et de la validation de la base finale. Aucun correctif métier supplémentaire ni aucune migration n'ont donc été introduits dans cet audit. Ce résultat ne constitue ni un test d'intrusion exhaustif ni une validation juridique ou de production.

Les corrections réalisées concernent uniquement les dépendances : alignement des versions Microsoft centrales, choix de la variante officielle xUnit compatible avec VSTest, et résolution du conflit de versions AngleSharp. Les prérequis de production ci-dessous sont distincts de défauts backend reproduits ; ils ne sont pas considérés comme réalisés par cet audit.

## Validation finale

- La [CI finale de develop](https://github.com/jenngllg/mon-kado/actions/runs/34866178089) est réussie sur `817432313e352a362d15f4df3d7aeb55023929bf` : **4 046 tests Linux réussis**, un cas propre à Windows ignoré sous Linux et couvert par le job Windows, aucun échec.
- Couverture combinée Linux/Windows : **23 026/23 026 lignes et 4 583/4 583 branches, soit 100 % dans les deux cas**. Quality gate Sonar : **PASSED** sur `develop`.
- Les étapes de formatage, build, scan de l'historique Git pour les secrets, audits des packages vulnérables et dépréciés, vérification du modèle EF et validation des deux configurations Compose ont toutes réussi. Aucun seuil, filtre ou retry de tests n'a été ajouté pour obtenir ce résultat.
- Les [contrôles conteneurs finaux](https://github.com/jenngllg/mon-kado/actions/runs/34866178006) sont réussis : construction API/Worker, stack isolée et endpoints, identité non-root et racine en lecture seule, idempotence des migrations, persistance Data Protection, comportement lors d'une panne PostgreSQL et scans des images. Les scans appliquent le filtre existant des vulnérabilités HIGH/CRITICAL disposant d'un correctif ; ils ne prouvent pas une absence universelle de vulnérabilités.
- L'[analyse CodeQL finale](https://github.com/jenngllg/mon-kado/actions/runs/34866177625) est réussie. Le contrôle GitHub Code Scanning après analyse ne remonte aucune alerte ouverte. L'inventaire des MR ouvertes est vide ; #87 est fermée comme doublon et les huit MR initiales sont bien mergées.
- Validation de toutes les dépendances réunies, branche locale non poussée `codex/MK-906-integration-validation`, commit `b3e2cc9a84bfc8e3b7b8320ebce09850a85846e3` : **4 041 tests réussis, aucun échec, six cas Linux ignorés sous Windows** (13 rapports TRX). Formatage conforme, aucun package vulnérable ou déprécié, aucun changement de modèle EF non migré. Les seuls chemins non couverts sous Windows restent ceux des protections de fichiers Linux déjà identifiés ; aucune exclusion ajoutée. La comparaison intégrale avec `develop` final est réussie : même arbre Git `3ff4e02c0207bd774bae21bf027409945ff0ab0f`.
- Lors de cet assemblage local, le conflit de lignes adjacentes entre #78 et #80 a été résolu en conservant `AngleSharp 1.8.1` et `Microsoft.Extensions.Http 10.0.12`. Cette résolution a ensuite été appliquée à #80 et poussée normalement après vérification du diff et des tests d'import ; aucune version n'a été rétrogradée.
- Chaque MR a également été actualisée et validée avant son squash merge. Les preuves intermédiaires restent disponibles : [#84](https://github.com/jenngllg/mon-kado/actions/runs/34846037834), [#85](https://github.com/jenngllg/mon-kado/actions/runs/34847336507), [#83](https://github.com/jenngllg/mon-kado/actions/runs/34848749809), [#78](https://github.com/jenngllg/mon-kado/actions/runs/34850126461), [#81](https://github.com/jenngllg/mon-kado/actions/runs/34851429137), [#80](https://github.com/jenngllg/mon-kado/actions/runs/34852952913), [#82](https://github.com/jenngllg/mon-kado/actions/runs/34854424838), [#79](https://github.com/jenngllg/mon-kado/actions/runs/34855885686).

## Limites et prérequis de production

- Les brouillons `docs/legal/` sont locaux et non suivis dans le checkout principal. Ils ne prouvent ni publication frontend ni validation juridique de #810.
- Le guide MFA exige le support frontend complet, des smoke tests HTTPS réels, un déploiement coordonné et la préservation/restauration des clés Data Protection. Aucun de ces prérequis réels n'est réputé validé par la CI simulée.
- Les quotas IP en mémoire s'appliquent par instance API, contrairement au compteur de vérification MFA par compte en PostgreSQL. Le guide MFA demande une limitation IP agrégée au périmètre si elle est requise pour un déploiement multi-instance ; sa configuration réelle n'a pas été vérifiée.
- Les volumes, sauvegardes, prestataires et politiques de conservation de production ne sont pas inspectés. Les valeurs du dépôt ne prouvent pas les paramètres effectivement déployés.
- Les fichiers personnels préexistants, prototypes et anciens rapports sont préservés ; ce rapport reste local et non committé.

## Conclusion

L'audit backend #906 et l'intégration des huit mises à jour Dependabot sont terminés. Aucun correctif métier n'est proposé à la suite de cette revue. La mise en production et la clôture des parcours utilisateur dépendant du frontend restent conditionnées aux prérequis ci-dessus, qui n'ont pas été exécutés dans cette tâche.
