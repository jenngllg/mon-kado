# Instructions du dépôt MonKado

## Style C#

- Nommer les champs privés non constants en `_camelCase`, qu'ils soient d'instance ou `static` ; nommer les constantes privées en `PascalCase` sans préfixe `_`.
- Pour une variable locale, faire primer `var` sur la création d'objet typée par la cible : écrire `var service = new Service()` plutôt que `Service service = new()` ; réserver `new()` sans type aux contextes où `var` n'est pas applicable, notamment les champs et propriétés.
- Insérer une ligne vide immédiatement avant chaque instruction `if`.
- Insérer une ligne vide immédiatement avant chaque instruction `return`, sauf lorsque le `return` est directement l'unique instruction d'un `if` sans accolades ; dans ce cas, ne pas séparer le `if` et le `return` par une ligne vide.
- Insérer une ligne vide immédiatement avant chaque instruction `throw`, sauf lorsque le `throw` est directement l'unique instruction d'un `if` sans accolades ; dans ce cas, ne pas séparer le `if` et le `throw` par une ligne vide.
- Préférer les sorties rapides (guard clauses) pour traiter les cas d'erreur ou particuliers et éviter les `else` ainsi que les imbrications inutiles.
- Utiliser l'opérateur ternaire uniquement pour des conditions simples et ne jamais imbriquer plusieurs opérateurs ternaires.
- Préférer LINQ aux boucles explicites pour filtrer, projeter, agréger ou transformer des collections, tant que l'expression reste lisible, et utiliser la syntaxe par méthodes (`Where`, `Select`, etc.) plutôt que la syntaxe de requête (`from`, `where`, `select`).
- Lorsqu'une chaîne LINQ comporte plusieurs opérations, placer chaque opération sur sa propre ligne, avec le point en début de ligne.
- Placer une seule action par ligne : ne pas regrouper plusieurs appels de méthode ou plusieurs instructions sur une même ligne, et placer chaque appel d'une chaîne fluide sur sa propre ligne.
- Dès qu'un appel de méthode ou de constructeur comporte plusieurs arguments, placer chaque argument sur sa propre ligne, même si l'appel tiendrait sur une seule ligne.
- Dès qu'une méthode, un constructeur ou un délégué comporte plusieurs paramètres, placer chaque paramètre sur sa propre ligne, même si la signature tiendrait sur une seule ligne.
- Dès qu'une collection contient plusieurs éléments, placer chaque élément sur sa propre ligne, y compris avec les expressions de collection modernes.
- Suffixer par `Async` le nom de toute méthode asynchrone, quelle que soit sa visibilité, y compris lorsqu'elle retourne directement un `Task` ou un `ValueTask` sans utiliser le mot-clé `async` ; conserver toutefois le nom imposé par une interface ou une classe de base externe, par exemple `IRequestHandler.Handle`.
- Toute méthode asynchrone qui effectue des entrées/sorties doit accepter un `CancellationToken` obligatoire, sans valeur par défaut, le placer en dernier paramètre et le transmettre à tous les appels asynchrones qui le permettent.
- Valider les commandes, requêtes et DTO entrants de la couche Application exclusivement avec des validateurs FluentValidation dédiés exécutés dans le pipeline commun, plutôt qu'avec des vérifications dispersées dans les handlers ou services ; après cette validation, ne pas répéter dans un handler des contrôles de présence ou de format tels que `string.IsNullOrWhiteSpace(request.Property)`.
- Déclarer comme nullables dans les DTO entrants les propriétés fournies par le client, y compris lorsqu'elles sont obligatoires fonctionnellement, et confier aux validateurs centralisés les règles de présence telles que `.NotNull()` ou `.NotEmpty()`, plutôt que d'utiliser le mot-clé `required`.
- Lorsqu'un DTO entrant contient un sous-objet, créer un validateur dédié à ce sous-objet et le composer dans le validateur parent avec `SetValidator(...)`, plutôt que de dupliquer ou d'imbriquer toutes ses règles dans le parent.
- Centraliser les modèles de messages de validation récurrents en anglais dans un unique fichier `ValidationMessages.cs` contenant des constantes, plutôt que de répéter les chaînes dans les validateurs ou d'utiliser des fichiers `.resx` tant que l'API ne gère pas elle-même plusieurs langues.
- Exécuter les validateurs dans le pipeline commun et agréger toutes les erreurs de validation au lieu de s'arrêter à la première.
- Exposer les erreurs HTTP avec un objet `ErrorResponse` contenant exactement `int StatusCode`, `string? Title`, `string? Message`, `string? ErrorCode` et `IEnumerable<ValidationError>? ValidationErrors` ; le statut et le code fonctionnel appartiennent à cette enveloppe, pas à chaque erreur de validation.
- Définir chaque `ValidationError` avec exactement `string? PropertyName` et `string? ErrorMessage`.
- Renseigner `ValidationError.PropertyName` avec le chemin JSON complet en `camelCase`, y compris le chemin des sous-objets et l'index des collections, par exemple `address.postalCode` ou `wishes[2].name`, indépendamment du texte personnalisé défini avec `WithMessage(...)`.
- Définir les DTO et modèles d'échange comme des classes immuables, et non comme des `record`.
- Ne pas déclarer les classes `sealed` par défaut ; réserver `sealed` aux cas où l'interdiction d'hériter est intentionnelle.
- Déclarer les types du projet `public` par défaut plutôt que `internal`, sauf raison explicite de restreindre leur visibilité.
- Définir une interface publique pour chaque service afin de pouvoir substituer ses dépendances et tester unitairement ses consommateurs, notamment avec `Mock<TInterface>`.
- Documenter en anglais avec des commentaires XML chaque interface de service, chaque implémentation de service et chacune de leurs méthodes, y compris lorsque l'implémentation ou la méthode n'est pas `public` ; fournir au minimum un `/// <summary>` concis et ajouter `<param>`, `<returns>` et `<exception>` lorsqu'ils sont applicables.
- Enregistrer les services applicatifs avec une durée de vie `Scoped` par défaut ; utiliser une autre durée de vie uniquement lorsqu'elle est explicitement justifiée.
- Centraliser la traduction des exceptions dans un gestionnaire ASP.NET Core nommé `GlobalExceptionHandler`, qui implémente `IExceptionHandler`, et retourner une réponse HTTP `400 Bad Request` contenant toutes les erreurs de validation.
- Faire confiance au conteneur d'injection de dépendances pour les services injectés et ne pas ajouter de garde `null` dans les constructeurs pour ces dépendances.
- Placer chaque type C# dans son propre fichier, y compris les petits types internes et les exceptions, et nommer le fichier comme le type.
- Faire une exception uniquement pour une commande ou une requête MediatR et son handler fortement couplé : les placer dans le même fichier, nommé d'après la commande ou la requête ; conserver les validateurs, exceptions, DTO et tous les autres types dans leurs propres fichiers.
- Ne pas utiliser l'opérateur null-forgiving (`!`) lorsqu'il n'est pas nécessaire ; privilégier un contrat de nullabilité et un flux de contrôle qui permettent au compilateur d'établir la non-nullité, et réserver `!` aux invariants réellement garantis qui ne peuvent pas être exprimés autrement.
- Appliquer ces conventions à tout code C# créé ou modifié, sans reformater des fichiers sans rapport avec la tâche en cours.

