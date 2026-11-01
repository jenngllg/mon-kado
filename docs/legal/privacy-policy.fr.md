# Politique de confidentialité — MonKado

**Brouillon — non destiné à la publication**

Dernière mise à jour du brouillon : 8 septembre 2026.

Ce document prépare la future page `/privacy-policy`. Il décrit le backend à la
date de rédaction, pas un environnement de production vérifié. Les réglages
définitifs et les éléments `LEGAL-*` doivent être validés avec la
[checklist de publication](publication-checklist.md).

## Qui est responsable de vos données ?

**À compléter — LEGAL-01 et LEGAL-03 :** identité et coordonnées du responsable du
traitement, contact pour exercer vos droits et coordonnées du délégué à la
protection des données si un délégué est désigné. MonKado désigne ici le service,
pas une personne morale identifiée.

## Pourquoi et comment vos données sont-elles utilisées ?

Les informations viennent de vos saisies, des opérations effectuées sur le
service, de Google si vous choisissez cette connexion et des pages marchandes si
vous demandez un import. Des contenus peuvent aussi être saisis par d'autres
utilisateurs ; évitez d'y inclure des informations personnelles sur des tiers.

Les accès techniques nécessaires à l'exploitation sont réservés aux personnes
habilitées. Les destinataires fonctionnels sont précisés ci-dessous ; les
prestataires et leurs conditions restent à confirmer dans LEGAL-04 et LEGAL-06.

### Compte, connexion et profil

Votre adresse e-mail, votre pseudo, les dates du compte et ses informations de
sécurité permettent de gérer votre inscription, votre connexion et les demandes
liées au compte. Pour une inscription classique, le mot de passe est traité pour
créer une empreinte de vérification, pas conservé en clair. Le pseudo n'est pas
unique et ne constitue pas une preuve d'identité civile.

Si vous utilisez Google, le service vérifie votre identité Google et reçoit
notamment l'adresse e-mail, son état de vérification, un identifiant Google et le
nom disponible pour initialiser le pseudo. L'association Google est conservée avec
le compte ; les jetons d'accès Google ne sont pas conservés comme identifiants de
session MonKado. Google intervient dans le parcours de connexion choisi et traite
également des données selon ses propres conditions, à vérifier avant publication.

Votre photo de profil est facultative. **Les comptes dont l'adresse e-mail est
confirmée peuvent être recherchés sans connexion par leur pseudo.** Les résultats
exposent l'identifiant public, le pseudo et la photo actuelle lorsqu'elle existe,
mais pas l'adresse e-mail. La photo est donc publique, et non réservée aux membres
de vos listes. Ne choisissez pas de pseudo ou de photo que vous souhaitez garder
confidentiels.

Le compte confirmé n'a pas de purge automatique d'inactivité identifiée dans ce
backend. Il reste conservé jusqu'à sa suppression ; la justification de ce
critère doit être validée avant production. Les comptes non confirmés deviennent
éligibles au nettoyage après 30 jours. La suppression du compte retire son profil
et son association Google, sous réserve des exceptions d'historique décrites plus
bas.

### Listes, cadeaux, images et partage

Les noms, notes, liens marchands, prix, quantités, ordre des cadeaux et images
proviennent du propriétaire ou d'un import qu'il confirme. Ils servent à préparer
et consulter les listes. Une liste est accessible à son propriétaire et, après
partage, aux personnes disposant du lien et de son secret. Le lien peut être
transmis à d'autres personnes : il ne faut pas le considérer comme une invitation
limitée à un destinataire nominatif.

Les consultations partagées donnent accès au contenu prévu pour les visiteurs,
images comprises. Elles ne donnent pas accès aux identités des réservants. La
recherche publique des profils ne rend pas les listes privées publiques. Les
administrateurs habilités peuvent consulter le contenu actuel d'une liste
signalée pour les besoins de la modération.

Les images sont normalisées en WebP avec suppression des métadonnées ; le service
ne conserve pas l'image originale ni son nom comme fichier métier. Les images
normalisées sont stockées sur un volume serveur, pas comme contenu dans PostgreSQL.
Les liens temporaires des images de cadeaux ne remplacent pas la vérification des
droits d'accès. Ces protections n'empêchent pas un destinataire de conserver une
copie du contenu qu'il a pu consulter.

Les données actives restent liées à la liste et aux cadeaux jusqu'à leur
suppression. Le retrait ou remplacement d'une image rend l'ancienne référence
inaccessible par l'API ; son fichier est nettoyé ensuite. Les historiques de
réservation peuvent conserver certains noms après suppression de la source.

### Participation invitée et réservations

