package validation

import (
	"net/url"
	"strings"

	"github.com/JennGllg/mon-kado/internal/domain"
)

// ActivityPage validates the server-approved page identifier; raw URLs are never accepted.
func ActivityPage(value string) (string, error) {
	labels := map[string]string{
		"home": "Accueil", "members": "Recherche de membres", "member-profile": "Profil d’un membre", "profile": "Mon profil", "personal-data": "Données personnelles", "authenticator": "Double authentification", "password-change": "Modification du mot de passe", "email-change": "Modification de l’adresse e-mail", "lists": "Mes listes", "extension-add": "Ajouter depuis l’extension", "followed-lists": "Listes suivies", "groups": "Mes groupes", "group-join": "Rejoindre un groupe", "group-invitation": "Invitation à un groupe", "group": "Groupe", "group-edit": "Modification d’un groupe", "group-member": "Membre du groupe", "group-wishlist": "Liste du groupe", "group-wish": "Souhait du groupe", "new-list": "Création d’une liste", "edit-list": "Modification d’une liste", "new-wish": "Création d’un souhait", "edit-wish": "Modification d’un souhait", "wish-details": "Détail d’un souhait", "list-details": "Détail d’une liste", "reservations": "Mes réservations", "reported-wishlists": "Listes signalées", "wishlist-report-review": "Examen d’un signalement", "wishlist-moderation": "Modération d’une liste", "wishlist-report-history": "Historique du signalement", "wishlist-moderation-history": "Historique de modération", "shared-wishlist": "Liste partagée", "shared-wish": "Souhait partagé", "activity": "Activité des membres", "admin-members": "Comptes",
	}
	if label, ok := labels[value]; ok {
		return label, nil
	}
	return "", &domain.Error{Status: 400, Code: domain.RequestValidationError, Message: "The request contains validation errors.", Validation: []domain.ValidationError{{PropertyName: "page", ErrorMessage: "The page is invalid."}}}
}

// ActivityQuery validates and aggregates activity filters.
func ActivityQuery(values url.Values) (string, string, string, error) {
	var query, kind, level string
	problems := []domain.ValidationError{}
	for _, key := range []string{"q", "kind", "logLevel"} {
		entries, ok := values[key]
		if !ok {
			continue
		}
		if len(entries) != 1 || !Text(entries[0], 100, false) {
			problems = append(problems, domain.ValidationError{PropertyName: key, ErrorMessage: "The activity filter is invalid."})
			continue
		}
		value := strings.TrimSpace(entries[0])
		if key == "logLevel" {
			value = canonicalValue(value, "info", "error")
		}
		if key == "q" {
			query = value
		} else if key == "kind" && (value == "" || value == "pageVisit" || value == "action") {
			kind = value
		} else if key == "logLevel" && (value == "" || value == "info" || value == "error") {
			level = value
		} else {
			problems = append(problems, domain.ValidationError{PropertyName: key, ErrorMessage: "The activity filter is invalid."})
		}
	}
	if len(problems) > 0 {
		return "", "", "", &domain.Error{Status: 400, Code: domain.RequestValidationError, Message: "The query contains validation errors.", Validation: problems}
	}
	return query, kind, level, nil
}