## Tests

- Créer une classe de tests unitaires par classe testée, la nommer `<Type>Tests`, par exemple `LoginCommandHandlerTests`, et placer cette classe dans un fichier portant le même nom ; reproduire dans chaque projet de tests l'arborescence et le namespace du projet testé.
- Nommer les méthodes de test selon la forme `Method_WhenCondition_ExpectedResult`, par exemple `LoginAsync_WhenCredentialsAreInvalid_ReturnsUnauthorized()`.
- Structurer chaque test avec les sections explicites `// Arrange`, `// Act` et `// Assert`.
- Autoriser plusieurs assertions dans un même test lorsqu'elles vérifient ensemble un seul résultat cohérent ; créer des tests séparés lorsque les assertions portent sur des comportements distincts.
- Utiliser un `[Theory]` avec plusieurs jeux de données lorsque le même comportement doit être vérifié avec différentes valeurs, plutôt que dupliquer des `[Fact]`.
- Utiliser `[InlineData]` pour les valeurs simples d'un `[Theory]` et `[MemberData]` pour les objets ou scénarios complexes.
- Créer les mocks Moq avec `MockBehavior.Strict` afin que tout appel non configuré fasse échouer le test.
- Mocker uniquement les dépendances externes ou produisant des effets de bord ; utiliser les implémentations réelles pour les validateurs, value objects et autres collaborateurs simples et déterministes.
- À la fin de la phase `Assert` de chaque test utilisant des mocks, appeler `VerifyNoOtherCalls()` sur chacun d'eux après avoir vérifié les interactions attendues.
- Vérifier les interactions attendues avec des appels `Verify(...)` explicites dans la section `Assert` ; ne pas utiliser `.Verifiable()` ni `VerifyAll()`.
- Initialiser dans le constructeur de la classe de tests les mocks communs et le sujet testé, et les conserver dans des champs privés en s'appuyant sur l'isolation par instance de xUnit.
- Nommer le champ du sujet testé d'après son rôle ou son type (`_handler`, `_emailService`, etc.) et ne pas utiliser le nom générique `_sut`.
- Suffixer par `Mock` le nom des champs contenant un `Mock<T>`, par exemple `_emailServiceMock`.
- Utiliser les assertions natives de xUnit v3 et ne pas ajouter Fluent Assertions ; créer au besoin des helpers d'assertion internes ciblés pour éviter les répétitions.
- Pour les tests d'intégration PostgreSQL, partager un conteneur Testcontainers par collection de tests et centraliser dans la fixture la migration initiale ainsi que la remise à zéro de la base entre les tests, plutôt que de démarrer un conteneur par test ou de répéter des commandes `TRUNCATE` dans chaque classe ; exécuter séquentiellement les tests d'une même collection qui partagent une base, tout en autorisant le parallélisme entre les collections utilisant des conteneurs distincts.
- Ne pas tester unitairement `DbContext`, `DbSet`, les mappings EF Core ni les repositories triviaux et ne pas les mocker ; vérifier par défaut la persistance de bout en bout dans les tests d'intégration de l'API ou du Worker avec un vrai PostgreSQL Testcontainers, et créer des tests dédiés à la couche de persistance uniquement lorsqu'ils sont nécessaires pour les migrations, une requête complexe ou un comportement de base difficile à couvrir autrement.
- Dans les tests d'intégration, vérifier par défaut les résultats au travers des contrats publics, par exemple en enchaînant un `POST` puis un `GET`, plutôt que d'inspecter systématiquement la base ; lire directement PostgreSQL uniquement dans des tests ciblés portant sur un invariant technique important qui n'est pas observable publiquement, tel que le hash d'un mot de passe, l'atomicité d'une outbox, un rollback ou une contrainte propre à PostgreSQL.
- Dans les tests d'intégration de l'API ou du Worker, remplacer les providers externes tels que Gmail par des fakes en mémoire afin de traverser tout le pipeline applicatif sans effectuer d'appel réseau réel.
- Utiliser AutoFixture pour créer les DTO et modèles de tests complets, et encapsuler les scénarios récurrents dans des classes statiques simples telles que `WishlistTestData`, plutôt que de multiplier les builders fluides et leurs méthodes `With...` ; créer une nouvelle instance configurée de `Fixture` par appel plutôt que de partager une instance statique mutable entre les tests, exposer des méthodes explicitement nommées selon leur scénario telles que `CreateRequestWithoutName()` plutôt qu'une méthode générique à nombreux paramètres optionnels, conserver explicites les valeurs qui déclenchent le comportement testé, placer les helpers partagés par plusieurs projets dans `Tests.Common` et garder localement ceux propres à un seul composant.
- Utiliser directement et explicitement `Fixture` à l'intérieur des classes `TestData` afin de garder la maîtrise de ses personnalisations ; créer les fixtures avec une méthode centrale `TestFixture.Create()` située dans `Tests.Common` pour appliquer les personnalisations communes telles que la génération de GUID v7, ajouter localement les personnalisations propres à chaque domaine, et ne pas utiliser `[AutoData]` ni `[InlineAutoData]` par défaut dans les méthodes de test.
- Ne pas utiliser AutoFixture pour générer automatiquement les mocks : continuer à créer explicitement les mocks Moq avec `MockBehavior.Strict` dans le constructeur de la classe de tests.
- Vérifier les contrats JSON et OpenAPI avec des assertions explicites sur les statuts, propriétés et schémas attendus plutôt qu'avec des snapshot tests fondés sur des fichiers de référence complets.
- Tester les méthodes privées uniquement à travers le comportement public de leur classe ; ne jamais utiliser la réflexion ni modifier la visibilité d'un membre uniquement pour le tester, sauf situation d'extrême urgence explicitement justifiée.
- Ne jamais utiliser `InternalsVisibleTo` pour exposer des types ou membres internes aux projets de tests ; tester les composants exclusivement à travers leurs contrats publics.
- Viser initialement une couverture de tests de `100 %` des lignes et des branches conditionnelles dans la quality gate ; exclure uniquement le code généré automatiquement, les migrations EF Core et les types purement descriptifs explicitement autorisés ci-dessous, ne réduire cet objectif que plus tard lorsqu'un cas concret démontre qu'il n'est pas pertinent, et documenter alors explicitement les autres exclusions retenues.
- Autoriser `[ExcludeFromCodeCoverage]` uniquement sur un type purement descriptif dépourvu de logique exécutable, par exemple un DTO, un modèle d'échange ou une classe de constantes ; ne pas exclure globalement un dossier ou une catégorie entière et conserver la couverture dès que le type contient un constructeur métier, une propriété calculée, une validation, un mapping ou tout autre comportement.
- Ne jamais appliquer `[ExcludeFromCodeCoverage]` aux commandes ni aux requêtes MediatR : elles doivent rester couvertes par les tests qui parcourent le flux du contrôleur jusqu'au handler.
- Ne pas configurer de retry automatique pour masquer les tests instables dans la CI ; lorsqu'un test échoue de façon intermittente sans modification du code, laisser la quality gate échouer et corriger la cause de cette instabilité.
- Ne pas utiliser `Task.Delay` ni d'attente temporelle réelle dans les tests dépendant du temps ; injecter un `TimeProvider` contrôlé par le test et faire avancer son horloge instantanément.
- Synchroniser les tests de workers et de traitements asynchrones avec des signaux déterministes tels que `TaskCompletionSource`, plutôt qu'avec des boucles de polling ou des délais arbitraires.
- Dans les tests unitaires des méthodes asynchrones, vérifier explicitement que le `CancellationToken` reçu est transmis sans substitution à toutes les dépendances qui l'acceptent.
- Transmettre `TestContext.Current.CancellationToken` à tous les appels asynchrones effectués par les tests xUnit afin que le runner puisse interrompre proprement leur exécution.

