package httpapi

import (
	"html"
	"net/http"
	"strings"
)

func (a *API) previewRoutes() {
	for _, method := range []string{"GET", "HEAD"} {
		a.route(method+" /api/v1/shared-wishlists/{shareLinkId}/preview", options{Rate: 60}, a.sharePreview)
		a.route(method+" /api/v1/shared-wishlists/{shareLinkId}/preview/image", options{Rate: 60}, a.sharePreview)
	}
}

func (a *API) sharePreview(r *request) (response, error) {
	link := r.PathValue("shareLinkId")
	image := strings.HasSuffix(r.URL.Path, "/image")
	preview, err := a.Service.PublicSharePreview(r.Context(), link, image)
	if err != nil {
		return response{}, err
	}
	if image {
		result := response{Binary: preview.JPEG, MediaType: "image/jpeg", ContentLength: int64(len(preview.JPEG))}
		if r.Method == http.MethodHead {
			result.Binary = []byte{}
		}
		return result, nil
	}
	title := html.EscapeString(preview.Name + " | Ma liste sur MonKado")
	canonical := html.EscapeString(a.Service.Config.FrontendOrigin + "/shared-wishlists/" + link)
	graphImage := ""
	if preview.HasImages {
		graphImage = `<meta property="og:image" content="` + html.EscapeString(a.Service.Config.FrontendOrigin+"/share-previews/"+link+"/image") + `"><meta property="og:image:type" content="image/jpeg"><meta property="og:image:width" content="1200"><meta property="og:image:height" content="630"><meta property="og:image:alt" content="Aperçu des souhaits de cette liste">`
	}
	content := []byte(`<!doctype html><html lang="fr" prefix="og: https://ogp.me/ns#"><head><meta charset="utf-8"><title>` + title + `</title><meta property="og:type" content="website"><meta property="og:site_name" content="MonKado"><meta property="og:title" content="` + title + `"><meta property="og:url" content="` + canonical + `"><meta property="og:locale" content="fr_FR"><meta property="og:description" content="Découvre cette liste de souhaits sur MonKado.">` + graphImage + `</head><body><h1>` + title + `</h1></body></html>`)
	result := response{Binary: content, MediaType: "text/html; charset=utf-8", ContentLength: int64(len(content))}
	if r.Method == http.MethodHead {
		result.Binary = []byte{}
	}
	return result, nil
}
