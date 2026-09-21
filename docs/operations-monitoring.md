# Exploitation du monitoring local — MK-815

## Périmètre et limites

Le moniteur s’exécute sur le VPS, indépendamment de l’API, du Worker et de PostgreSQL.
Il ne redémarre rien, ne répare pas de données et ne déclenche pas de déploiement.
Les alertes utilisent une copie privée des autorisations Gmail existantes, sans outbox
PostgreSQL et sans autorisation Drive. Aucun secret de production ne passe par GitHub Actions.

Aucun abonnement supplémentaire, serveur de métriques, stockage cloud de logs ou sonde
DigitalOcean Uptime n’est requis. Le complément retenu est uniquement DigitalOcean Monitoring
gratuit. Ne pas activer d’option payante depuis ce guide.

**Une panne complète du VPS, de son réseau ou de Gmail peut empêcher les alertes locales.**
Le contrôle externe de disponibilité n’est pas fourni. Une réponse Gmail acceptée ne prouve
pas la réception dans la boîte du destinataire. Une interruption après le POST peut produire
un doublon lors d’une nouvelle tentative explicite ; elle n’est jamais traitée comme un succès.

## Données et conservation

- API et Worker : JSON structuré, une ligne par événement, heure UTC, service, révision,
  niveau, catégorie, EventId, CorrelationId, TraceId et propriétés techniques autorisées.
- Les messages formatés arbitraires, corps HTTP, URL brutes, query strings, adresses e-mail,
  mots de passe, jetons, références support et contenus métier ne sont pas rendus dans les logs.
  Le nom d’événement est issu du catalogue compilé, pas d’un texte fourni par l’appelant.
- Les exceptions conservent leur type et les frames techniques, sans message, données
  arbitraires ni chemins de fichiers sources.
- Docker conserve les logs avec son pilote `local`, au plus trois fichiers de 10 Mo par
  conteneur. Ce plafond en taille ne garantit aucune durée de conservation en jours.
- Les mesures locales sont privées, sans identifiant de membre : sept jours au maximum,
  avec un plafond global de 32 Mio. La saturation raccourcit cette durée.
- Le moniteur conserve aussi l’état technique courant des incidents, dédoublonnage et
  tentatives. Ne pas supprimer cet état pour « remettre au vert » : cela perdrait la preuve
  des tentatives et pourrait provoquer des notifications en double.

Les métriques .NET utilisent `Meter` et des compteurs cumulatifs par processus. Un identifiant
de démarrage empêche de comparer les compteurs de deux processus différents. Les requêtes
HTTP sont comptées avec statut, durée, rejets 429 et abandons. Les cycles Worker enregistrent
leur vrai début, résultat et prochaine exécution attendue ; un heartbeat ne vaut pas réussite
d’un traitement. Les échecs définitifs d’éléments sont comptés séparément des cycles.

Toutes les 30 secondes, chaque processus publie atomiquement son propre fichier versionné :

| Propriétaire | Chemin hôte | Accès |
|---|---|---|
| UID/GID 1654, API | `/var/lib/monkado-observability/api/snapshot.json` | répertoire 0700, fichier 0600 |
| UID/GID 1654, Worker | `/var/lib/monkado-observability/worker/snapshot.json` | répertoire 0700, fichier 0600 |
| root, moniteur | `/var/lib/monkado-monitoring/` | répertoire 0700, fichiers 0600 |

Les conteneurs disposent chacun d’un montage distinct, sans accès au fichier de l’autre
service. Le reste de leur système de fichiers reste en lecture seule. Un fichier absent,
invalide ou périmé n’est pas remplacé par des mesures fictives égales à zéro.

## Règles par défaut

Le timer lance une observation par minute, sans rattrapage automatique. Le service est
limité à 128 Mio, sans swap, 10 % CPU et 45 secondes. Un verrou non bloquant empêche les
observations et les tests d’e-mail simultanés.