## API HTTP

- Lorsqu'un endpoint crée une ressource, retourner `201 Created`, renseigner l'en-tête HTTP `Location` avec l'URL permettant de récupérer cette ressource et inclure le DTO complet de la ressource créée dans le corps de la réponse.
- Lorsqu'un endpoint modifie une ressource avec `PUT` ou `PATCH`, retourner `200 OK` et inclure le DTO complet de la ressource mise à jour dans le corps de la réponse.
- Lorsqu'un endpoint supprime une ressource ou exécute une commande ne produisant aucun résultat utile, retourner `204 No Content` sans corps de réponse.
- Lorsqu'une ressource ciblée n'existe pas, y compris lors d'un `DELETE`, retourner `404 Not Found` avec un `ErrorResponse` ; le fait qu'un second `DELETE` retourne `404` ne remet pas en cause l'idempotence de la méthode.
- Lorsqu'une création ou une modification entre en conflit avec l'état actuel d'une ressource, par exemple lorsqu'une adresse e-mail est déjà inscrite, retourner `409 Conflict` avec un `ErrorResponse` plutôt que `400 Bad Request`.
- Retourner `401 Unauthorized` lorsque l'appelant n'est pas authentifié et `403 Forbidden` lorsqu'il est authentifié mais ne possède pas les autorisations nécessaires.
- Pour une ressource privée appartenant à un autre utilisateur, retourner `404 Not Found` plutôt que `403 Forbidden` afin de ne pas révéler son existence ; réserver `403 Forbidden` aux ressources dont l'existence peut être divulguée.
- Centraliser les règles d'accès aux ressources dans des `AuthorizationHandler` ASP.NET Core dédiés, plutôt que de les dupliquer dans les contrôleurs ou les handlers applicatifs ; adapter le résultat d'autorisation pour conserver une réponse `404 Not Found` lorsqu'une ressource privée appartient à un autre utilisateur.
- Utiliser des politiques d'autorisation nommées, référencées par des constantes telles que `AuthorizationPolicies.ManageWishlist`, plutôt que des vérifications directes de rôles dans les attributs ou le code applicatif ; centraliser toutes les constantes de politiques dans un unique fichier `AuthorizationPolicies.cs`, organisé avec une région par domaine fonctionnel.
- Renseigner un `ErrorCode` obligatoire, stable et exploitable par le front pour chaque erreur métier prévisible, par exemple `ACCOUNT_EMAIL_ALREADY_EXISTS`, afin que les consommateurs ne dépendent jamais du texte de `Message` ; conserver la propriété nullable dans `ErrorResponse` pour les erreurs techniques imprévues.
- Formater les codes d'erreur en `UPPER_SNAKE_CASE` et les préfixer par leur domaine fonctionnel, par exemple `ACCOUNT_EMAIL_ALREADY_EXISTS`, `WISHLIST_NOT_FOUND` ou `GIFT_RESERVATION_CONFLICT`.
- Centraliser tous les codes d'erreur dans un unique fichier `ErrorCodes.cs` contenant des constantes C# ; organiser ce fichier avec une région explicite par domaine fonctionnel, par exemple `Account`, `Wishlist` ou `Gift`, plutôt que de répartir les codes dans plusieurs types ou de les placer dans des fichiers de ressources.
- Rédiger en anglais les propriétés `Title` et `Message` des réponses d'erreur ; considérer `ErrorCode` comme le contrat stable permettant au front de traduire les messages destinés à l'utilisateur.
- Sérialiser en JSON les propriétés exposées par l'API en `camelCase`, par exemple `statusCode`, `errorCode` et `validationErrors`, tout en conservant les noms C# en `PascalCase`.
- Sérialiser en JSON les valeurs d'enums exposées par l'API sous forme de chaînes lisibles en `camelCase`, par exemple `EmailNotConfirmed` devient `"emailNotConfirmed"`, et configurer `JsonStringEnumConverter` avec `allowIntegerValues: false` afin de refuser les représentations numériques.
- Retourner `400 Bad Request` avec un `ErrorResponse` lorsqu'une valeur d'enum JSON est inconnue : laisser la désérialisation rejeter les chaînes inconnues avant le pipeline FluentValidation, puis utiliser les validateurs pour vérifier la présence de la valeur et les restrictions métier applicables aux valeurs correctement désérialisées.
- Inclure dans le JSON les propriétés nullables même lorsqu'elles valent `null`, plutôt que de les omettre de la réponse.
- Utiliser `DateTime` plutôt que `DateTimeOffset` pour les dates et heures métier, et toujours les stocker, manipuler et exposer en UTC.
- Nommer les propriétés temporelles sans suffixe `Utc`, par exemple `CreatedAt` et `UpdatedAt`, puisque toutes les dates et heures sont déjà définies comme étant en UTC.
- Injecter le `TimeProvider` natif de .NET dans les services qui ont besoin de l'heure actuelle, plutôt que d'appeler directement `DateTime.UtcNow` ou de créer une abstraction personnalisée ; obtenir un `DateTime` UTC avec `timeProvider.GetUtcNow().UtcDateTime`.
- Versionner l'API dès sa première version et porter la version dans l'URL, par exemple `/api/v1/wishlists`, plutôt que dans un en-tête HTTP.
- Concevoir les routes REST avec des noms de ressources au pluriel et sans verbes, par exemple `/api/v1/wishlists/{id}/wishes`, en laissant le verbe HTTP exprimer l'opération ; réserver les verbes dans l'URL aux véritables actions métier qui ne correspondent pas naturellement à un CRUD, comme `confirm` ou `reserve`.
- Conserver dans l'URL le parent d'une ressource qui ne peut pas être manipulée indépendamment, même lorsque ses identifiants sont globalement uniques ; accéder ainsi à un souhait par `/api/v1/wishlists/{wishlistId}/wishes/{wishId}` et rechercher la ressource avec les deux identifiants, sans considérer le GUID comme un mécanisme d'autorisation.
- Déclarer les actions de contrôleur qui renvoient un DTO avec `ActionResult<TDto>`, ou `Task<ActionResult<TDto>>` lorsqu'elles sont asynchrones, plutôt qu'avec `IActionResult`, afin d'exposer précisément leur contrat de réponse et d'améliorer la documentation OpenAPI.
- Déclarer les actions de contrôleur qui ne renvoient aucun corps avec `IActionResult`, ou `Task<IActionResult>` lorsqu'elles sont asynchrones, et documenter explicitement avec `[ProducesResponseType]` leurs statuts HTTP et schémas de réponse métier spécifiques ; conserver également cette documentation explicite pour les actions retournant `ActionResult<TDto>`.
- Écrire chaque attribut `[ProducesResponseType(...)]` intégralement sur une seule ligne, paramètres compris, même lorsqu'il contient plusieurs arguments ; cette exception prévaut sur la règle générale qui place chaque argument sur sa propre ligne.
- Centraliser les réponses OpenAPI communes dans un `IOpenApiOperationTransformer` dédié : ajouter `500 Internal Server Error` à toutes les opérations et ajouter conditionnellement `401 Unauthorized` et `403 Forbidden` aux opérations protégées, plutôt que de répéter ces attributs sur chaque action.
- Documenter en anglais avec des commentaires XML tous les types et membres `public`, y compris chaque action de contrôleur ; fournir au minimum un `/// <summary>` concis, ajouter systématiquement `<param>`, `<returns>` et `<exception>` lorsqu'ils sont applicables, et utiliser la documentation des endpoints pour alimenter leur description OpenAPI.
- Réserver les commentaires dans le corps du code à l'explication du « pourquoi » lorsqu'une décision n'est pas évidente ; ne pas commenter ce que le code exprime déjà clairement.
- Associer chaque commentaire `TODO` à un ticket identifiable, par exemple `// TODO(#815): add structured logging`, afin qu'aucun travail différé ne reste sans suivi.
- Remplacer les chaînes et nombres métier répétés ou peu explicites par des constantes nommées ; conserver directement les valeurs évidentes telles que `0`, `1` ou `100` lorsqu'elles restent claires dans leur contexte.

