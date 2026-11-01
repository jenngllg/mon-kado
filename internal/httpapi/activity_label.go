package httpapi

import "strings"

// activityLabel uses only registered route templates and never request paths or values.
func activityLabel(pattern string) string {
	method, path, _ := strings.Cut(pattern, " ")
	verbs := map[string]string{"GET": "Consultation", "POST": "Envoi", "PUT": "Modification", "PATCH": "Modification", "DELETE": "Suppression"}
	nouns := map[string]string{
		"wishlists": "listes", "wishes": "souhaits", "groups": "groupes", "members": "membres", "current": "actuel", "reservations": "réservations", "suggestions": "suggestions", "invitations": "invitations", "images": "images", "image": "image", "profile": "profil", "subscriptions": "abonnements", "share-links": "liens de partage", "data-exports": "exports de données", "reported-wishlists": "listes signalées", "moderation": "modération", "reports": "signalements", "reviews": "examens", "admin": "administration", "two-factor": "double authentification",
	}
	parts := []string{}
	for _, segment := range strings.Split(strings.TrimPrefix(path, "/api/v1/"), "/") {
		if segment == "" || strings.HasPrefix(segment, "{") {
			continue
		}
		if label, ok := nouns[segment]; ok {
			segment = label
		}
		parts = append(parts, segment)
	}
	return verbs[method] + " · " + strings.Join(parts, " / ")
}
