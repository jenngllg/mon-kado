package httpapi

import (
	"bytes"
	_ "embed"
	"html/template"
	"strings"

	"github.com/JennGllg/mon-kado/internal/validation"
)

//go:embed assets/group-invitation.png
var groupInvitationLogo []byte

var groupInvitationHTML = template.Must(template.New("groupInvitation").Parse(`<!doctype html>
<html lang="fr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>{{.Name}} · MonKado</title><meta name="robots" content="noindex,nofollow,noarchive">
<meta property="og:type" content="website"><meta property="og:site_name" content="MonKado">
<meta property="og:title" content="Rejoins le groupe « {{.Name}} » sur MonKado">
<meta property="og:description" content="Une invitation à partager vos envies et préparer vos cadeaux ensemble.">
<meta property="og:url" content="{{.URL}}"><meta property="og:image" content="{{.Image}}">
<meta property="og:image:alt" content="Invitation au groupe {{.Name}}">
<meta name="twitter:card" content="summary_large_image"><meta name="twitter:title" content="Rejoins le groupe « {{.Name}} » sur MonKado">
<meta name="twitter:description" content="Une invitation à partager vos envies et préparer vos cadeaux ensemble.">
<meta name="twitter:image" content="{{.Image}}"></head><body><h1>{{.Name}}</h1><p>Invitation à rejoindre ce groupe sur MonKado.</p></body></html>`))

func (a *API) previewGroupCode(r *request) (response, error) {
	code, err := validation.GroupJoinCode(r.Header.Get(shareTokenHeader))
	if err != nil {
		return response{}, err
	}
	preview, err := a.Service.GroupJoinPreview(r.Context(), "", code)
	return response{Body: preview}, err
}

func (a *API) previewGroupInvitation(r *request) (response, error) {
	preview, err := a.Service.GroupJoinPreview(r.Context(), r.PathValue("groupId"), "")
	if err != nil {
		return response{}, err
	}
	if strings.Contains(r.Header.Get("Accept"), "application/json") {
		return response{Body: preview}, nil
	}
	var html bytes.Buffer
	err = groupInvitationHTML.Execute(&html, struct{ Name, URL, Image string }{
		preview.Name,
		a.Service.Config.FrontendOrigin + "/groups/join/" + preview.Id,
		a.Service.Config.FrontendOrigin + "/group-previews/" + preview.Id + "/image",
	})
	return response{Binary: html.Bytes(), MediaType: "text/html; charset=utf-8"}, err
}

func (a *API) previewGroupInvitationImage(r *request) (response, error) {
	content, err := a.Service.ReadGroupJoinPreviewImage(r.Context(), r.PathValue("groupId"))
	if err != nil {
		return response{}, err
	}
	if content == nil {
		return response{Binary: groupInvitationLogo, MediaType: "image/png"}, nil
	}
	return response{Binary: content, MediaType: "image/webp"}, nil
}