## Journalisation

- Écrire des logs structurés avec des modèles de message stables et des propriétés nommées, par exemple `logger.LogInformation("Wishlist {WishlistId} created", wishlistId)`, plutôt qu'avec des chaînes interpolées ou concaténées.
- Définir tous les logs applicatifs avec des méthodes `partial` décorées par `[LoggerMessage]` afin de générer leur implémentation à la compilation, de typer leurs paramètres et de leur attribuer un `EventId` stable ; regrouper ces méthodes dans des classes de journalisation dédiées plutôt que d'appeler directement les extensions `ILogger.LogInformation(...)`, `LogWarning(...)` ou `LogError(...)` depuis les services.
- Garantir l'unicité des `EventId` dans toute l'application en réservant une plage numérique par domaine fonctionnel, par exemple `1000` à `1999` pour `Account` et `2000` à `2999` pour `Wishlist` ; centraliser toutes leurs constantes dans un unique fichier `LogEventIds.cs`, organisé avec une région par domaine.
- Journaliser au niveau `Error` les réponses HTTP métier en échec, notamment `400 Bad Request`, `401 Unauthorized`, `403 Forbidden`, `404 Not Found`, `409 Conflict`, `412 Precondition Failed` et `429 Too Many Requests`, même lorsqu'elles proviennent d'une requête client attendue, sans inclure les valeurs sensibles reçues.
- Journaliser au niveau `Information` les opérations métier importantes qui réussissent, notamment la création d'une liste ou la réservation d'un cadeau.
- Pour les lectures `GET` réussies, conserver le log HTTP de la requête et ajouter également un log métier au niveau `Information` dans la couche Application.
- Inclure obligatoirement dans tous les logs, au moyen d'un scope commun, le `CorrelationId` fonctionnel et le `TraceId` W3C destiné au traçage distribué ; retourner le `CorrelationId` au client dans l'en-tête de réponse `X-Correlation-ID`, conserver la valeur valide fournie par le client et en générer une nouvelle uniquement lorsque l'en-tête est absent ou invalide.
- Appliquer une liste blanche stricte aux données journalisées : autoriser les identifiants techniques nécessaires tels que `UserId` ou `WishlistId`, et ne jamais journaliser les mots de passe, jetons, secrets, adresses e-mail, corps de requête ni contenu des souhaits.
- Journaliser chaque exception non gérée une seule fois à la frontière de l'application dans le `GlobalExceptionHandler` ; ne pas reloguer la même exception dans les couches qu'elle traverse.
- Journaliser au niveau `Error` les erreurs métier attendues telles que `404 Not Found` ou `409 Conflict` sans stack trace, et inclure la stack trace complète uniquement pour les exceptions techniques inattendues.