| Signal | Déclenchement |
|---|---|
| API, readiness PostgreSQL, frontend activé | trois contrôles consécutifs en échec |
| Conteneur arrêté | trois observations consécutives |
| Nouvelle preuve OOM | immédiat |
| Redémarrages d’un même conteneur | au moins trois en dix minutes |
| Fichier de métriques | absent/invalide ou âgé de plus de trois minutes |
| Cycle Worker | trois échecs, attente dépassée de deux minutes, ou exécution de plus de quinze minutes |
| Échec définitif d’élément | nouvelle augmentation du compteur |
| HTTP 5xx | au moins cinq erreurs et au moins 20 % sur cinq minutes |
| Latence HTTP | p95 supérieur à deux secondes sur dix minutes, au moins vingt mesures |
| Sauvegarde | capture distante de plus de trente heures, échec terminal ou nuit manquée |
| Intégrité sauvegardes | dernier contrôle de plus de huit jours |
| Certificat HTTPS | expiration dans moins de quatorze jours |

Les décisions de latence utilisent les buckets du histogramme, pas les durées individuelles.
Après un redémarrage ou faute de trafic suffisant, les contrôles HTTP peuvent rester
`unknown`. Ce n’est ni une preuve de panne ni une preuve de bonne santé. Une collecte hôte
incomplète est signalée ; elle ne ferme pas un incident existant.

Le moniteur lit le statut #813 sans charger son code ni accéder au dépôt Drive. L’âge de la
**capture distante** est utilisé, pas la date d’un transfert récent d’une vieille capture.
Une nuit manquée est évaluée après 03 h 15, heure de Paris.

Les marqueurs #812 et #813 suspendent uniquement les contrôles de disponibilité/progression
attendus, pendant quinze minutes au maximum. Le marqueur de transition #811 suspend
seulement la disponibilité frontend. Les OOM et sauvegardes ne sont jamais masqués. Au-delà
de quinze minutes, la maintenance est elle-même un incident et les contrôles reprennent.

Un incident ouvre une notification ; le rétablissement demande trois observations saines
consécutives. Les incidents encore ouverts ont un rappel après 24 heures. Les transitions
du même passage sont regroupées : au plus six tentatives d’e-mail par heure, tests volontaires
compris. Les envois opérationnels ont au maximum trois tentatives espacées de quinze minutes.
Une réservation durable précède chaque POST afin qu’un arrêt du processus ne contourne pas
ces limites. Les réponses et erreurs fournisseur ne sont jamais imprimées.

## Installation et activation manuelles

La livraison du code ne vaut pas autorisation de déploiement ou d’envoi réel. Les commandes
ci-dessous sont réservées à l’opérateur après approbation de la publication et du test Gmail.

1. Installer le bundle relu via `deployments/production/install.sh`, comme pour #812.
   Si le monitoring était déjà actif, arrêter son timer et attendre la fin de son service
   avant cette réinstallation ; le script refuse une mise à jour concurrente.
2. Les fichiers du moniteur entrent dans l’empreinte de configuration de la publication.
   Ne pas modifier cette empreinte ou contourner sa vérification pour forcer un déploiement.
   Réinstaller les fichiers revus avant d’utiliser une nouvelle publication qui les modifie.
3. Vérifier `/etc/monkado/monitoring.json`, root:root, 0600. La valeur initiale laisse
   `notificationsEnabled` et `frontendEnabled` à `false`. Une configuration existante n’est
   pas remplacée. Les timers et fichiers de sauvegarde #813 ne sont pas modifiés.