Un invité fournit un pseudo et reçoit un identifiant technique de session dans
son navigateur. Les participations relient un invité ou un membre à une liste.
Les quantités réservées et les dates servent à gérer les disponibilités et à
éviter la surréservation. Les parcours personnels donnent au participant accès à
ses propres réservations ; le propriétaire ne reçoit pas les identités des
réservants par les réponses de gestion de ses cadeaux.

La session invitée expire après 180 jours par défaut. **L'expiration ou la
suppression du cookie ne supprime pas la participation ni les réservations.** Une
participation invitée rattachée ultérieurement à un compte peut garder son ancien
pseudo invité. Les participations et réservations actives suivent aussi le cycle
de vie de leurs ressources parentes.

L'historique d'un membre conserve les noms de liste et de cadeau, quantités, dates
et état de ses réservations, y compris lorsqu'une ressource d'origine n'existe
plus. Aucune purge temporelle de cet historique n'a été identifiée ; il disparaît
avec le compte du membre qui détient cet historique. Ce critère et celui des
participations invitées doivent être validés dans LEGAL-07, sans présenter la durée
du cookie comme leur durée de conservation.

### Liens de produits

Vous pouvez enregistrer un lien de produit avec un souhait. MonKado ne consulte
pas automatiquement ce lien pour remplir les informations du souhait. Si vous
ouvrez le lien, vous quittez le service et les pratiques du site destination
s'appliquent. L'extension facultative peut lire la page consultée dans votre
navigateur ; ses fonctionnalités sont distinctes du formulaire MonKado.

### Signalements et modération

Le motif et le commentaire d'un signalement sont fournis par le déclarant et
destinés aux administrateurs habilités. Le signalement ne contient pas de champ
identifiant son auteur ; cela ne garantit pas l'anonymat d'un texte dans lequel
une personne aurait elle-même indiqué son identité, ni de toute trace réseau.

Le traitement des signalements et la modération utilisent également des décisions,
motifs, dates et identifiants administratifs. Le propriétaire peut recevoir une
notification liée à la modération de sa liste. Les signalements et historiques de
modération suivent la suppression de leur liste ; cette règle est distincte de la
rétention des audits d'export ou d'effacement.

### E-mails et sécurité des requêtes

MonKado utilise Gmail pour envoyer les messages transactionnels : confirmation,
sécurité du compte, export et notifications de modération notamment. L'adresse
destinataire et le contenu du message, qui peut comporter un lien de confirmation,
sont transmis au prestataire et à la messagerie du destinataire. Le code d'envoi
ne prévoit ni marketing ni suivi d'ouverture.

Les métadonnées d'e-mails d'authentification traités sont nettoyées après 30 jours
par défaut ; celles de modération disposent aussi d'un délai par défaut de
30 jours. Des demandes parentes peuvent provoquer une suppression plus tôt.
Les messages en attente ou en cours de nouvelle tentative ne sont pas tous soumis
à cette seule règle. Après un effacement administratif, l'adresse destinée à la
notification est retirée après résultat terminal, ou devient éligible à purge à
l'échéance absolue de 24 heures. Les copies présentes chez Gmail et chez les
destinataires suivent des règles distinctes, à préciser dans LEGAL-06 et LEGAL-07.

Les logs applicatifs servent au diagnostic et à la sécurité : événements, statuts
HTTP, modèles de route, identifiants techniques et de corrélation. Le code évite
les corps de requêtes et secrets dans ces logs. Les traces du proxy, de
l'hébergement et des prestataires doivent être inventoriées séparément ; ce
document ne garantit pas qu'elles excluent une adresse IP ou toute autre donnée
personnelle. Leurs destinataires habilités et leur durée sont à confirmer.

## Cookies et données de session

Les noms ci-dessous sont ceux de production ; les cookies MonKado locaux ne
portent pas le préfixe `__Host-`.

| Mécanisme | Usage | Durée technique actuelle |
| --- | --- | --- |
| `__Host-MonKado.Antiforgery` | Protection contre des requêtes forgées | Cookie de session du navigateur |
| `__Host-MonKado.Refresh` | Renouvellement de la connexion | Sans « se souvenir de moi » : cookie de session, session serveur glissante de 8 heures ; avec cette option : échéance absolue de 30 jours |
| `__Host-MonKado.Guest` | Reconnaissance de la participation invitée | 180 jours par défaut |
| `__Host-MonKado.GoogleExternal` et cookies de corrélation/nonce du middleware | Finalisation sécurisée de la connexion Google | Parcours temporaire de 5 minutes depuis son départ ; cookie final limité au temps restant |
| Jeton d'accès MonKado | Autorisation des requêtes authentifiées | 15 minutes ; sa conservation uniquement en mémoire côté frontend reste à vérifier |