## Providers externes

- Centraliser les délais d'expiration, stratégies de retry et circuit breakers des appels HTTP aux providers externes avec les mécanismes de résilience HTTP natifs de .NET, plutôt que d'implémenter ces comportements séparément dans chaque service.
- Limiter les retries HTTP automatiques aux méthodes idempotentes et aux erreurs transitoires telles que `408`, `429`, certains `5xx` et les erreurs réseau ; ne pas rejouer les erreurs client non transitoires telles que `400`, `401`, `403` ou `404`, ni un `POST` sauf si le provider garantit officiellement une clé d'idempotence.
- Ne pas appliquer de retry HTTP transparent à l'envoi Gmail, car `users.messages.send` est un `POST` sans clé d'idempotence documentée ; piloter les nouvelles tentatives explicitement dans l'outbox avec un compteur, une prochaine date de tentative, un nombre maximal d'essais et un délai exponentiel, afin de conserver la traçabilité de chaque tentative.
- Encapsuler chaque provider HTTP externe dans un client typé dédié exposé par une interface, par exemple `IGmailApiClient` et `GmailApiClient`, plutôt que d'utiliser directement un client nommé obtenu depuis `IHttpClientFactory` dans les services consommateurs ; placer l'interface dans `Application/Abstractions` et l'implémentation dans le projet d'infrastructure du provider afin que la couche Application ne dépende pas de son SDK.
- Confiner tous les types provenant du SDK d'un provider externe à son projet d'infrastructure et les convertir en modèles propres à MonKado avant de retourner un résultat à la couche Application.
- Tester les clients de providers avec des tests unitaires utilisant un `HttpMessageHandler` simulé et sans appel réseau réel ; réserver les appels au vrai provider à des tests d'intégration séparés et explicitement activés.
- Configurer les tests d'intégration d'un provider avec un fichier `appsettings.IntegrationTests.json` propre à leur projet et une classe d'options fortement typée ; désactiver les appels externes par défaut avec une option `Enabled` à `false`, autoriser les variables d'environnement à surcharger la configuration et y conserver exclusivement les secrets et données personnelles, puis valider les options requises uniquement lorsque ces tests sont activés.
- Exclure de la quality gate des pull requests les tests qui appellent réellement un provider externe tel que Gmail ; les exécuter uniquement depuis un workflow manuel sécurisé disposant des secrets nécessaires.
- Définir les timeouts, nombres maximaux de tentatives et délais de résilience dans des classes d'options liées à la configuration `appsettings`, et valider ces options au démarrage de l'application plutôt que de coder les valeurs en dur.
- Définir les propriétés des classes d'options avec des accesseurs `init` afin que le binding puisse les initialiser sans permettre leur modification ultérieure par le code applicatif.
- Injecter les configurations avec `IOptions<TOptions>` et considérer leur valeur comme stable pendant toute l'exécution ; ne pas utiliser `IOptionsSnapshot<TOptions>` sans besoin explicite de rechargement à chaud.
- Ne jamais stocker les secrets, notamment les identifiants Gmail et les chaînes de connexion, dans les fichiers `appsettings*.json` versionnés ; utiliser les User Secrets en environnement local, puis des variables d'environnement ou un gestionnaire de secrets en production.