4. Provisionner la copie privée Gmail sur le VPS, jamais dans une commande contenant un secret :

   ```bash
   sudo python3 /opt/monkado/src/Operations.Monitoring/monitor_provision.py install
   ```

   Saisir deux fois l’adresse personnelle destinataire dans l’invite masquée. Le script copie
   uniquement les quatre valeurs Gmail littérales de `production.env` et crée
   `/etc/monkado/monitoring-gmail.json`, root:root, 0600. Il n’envoie aucun message et ne lance
   aucun service. `install` refuse une destination existante ; `replace` est une opération
   explicite de remplacement après revue, notamment en cas de rotation Gmail. Les deux copies
   devront alors être tenues cohérentes ; aucune rotation réelle n’est automatique.
5. Vérifier la configuration et effectuer une observation, notifications toujours désactivées :

   ```bash
   sudo python3 /opt/monkado/src/Operations.Monitoring/monitor_cli.py check-config
   sudo systemctl start monkado-monitor.service
   sudo python3 /opt/monkado/src/Operations.Monitoring/monitor_cli.py status
   ```

6. **Seulement après accord pour un e-mail réel**, tester le canal :

   ```bash
   sudo python3 /opt/monkado/src/Operations.Monitoring/monitor_cli.py test-email
   ```

   Ce test n’active pas les alertes, n’effectue pas de contrôle applicatif et ne se rejoue pas
   automatiquement. Vérifier `emailAcknowledged: true`, puis confirmer la réception dans la
   boîte personnelle. Un résultat ambigu rend un code de sortie non nul ; consulter le statut
   privé `test-email.json` sans afficher les identifiants Gmail. Ne pas répéter aveuglément.
7. Après revue du premier statut et réception du test, activer les notifications dans le
   fichier privé ; activer la vérification frontend uniquement lorsque #811 est effectivement
   publié et vérifié. Puis, après accord d’activation :

   ```bash
   sudo systemctl enable --now monkado-monitor.timer
   sudo systemctl list-timers monkado-monitor.timer --no-pager
   ```

L’installateur ne lance pas le monitoring, n’active pas ce timer et ne remplace pas les
identifiants déjà installés. Ne pas activer directement le service au démarrage : le timer
est l’unique planification prévue.

## Consultation et incident

```bash
sudo python3 /opt/monkado/src/Operations.Monitoring/monitor_cli.py status
sudo systemctl show monkado-monitor.service -p Result -p ExecMainStatus -p MemoryPeak
sudo systemctl list-timers monkado-monitor.timer --no-pager
sudo journalctl -u monkado-monitor.service -n 20 --no-pager
```

`status` est en lecture seule. Un statut âgé de plus de trois minutes ou une invocation
échouée donne un état dégradé. Le moniteur arrêté ne peut pas envoyer sa propre alerte : le
contrôle du timer reste une tâche d’exploitation. Ne pas afficher `production.env`, le JSON
Gmail, un `docker inspect` complet ou les arguments OAuth dans un diagnostic partagé.

## Complément DigitalOcean Monitoring gratuit

Après accord séparé, vérifier l’agent déjà installé et la remontée des métriques dans le
tableau de bord. Préparer les seuils CPU 90 % pendant dix minutes, mémoire 90 % pendant cinq
minutes et disque 85 % pendant cinq minutes, vers le destinataire personnel. Confirmer le
caractère gratuit des options affichées avant toute validation. Ne pas activer Uptime, une
base managée, des sauvegardes DigitalOcean ou une autre option facturée.

Ce complément observe les ressources hôte, pas le résultat des cycles Worker ou la validité
d’une sauvegarde. Il ne supprime pas la limite d’absence d’une sonde externe applicative.

## Validation avant activation

- Exécuter la quality gate .NET et Python, les vérifications du bundle et les exercices Docker.
- Vérifier les permissions et l’isolation des montages, les plafonds CPU/mémoire et les pannes
  simulées, sans saturation ni changement de secret en production.
- Garder les providers simulés dans la CI ; aucun test ne doit envoyer de vrais e-mails.
- Autoriser explicitement le contrôle de production à faible volume, le test Gmail et
  l’activation du timer. Consigner leurs résultats réels ; les tests simulés ne les remplacent pas.
