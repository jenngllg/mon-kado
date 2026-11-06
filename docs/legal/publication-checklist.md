# [MK-810] Checklist de publication des informations légales

État au 8 septembre 2026 : **brouillons préparatoires uniquement, publication bloquée**.

Référence technique examinée : `develop`, commit `c774109` (incluant MK-806).

Complément du 6 novembre 2026 : les brouillons et pages HTML ont été actualisés
pour l'extension facultative, les notifications push, les groupes et le mode
surprise. Les traces d'utilisation et accès administratifs sont décrits brièvement,
sans détailler les outils internes. Les constats historiques ci-dessous restent
datés ; leurs anciennes références .NET ne prouvent pas la configuration déployée.
Cette checklist accompagne les [mentions légales](legal-notice.fr.md), la
[politique de confidentialité](privacy-policy.fr.md) et les
[CGU](terms-of-use.fr.md). C'est un document de suivi
versionné, pas un plan d'implémentation ni un registre juridique validé.

## Décisions et informations bloquantes

Chaque point reste ouvert jusqu'à une validation explicite de l'exploitant. Ne
pas publier de données personnelles dans Git pour remplir ces cases sans accord.
Une preuve contractuelle ou sensible peut rester dans un espace restreint ; noter
ici seulement sa référence non sensible, la date et le rôle du validateur.

- [ ] **LEGAL-01 — Éditeur et responsable du traitement.** Confirmer le statut,
  l'identité et les coordonnées à publier ; déterminer les mentions applicables
  à ce statut, sans présumer une société ou une activité non professionnelle.
- [ ] **LEGAL-02 — Publication.** Confirmer l'identité et la qualité du responsable
  de publication et les obligations applicables.
- [ ] **LEGAL-03 — Contact.** Choisir un canal public réellement suivi, son
  responsable de traitement des demandes et, le cas échéant, le contact du DPO.
  Ne pas assimiler automatiquement la boîte d'envoi Gmail au support.
- [ ] **LEGAL-04 — Hébergement.** Identifier les prestataires et coordonnées du
  frontend, de l'API, de PostgreSQL, des images, des archives et sauvegardes,
  ainsi que les lieux réels d'exploitation. Les fichiers Compose ne le prouvent pas.
- [ ] **LEGAL-05 — Fondements.** Valider une base légale par traitement de la
  matrice ci-dessous, les intérêts poursuivis si nécessaire, les champs obligatoires
  ou facultatifs et les conséquences de leur absence. Aucune base légale n'est
  attribuée automatiquement par cette livraison.
- [ ] **LEGAL-06 — Destinataires et prestataires.** Vérifier les habilitations,
  contrats et rôles des services Google de connexion et Gmail d'envoi, des
  hébergeurs, outils de support et éventuels outils de logs. Déterminer les
  localisations, transferts hors EEE et garanties applicables, sans déduire une
  absence de transfert du pays du VPS. Vérifier les politiques des copies d'e-mails.
- [ ] **LEGAL-07 — Conservation.** Valider les valeurs réellement configurées,
  les critères sans purge temporelle, les délais de nettoyage et la politique des
  logs, sauvegardes, support et prestataires. Définir le traitement des suppressions
  après restauration d'une sauvegarde. Toute correction de code nécessaire relève
  d'un travail distinct, pas d'une promesse ajoutée aux textes.
- [ ] **LEGAL-08 — Cookies et frontend.** Inventorier cookies, stockage navigateur,
  scripts et appels tiers réels ; vérifier les durées, la qualification des cookies
  techniques et toute éventuelle exigence de consentement. Vérifier que le JWT
  reste en mémoire, sans stockage persistant. Aucun bandeau n'est ajouté ici.
- [ ] **LEGAL-09 — Exercice des droits.** Définir le canal, les délais applicables,
  la vérification proportionnée du lien avec un compte ou une participation
  invitée, les demandes complémentaires et la protection des tiers. Vérifier les
  parcours frontend de modification, export et effacement ; ne pas imposer
  systématiquement une pièce d'identité à un compte pseudonyme.