## Persistance

- Utiliser des identifiants `Guid` v7 pour les nouvelles entités et les générer dans l'application avec l'API native de .NET avant la persistance, plutôt que d'utiliser des GUID v4 ou des identifiants numériques générés par PostgreSQL.
- Effectuer une suppression physique par défaut ; n'utiliser le soft delete que lorsqu'un besoin métier explicite de restauration, d'historique ou d'audit le justifie, en prenant la décision entité par entité plutôt qu'en l'imposant globalement ; traiter l'anonymisation des comptes utilisateur comme un mécanisme distinct.
- Identifier les entités auditées au moyen d'une interface `IAuditableEntity`, sans leur imposer de classe de base, et renseigner automatiquement `CreatedAt` et `UpdatedAt` au moyen d'un interceptor EF Core utilisant le `TimeProvider` injecté plutôt que depuis leurs méthodes métier ; exposer `CreatedAt` avec un setter privé et `UpdatedAt` comme un `DateTime?` avec un setter privé, laisser `UpdatedAt` à `null` lors de la création et ne le renseigner qu'à la première modification.
- Appliquer la concurrence optimiste uniquement aux entités susceptibles d'être modifiées simultanément, sans l'imposer à toutes les entités ; utiliser la colonne système PostgreSQL `xmin` comme jeton `uint Version` configuré avec `.IsRowVersion()`, exposer cette version sous forme d'`ETag`, exiger `If-Match` pour les modifications et suppressions concernées et retourner `412 Precondition Failed` lorsque la version est périmée ; conserver les contraintes et transactions PostgreSQL pour garantir les invariants critiques tels que l'unicité d'une réservation.
- Définir dans `Application/Abstractions` une interface `IUnitOfWork` exposant uniquement `Task<int> SaveChangesAsync(CancellationToken cancellationToken)` ; ne pas lui faire hériter de `IDisposable` et ne pas ajouter de méthodes `GetRepository`, `Commit` ou `Rollback`.
- Faire implémenter directement `IUnitOfWork` par le `MonKadoDbContext` existant ; ne pas créer de classe ou de wrapper `UnitOfWork` supplémentaire.
- Injecter explicitement les interfaces des repositories spécialisés dans les services ou handlers qui les consomment afin que leurs dépendances restent visibles.
- Autoriser les repositories spécialisés à exposer un `IQueryable<TEntity>` au moyen d'une méthode `Query()` appliquant `AsNoTracking()` par défaut pour les lectures ; utiliser des méthodes explicites telles que `GetByIdForUpdateAsync()` lorsqu'une entité trackée doit être modifiée.
- Ne pas imposer la pagination à toutes les collections ; la mettre en place lorsque la collection peut devenir volumineuse, notamment pour les souhaits d'une liste, et utiliser alors une pagination classique fondée sur `page` et `pageSize`, indexée à partir de `1`, avec `pageSize = 20` par défaut et `100` au maximum, plutôt qu'une pagination par curseur.
- Valider les paramètres de pagination avec les validateurs du pipeline : `page` doit être supérieur ou égal à `1` et `pageSize` doit être compris entre `1` et `100` ; retourner une erreur HTTP `400 Bad Request` lorsqu'une valeur fournie est hors limites, et appliquer les valeurs par défaut uniquement lorsque les paramètres sont absents.
- Retourner les résultats paginés dans une classe générique immuable nommée `PaginatedResponse<T>`, contenant `IEnumerable<T> Items`, `int CurrentPage`, `int PageSize`, `int TotalCount`, `int TotalPages` ainsi que les propriétés calculées en lecture seule `bool HasPreviousPage` et `bool HasNextPage`.
- Lorsque `TotalCount` vaut `0`, retourner `TotalPages = 0`, conserver `CurrentPage = 1` pour la première page demandée et définir `HasPreviousPage` ainsi que `HasNextPage` à `false`.
- Lorsqu'une page valide est supérieure à `TotalPages`, retourner `200 OK` avec `Items` vide, conserver le numéro demandé dans `CurrentPage` et retourner les métadonnées réelles, sans normaliser la page ni produire d'erreur.
- Ne pas imposer de tri stable à toutes les requêtes paginées ; ajouter un ordre explicite uniquement lorsque le contrat fonctionnel de l'endpoint l'exige.
- Ne jamais appeler `SaveChanges()` ni `SaveChangesAsync()` depuis un repository ; coordonner toutes les modifications d'une opération métier puis appeler une seule fois `unitOfWork.SaveChangesAsync(cancellationToken)` afin de les valider atomiquement.
- Enregistrer `MonKadoDbContext`, `IUnitOfWork` et ses repositories avec une durée de vie `Scoped` afin qu'ils partagent le même contexte pendant une requête, puis laisser le conteneur d'injection disposer du contexte.

