package application

import (
	"fmt"
	"strings"
	"time"
)

func composeEmail(kind string, message EmailMessage, frontend string, exportLifetime time.Duration) EmailMessage {
	if strings.HasPrefix(message.Text, frontend+"/") {
		message.ActionURL = message.Text
	}
	message.Subject = "Notification de sécurité – MonKado"
	message.Text = "Une opération de sécurité a été effectuée sur votre compte MonKado."
	switch kind {
	case "EMAIL_CONFIRMATION":
		message.Subject = "Confirmez votre adresse e-mail – MonKado"
		message.Text = "Bienvenue sur MonKado. Confirmez votre adresse e-mail pour activer votre compte :\n" + message.ActionURL + "\n\nSi vous n'avez pas créé ce compte, ignorez cet e-mail."
	case "PASSWORD_RESET":
		message.Subject = "Réinitialisez votre mot de passe – MonKado"
		message.Text = "Vous avez demandé la réinitialisation de votre mot de passe. Choisissez un nouveau mot de passe sur cette page :\n" + message.ActionURL + "\n\nSi vous n'êtes pas à l'origine de cette demande, ignorez cet e-mail."
	case "EMAIL_CHANGE_CONFIRMATION":
		message.Subject = "Confirmez votre nouvelle adresse e-mail – MonKado"
		message.Text = "Confirmez le changement d'adresse e-mail de votre compte :\n" + message.ActionURL + "\n\nSi vous n'êtes pas à l'origine de cette demande, ignorez cet e-mail."
	case "ACCOUNT_DELETION_CONFIRMATION":
		message.Subject = "Confirmez la suppression de votre compte – MonKado"
		message.Text = "Vous avez demandé la suppression définitive de votre compte, de vos listes et de votre historique. Vos réservations seront annulées. Connectez-vous au même compte puis confirmez sur cette page :\n" + message.ActionURL + "\n\nSi vous n'êtes pas à l'origine de cette demande, ignorez cet e-mail. L'ouverture du lien ne supprime rien."
	case "PERSONAL_DATA_EXPORT_READY":
		message.Subject = "Votre export de données personnelles est prêt – MonKado"
		message.ActionURL = frontend + "/profile"
		message.Text = fmt.Sprintf("Votre export privé est prêt. Il expire après %.0f heures. Téléchargez-le depuis votre profil :\n%s", exportLifetime.Hours(), message.ActionURL)
	case "PASSWORD_CHANGED_SECURITY_NOTIFICATION":
		message.Text = "Le mot de passe de votre compte MonKado a été modifié."
	case "TWO_FACTOR_ENROLLED":
		message.Text = "La double authentification a été activée sur votre compte MonKado."
	case "TWO_FACTOR_REPLACED":
		message.Text = "L'authenticator de votre compte MonKado a été remplacé."
	case "TWO_FACTOR_RECOVERY_CODE_USED":
		message.Text = "Un code de récupération de votre compte MonKado a été utilisé."
	case "TWO_FACTOR_RECOVERY_CODES_REGENERATED":
		message.Text = "Les codes de récupération de votre compte MonKado ont été régénérés."
	}
	if strings.Contains(kind, "SECURITY_NOTIFICATION") || strings.HasPrefix(kind, "TWO_FACTOR_") {
		message.Text += "\n\nSi vous n'êtes pas à l'origine de cette opération, contactez immédiatement l'assistance MonKado. Ne communiquez jamais vos codes d'authentification ou de récupération."
	}
	message.Text = "Bonjour,\n\n" + message.Text
	return message
}