Une fin de session de navigateur dépend aussi de ses fonctions de restauration.
La validité d'un jeton n'est pas une garantie de purge physique immédiate des
enregistrements serveur. Les cookies de Google sur ses propres domaines relèvent
également de ses pratiques.

Ces mécanismes ont des fonctions techniques. Leur qualification finale et
l'inventaire complet du frontend restent à valider dans LEGAL-08. Aucun bandeau
de consentement ou mécanisme d'acceptation n'est ajouté par ce document. Tout
ajout de mesure d'audience, publicité ou affiliation doit être réévalué avant
activation, conformément aux [règles de la CNIL sur les cookies](https://www.cnil.fr/fr/cookies-et-autres-traceurs/que-dit-la-loi).

## Export, suppression et conservation résiduelle

L'export automatique prépare une archive ZIP privée contenant du JSON et les
images normalisées encore conservées. Il couvre notamment le compte, ses listes,
ses participations et ses propres réservations. Il ne contient pas les mots de
passe, secrets de connexion ou de partage, ni les réservations des autres personnes.
L'archive est disponible 24 heures après sa publication par défaut, puis son
nettoyage est asynchrone. Les métadonnées de traitement ont leur propre cycle.

Cet export ne prétend pas couvrir à lui seul toute demande d'accès : les logs
opérationnels et certains éléments de modération nécessitent un examen
complémentaire, notamment pour protéger les droits des tiers. Le processus de
contact correspondant reste à rendre opérationnel avant publication.

La suppression retire le compte et ses données liées selon leurs règles de
suppression. Elle ne supprime pas automatiquement toute copie déjà reçue par un
tiers, les historiques de réservation appartenant à d'autres membres, les
sauvegardes ni tous les audits. Les fichiers sont retirés par des traitements de
fond pouvant être retardés par une panne.

Les audits d'export et d'effacement administratifs conservent des identifiants
techniques, dates, types d'action et références de demande pendant six mois
calendaires avant éligibilité à purge. Certaines références à un compte sont
effacées lors de sa suppression. Cette durée est un choix du projet, pas une durée
présentée comme imposée par le RGPD. Le début d'un téléchargement journalisé ne
prouve pas la réception complète de l'archive.

**À compléter — LEGAL-07 :** durées définitives, critères de conservation et
modalités de suppression dans les sauvegardes, logs, outils de support et services
tiers. Les valeurs techniques ci-dessus ne sont pas une promesse de production.

## Fondements, prestataires et transferts

**À compléter — LEGAL-05 :** base légale de chaque traitement, caractère requis ou
facultatif des données et conséquences d'un refus, intérêts poursuivis lorsque
l'intérêt légitime est retenu. Ne pas considérer l'inscription comme un
consentement global à tous les traitements.

**À compléter — LEGAL-04 et LEGAL-06 :** prestataires réels, rôles respectifs,
localisations, sous-traitance et éventuels transferts hors Espace économique
européen, avec les garanties applicables et leur mode de consultation. Aucun
hébergement exclusivement européen ni absence de transfert n'est affirmé ici.

## Vos droits et vos contacts

Selon les conditions applicables, vous pouvez exercer vos droits d'accès,
rectification, effacement, limitation, opposition et portabilité ; un consentement
peut être retiré lorsqu'un traitement repose sur celui-ci. Vous pouvez aussi
adresser une réclamation à la CNIL. Ces informations devront être adaptées aux
bases légales retenues. [Information des personnes — CNIL](https://www.cnil.fr/fr/conformite-rgpd-information-des-personnes-et-transparence)

**À compléter — LEGAL-03 et LEGAL-09 :** canal public suivi, modalités de demande,
délais applicables et vérification proportionnée du lien avec le compte. Une pièce
d'identité civile n'établit pas à elle seule la propriété d'un compte pseudonyme ;
ne pas en demander systématiquement. Les fonctions de modification, export et
suppression du backend nécessitent encore une vérification de leurs parcours
frontend avant de les annoncer comme disponibles aux utilisateurs.

**À compléter — LEGAL-10 :** public visé, traitement des situations concernant les
mineurs et vérification d'éventuels traitements supplémentaires introduits par le
frontend ou le déploiement. Ne pas déduire du seul backend une règle d'âge ou une
garantie d'absence de profilage pour tout le service.

## Évolution de cette politique

La version publiée indiquera sa date de mise à jour. Toute évolution des usages,
prestataires ou durées devra être répercutée dans les informations présentées aux
utilisateurs. La validation des textes et des pages publiques reste un prérequis
à leur publication définitive.