## Organisation du dépôt

- À l'intérieur de chaque projet, organiser les dossiers par responsabilité technique (`Commands`, `Queries`, `Validators`, `Services`, etc.) plutôt que par fonctionnalité métier ; placer le handler d'une commande ou d'une requête MediatR dans le même fichier que celle-ci, au sein de son dossier `Commands` ou `Queries`.
- Ranger les interfaces de services dans un dossier technique `Abstractions` et utiliser le namespace correspondant.
- Ranger les implémentations de services dans un dossier technique `Services` et utiliser le namespace correspondant, plutôt que dans un dossier racine nommé d'après un provider.
- Dans chaque projet, centraliser ses enregistrements d'injection de dépendances dans un dossier technique `Configurations` et exposer une méthode d'extension nommée `Configure<Project>Injection`, par exemple `ConfigureApplicationInjection()` ou `ConfigureDomainInjection()`.
- Utiliser le projet API comme composition root : y appeler les méthodes `Configure<Project>Injection()` de chaque projet et conserver `Program.cs` aussi concis que possible.
- Conserver des projets de tests séparés par composant et par nature, avec des noms tels que `Api.UnitTests`, `Api.IntegrationTests` et `Api.FunctionalTests`.
- Considérer Google OAuth, Gmail et tout service tiers appelé par le code comme des providers externes.
- Placer sous `src/` tout projet ou outil qui appelle directement un provider externe, y compris les exécutables de bootstrap ou d'administration propres à ce provider.
- Conserver ces outils sous forme de projets autonomes plutôt que comme des fichiers isolés.
- Lorsqu'un projet est déplacé sous `src/`, effectuer un déplacement complet et cohérent : renommer le dossier, le fichier projet, l'assembly, le namespace racine, les namespaces du code et les références de solution concernées.
- Ne pas déplacer `GmailOAuthBootstrap` tant que Jenn ne le demande pas explicitement.