- [ ] **LEGAL-10 — Public et usages effectifs.** Confirmer le public visé et le
  traitement des situations concernant les mineurs, l'information relative aux
  données de tiers et les traitements supplémentaires du frontend/déploiement
  (mesure d'audience, affiliation ou décisions automatisées éventuelles).
- [ ] **LEGAL-11 — Validation et publication.** Faire relire les textes complétés
  par une personne compétente, dater et approuver leur version, puis vérifier
  l'intégration frontend. Retirer les avertissements uniquement après résolution
  de tous les points bloquants. Une date de brouillon n'est pas une date d'entrée
  en vigueur de la politique.
- [ ] **LEGAL-12 — Périmètre contractuel et acceptation.** Confirmer la gratuité
  et l'absence de vente ou d'abonnement au lancement. Réexaminer les CGV avant
  toute offre payante. Définir l'information et l'acceptation éventuelle des CGU
  pour inscription classique, Google, invités et comptes existants : version,
  preuve, durée de conservation, mises à jour substantielles et refus. Distinguer
  cette acceptation du consentement RGPD. Aucune implémentation de ce mécanisme
  dans #810 ; tracer séparément tout travail nécessaire avant activation.
- [ ] **LEGAL-13 — Règles et modération.** Approuver les usages interdits et
  critères de suspension, les procédures d'information et contestation, les
  délais tenables et les obligations applicables à l'activité. Les sanctions
  annoncées doivent correspondre aux capacités et pratiques réelles ; ne pas
  assimiler l'effacement administratif RGPD à un bannissement.
- [ ] **LEGAL-14 — Propriété intellectuelle.** Valider les droits sur les éléments
  du service et une autorisation limitée aux opérations nécessaires sur les
  contenus utilisateurs/importés : droits, finalité, durée, territoire et
  prestataires. Pas de cession globale ni de réutilisation publicitaire présumée.
- [ ] **LEGAL-15 — Responsabilités et litiges.** Valider les engagements de
  disponibilité, la fin d'utilisation, les éventuels préavis, les modalités de
  réclamation, le droit applicable et la médiation si applicable. Écarter les
  exclusions générales et les clauses portant atteinte aux droits impératifs.

## Matrice des traitements à valider

Les données, provenance et destinataires fonctionnels sont décrits dans la
politique ; compléter ci-dessous la décision de l'exploitant avant publication.

| Traitement | Finalité à confirmer | Base légale / champs requis / conséquences |
| --- | --- | --- |
| Compte et connexion classique ou Google | Fourniture et sécurisation du compte | À compléter — LEGAL-05 |
| Profil et recherche publique | Identification des profils par les visiteurs | À compléter — LEGAL-05 ; confirmer l'information sur la photo publique |
| Listes, cadeaux, images et partage | Gestion des souhaits et consultation partagée | À compléter — LEGAL-05 |
| Participation invitée et réservations | Coordination des réservations et historique personnel | À compléter — LEGAL-05 |
| Import de page marchande | Suggestion de contenu à la demande du propriétaire | À compléter — LEGAL-05 |
| Extension facultative | Lecture de la page produit à l'ouverture, préparation d'images et ajouts confirmés | À compléter — LEGAL-05 et LEGAL-08 ; distinguer préparation et enregistrement |
| Indicateur d'ajout par extension | Suivi de l'utilisation de l'extension associé au compte | À compléter — LEGAL-05 ; suppression avec le compte |
| Groupes et idées surprises | Partage selon l'audience choisie, propositions privées et confidentialité du bénéficiaire | À compléter — LEGAL-05 ; distinguer lien public et partage de groupe |
| Notifications dans MonKado et push | Information sur les activités et rappels selon les préférences | À compléter — LEGAL-05, LEGAL-06 et LEGAL-07 ; permission système, abonnements et services d'acheminement |
| Traces d'utilisation et accès administratifs | Suivi du fonctionnement, diagnostic et administration | À compléter — LEGAL-05 ; accès habilités, traces éligibles au nettoyage après 90 jours |
| Signalements et modération | Examen des abus et décisions de modération | À compléter — LEGAL-05 |
| E-mails transactionnels | Notifications liées aux opérations du service | À compléter — LEGAL-05 |
| Sessions, cookies et logs | Continuité, sécurité et diagnostic | À compléter — LEGAL-05 et LEGAL-08 |
| Export, effacement et audit administratif | Réponse aux demandes et traçabilité des actions | À compléter — LEGAL-05 ; distinguer chaque finalité |

## Durées vérifiées dans le backend

### Compléments examinés le 6 novembre 2026

- L'extension lit les métadonnées produit à l'ouverture et peut solliciter la
  préparation serveur d'une image avant confirmation ; aucun souhait n'est créé
  à la simple lecture. Voir le dépôt frontend, `extension/extract.js`,
  `extension/imageSelection.js` et `extension/popup.js`.
- L'indicateur d'ajout par extension suit le compte sans purge temporelle
  distincte : [enregistrement](../../internal/application/extension_usage.go).
- Les traces d'utilisation sont éligibles au nettoyage après 90 jours :
  [worker](../../internal/worker/worker.go). Les corps métier sont filtrés et les
  idées surprises privées omises : [filtrage](../../internal/httpapi/activity_payload.go).
- Les abonnements push sont rattachés à une session ; les adresses sont chiffrées
  et supprimées avec le compte. Les services Apple, Google ou Mozilla reçoivent
  un signal vide ; le message affiché est générique :
  [fonctionnement et nettoyage](../mobile-push.md),
  [transport](../../internal/providers/push.go).
- Confirmer les durées des notifications et de la file de livraison, la rétention
  des prestataires push et les limites des journaux techniques selon la
  configuration réellement déployée. Les anciennes limites par conteneur ne
  doivent pas être reprises comme une garantie actuelle sans vérification.

### Constats historiques du 8 septembre 2026

Ces constats ne certifient pas la configuration de production. Une échéance
d'accès ou d'éligibilité à purge n'est pas une garantie de suppression physique
à cet instant. Les Workers agissent par lots et peuvent être indisponibles.

| Données ou mécanisme | Constat au commit examiné | Source technique |
| --- | --- | --- |
| Compte non confirmé | Échéance à 30 jours, puis nettoyage ; constante métier | [Création](../../src/Infrastructure.Persistence.PostgreSql/Services/AccountRegistrationService.cs), [nettoyage](../../src/Infrastructure.Persistence.PostgreSql/Services/ExpiredAccountCleanup.cs) |
| Compte confirmé | Pas de purge d'inactivité identifiée ; suppression explicite, à justifier | [Suppression membre](../../src/Infrastructure.Persistence.PostgreSql/Services/MemberAccountDeletionService.cs), [effacement administratif](../../deployments/administrative-account-erasure.md) |
| JWT MonKado | 15 minutes de validité, pas une durée de rétention des données du compte | [Émission](../../src/Infrastructure.Persistence.PostgreSql/Services/JwtAccessTokenService.cs) |
| Refresh et cookie | Serveur : 8 heures glissantes ou 30 jours absolus ; cookie sans expiration persistante hors rememberMe | [Politique](../../src/Application/Models/RefreshSessionPolicy.cs), [cookie](../../src/API/Services/RefreshTokenCookieService.cs), [nettoyage](../../src/Infrastructure.Persistence.PostgreSql/Services/ExpiredAuthenticationSessionCleanup.cs) |
| Antiforgery | Cookie de session ; Secure en production, HttpOnly, SameSite=Lax | [Configuration](../../src/API/Extensions/WebSecurityExtensions.cs) |
| Google temporaire | 5 minutes depuis le départ ; durée restante au callback, pas de prolongation | [Constante](../../src/API/Constants/GoogleAuthenticationConstants.cs), [état temporaire](../../src/API/Services/GoogleExternalAuthenticationService.cs), [configuration OIDC](../../src/API/Extensions/GoogleAuthenticationExtensions.cs) |
| Session invitée | 180 jours par défaut, configurable jusqu'à 180 jours ; expiration ne supprimant pas la participation | [Options](../../src/Infrastructure.Persistence.PostgreSql/Options/GuestSessionOptions.cs), [cookie](../../src/API/Services/GuestSessionCookieService.cs), [nettoyage](../../src/Infrastructure.Persistence.PostgreSql/Services/ExpiredGuestSessionCleanup.cs) |
| Historique des réservations | Pas de purge temporelle identifiée ; suppression avec le membre détenteur, pas avec la liste source | [Mapping](../../src/Infrastructure.Persistence.PostgreSql/Configurations/GiftReservationHistoryConfiguration.cs), [lecture](../../src/Infrastructure.Persistence.PostgreSql/Services/GiftReservationHistoryService.cs) |
| Changement d'e-mail | Demande valable 24 heures ; demandes confirmées ou révoquées éligibles après 7 jours ; nettoyage différé tant qu'un e-mail associé reste non traité | [Service](../../src/Infrastructure.Persistence.PostgreSql/Services/MemberEmailChangeService.cs), [nettoyage](../../src/Infrastructure.Persistence.PostgreSql/Services/ExpiredMemberEmailChangeRequestCleanup.cs), [conditions de suppression](../../src/Infrastructure.Persistence.PostgreSql/Repositories/MemberEmailChangeRequestRepository.cs) |
| Réinitialisation du mot de passe | Fenêtre de 1 heure ; ne pas l'assimiler à la purge de toutes les métadonnées d'e-mail | [Service](../../src/Infrastructure.Persistence.PostgreSql/Services/PasswordResetService.cs), [envoi](../../src/Infrastructure.Persistence.PostgreSql/Services/AuthenticationEmailDispatcher.cs) |
| Confirmation de suppression membre | 30 minutes par défaut, configurable au plus à 1 heure | [Options](../../src/Infrastructure.Persistence.PostgreSql/Options/MemberAccountDeletionOptions.cs), [validation](../../src/Infrastructure.Persistence.PostgreSql/Options/MemberAccountDeletionOptionsValidator.cs) |
| E-mails d'authentification traités | 30 jours par défaut après traitement, configurables de 1 à 365 ; pas une purge générale des messages en attente | [Options](../../src/Worker/Options/AuthenticationEmailOptions.cs), [Worker](../../src/Worker/Workers/ProcessedAuthenticationEmailCleanupWorker.cs), [service](../../src/Infrastructure.Persistence.PostgreSql/Services/ProcessedAuthenticationEmailCleanup.cs) |
| E-mails de modération traités | 30 jours par défaut, configurables de 1 à 365 ; cycle propre à la modération | [Options](../../src/Worker/Options/WishlistModerationEmailOptions.cs), [dispatcher](../../src/Infrastructure.Persistence.PostgreSql/Services/WishlistModerationEmailDispatcher.cs) |
| Notification d'effacement administratif | Destinataire retiré après résultat terminal ; échéance absolue de 24 heures, purge au cycle disponible | [Maintenance](../../src/Infrastructure.Persistence.PostgreSql/Services/AccountErasureMaintenance.cs), [fonctionnement](../../deployments/administrative-account-erasure.md) |
| Signalements et modération | Suppression avec la liste ; pas la règle de six mois des audits RGPD | [Signalements](../../src/Infrastructure.Persistence.PostgreSql/Configurations/WishlistReportConfiguration.cs), [modération](../../deployments/wishlist-moderation.md) |
| Archive d'export | Disponibilité 24 heures après publication par défaut ; nettoyage de fichiers asynchrone | [Options](../../src/Application/Options/PersonalDataExportOptions.cs), [maintenance](../../src/Infrastructure.Persistence.PostgreSql/Services/PersonalDataExportJobs.cs) |
| Métadonnées d'export | Nettoyage des états terminaux après fichiers, avec conservation nécessaire à la fenêtre de quota (24 heures par défaut depuis création) ; pas une règle de 24 heures pour toute demande en cours | [Maintenance](../../src/Infrastructure.Persistence.PostgreSql/Services/PersonalDataExportJobs.cs) |
| Audits d'export et d'effacement | Six mois calendaires depuis création, puis purge bornée ; constantes du projet | [Export](../../src/Application/Common/Constants/AdministrativeDataExportConstraints.cs), [effacement](../../src/Application/Common/Constants/AdministrativeAccountErasureConstraints.cs), [maintenance d'effacement](../../src/Infrastructure.Persistence.PostgreSql/Services/AccountErasureMaintenance.cs) |
| Images et temporaires | Images retirées via outbox ; grâce des temporaires à 1 heure par défaut, pas une garantie de nettoyage dans l'heure | [Nettoyage](../../src/Infrastructure.Persistence.PostgreSql/Services/GiftImageCleanupService.cs), [options](../../src/Worker/Options/GiftImageCleanupOptions.cs) |
| Aperçu d'import | En mémoire, sans ressource persistée ; cadeau créé uniquement après confirmation distincte | [Contrôleur](../../src/API/Controllers/WishImportPreviewsController.cs), [service](../../src/Infrastructure.UrlImport/Services/WishImportService.cs) |
| Logs, sauvegardes, support, copies Gmail | Durées de production non établies par ce dépôt ; bloquant LEGAL-06/07 | [Logs HTTP](../../src/API/Extensions/SafeHttpRequestLoggingExtensions.cs), [limites de suppression](../../deployments/administrative-account-erasure.md) |

Pour chaque valeur configurable, relever avant publication la valeur effective
de production et sa provenance, sans copier de secrets. Vérifier aussi les
fréquences des Workers et les alertes sur les retards de purge ; ne pas modifier
la configuration dans cette livraison documentaire.

## Autres points de cohérence vérifiés

- Recherche anonyme de comptes confirmés : identifiant, pseudo et photo, sans
  e-mail, dans [MembersController](../../src/API/Controllers/MembersController.cs)
  et [UserSearchService](../../src/Infrastructure.Persistence.PostgreSql/Services/UserSearchService.cs).
- Photo de profil publiquement accessible et référence courante vérifiée dans
  [ProfileImagesController](../../src/API/Controllers/ProfileImagesController.cs).
- Google utilise les scopes `openid`, `email`, `profile` et `SaveTokens = false`
  dans la [configuration OIDC](../../src/API/Extensions/GoogleAuthenticationExtensions.cs).
  Ne pas confondre les jetons Google utilisateur avec le secret OAuth du compte
  technique d'envoi Gmail.
- Périmètre de l'archive et nécessité d'un accès complémentaire :
  [guide des exports](../../deployments/personal-data-exports.md).
- Les règles de logs du dépôt ne prouvent pas les réglages du proxy, du frontend
  ou du prestataire. Vérifier ces surfaces séparément avant toute affirmation.
- Suspension administrative : lecture privée conservée, accès public et mutations
  ordinaires bloqués, sans suppression immédiate, selon le
  [guide de modération](../../deployments/wishlist-moderation.md).
- Les réservations du service ne sont pas des commandes marchandes. L'import
  produit des suggestions à confirmer, pas une réservation de stock ni un prix
  garanti : [contrat d'import](../../src/API/Controllers/WishImportPreviewsController.cs).

## Intégration et recette frontend ultérieures

- [ ] Publier trois pages statiques françaises `/legal-notice`, `/privacy-policy`
  et `/terms-of-use`,
  sans endpoint backend ajouté et sans dépendance à sa disponibilité.
- [ ] Adapter les liens Markdown internes : les liens entre brouillons
  deviennent les routes publiques ; ne pas exposer la checklist interne à leur place.
- [ ] Vérifier l'accès sans compte, cookie, consentement préalable ni redirection
  vers la connexion ; répéter avec API indisponible.
- [ ] Tester l'ouverture directe et l'actualisation des trois routes sur
  l'hébergement réel, sans erreur 404 ni ancien routage `/#/`.
- [ ] Ajouter des liens de pied de page et une information adaptée aux moments de
  collecte : inscription classique/Google, participation invitée, photo publique,
  signalement et autres formulaires concernés.
- [ ] Vérifier titres hiérarchisés, liens compréhensibles, clavier, focus visible,
  lecture mobile et zoom ; les tableaux ne doivent pas rendre le contenu illisible.
- [ ] Afficher la date de dernière mise à jour de la version approuvée ; rechercher
  tout `À compléter`, repère `LEGAL-*` et avertissement de brouillon avant diffusion.
- [ ] Contrôler les liens de contact et CNIL ainsi que les parcours d'exercice des
  droits annoncés ; aucune promesse de fonctionnalité frontend non disponible.
- [ ] Confirmer que cette livraison n'a ajouté ni collecte, ni consentement global,
  ni acceptation obligatoire de la politique pour la consulter.
- [ ] Ne pas annoncer les CGU comme déjà acceptées ; leur consultation doit rester
  libre. Si un parcours d'acceptation est retenu, le livrer et le tester séparément,
  en cohérence avec LEGAL-12, avant de prétendre disposer d'une preuve contractuelle.
- [ ] Consigner la validation de production et la preuve de consultation publique
  avant de considérer #810 terminée côté utilisateur.

## Références externes de préparation

Consultées le 8 septembre 2026, à revérifier lors de la validation définitive :

- [CNIL — Information et transparence](https://www.cnil.fr/fr/conformite-rgpd-information-des-personnes-et-transparence) : rubriques d'information et présentation adaptée aux personnes.
- [CNIL — Cookies et autres traceurs](https://www.cnil.fr/fr/cookies-et-autres-traceurs/que-dit-la-loi) : examen des finalités des traceurs et des règles de consentement applicables.
- [Service Public — Site internet d'une société](https://entreprendre.service-public.gouv.fr/vosdroits/F37351) : référence conditionnelle, à adapter au statut réel de l'éditeur, pas un modèle automatiquement applicable.
- [France Num — Rédaction des CGU](https://www.francenum.gouv.fr/guides-et-conseils/developpement-commercial/site-web/rediger-des-conditions-generales-dutilisation) : distinction des CGV et préparation du cadre d'utilisation et de son acceptation.
- [Service Public — CGV](https://entreprendre.service-public.gouv.fr/vosdroits/F33527) : réexamen à prévoir si le modèle commercial évolue ; aucune CGV ajoutée pour une vente inexistante.

## Validation de la livraison documentaire

- [x] Vérifier les liens relatifs et la cohérence des quatre documents.
- [x] Vérifier que chaque information manquante renvoie à LEGAL-01 à LEGAL-15.
- [x] Relire les durées contre les sources techniques et distinguer validité,
  rétention et nettoyage ; ne pas inventer une purge absente du code.
- [x] Vérifier l'absence de secrets, d'identités ou d'adresses personnelles ajoutées
  sans accord, et de déclaration générale de conformité acquise.
- [x] Contrôler que le diff de MK-810 ne contient que les quatre documents, aucun plan local,
  changement de code, de configuration, migration ou frontend.

Les contrôles documentaires ne remplacent pas la recette des pages futures. Aucun
nouveau code exécutable n'est introduit ; la couverture des tests backend n'est
pas modifiée et n'est pas une preuve de conformité juridique.