## Git et pull requests

- Rédiger en anglais les messages de commit ainsi que les titres et descriptions des pull requests.
- Ne jamais ajouter, committer ni pousser les plans d'implémentation dans le dépôt ; conserver les fichiers `*-implementation-plan.md` uniquement en local parmi les fichiers ignorés par Git.

## Gestion des conventions

- Pour chaque nouvelle règle donnée par Jenn, vérifier d'abord si elle peut être exprimée par une option standard de `.editorconfig`.
- Si l'option existe, ajouter ou mettre à jour la règle dans `.editorconfig` sans la dupliquer ici.
- Si aucune option standard ne permet de l'exprimer, documenter la règle dans ce fichier.
- Vérifier qu'une nouvelle préférence ne contredit pas une configuration existante avant de l'appliquer.

## Préférences évolutives

- Lorsqu'un choix de style ou de conception récurrent n'est pas encore documenté, demander à Jenn sa préférence au moment où ce choix se présente.
- Poser une seule question courte à la fois et, si cela aide, montrer deux petits exemples concrets.
- Ne pas demander toutes les préférences à l'avance et ne pas interrompre le travail pour des détails sans conséquence.
- Après chaque réponse, ajouter la préférence retenue dans ce fichier afin de ne pas reposer la même question.
- Avant d'appliquer largement une nouvelle convention à plusieurs fichiers, obtenir d'abord la préférence de Jenn.
