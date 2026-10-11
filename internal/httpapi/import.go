package httpapi

import (
	"github.com/JennGllg/mon-kado/internal/domain"
)

func (a *API) importPreview(r *request) (response, error) {
	input, err := body[domain.CreateWishImportPreviewRequest](r)
	if err != nil {
		return response{}, err
	}
	if err = a.Service.AuthorizeWishlist(r.Context(), r.Principal.MemberID, r.PathValue("wishlistId"), true); err != nil {
		return response{}, err
	}
	result, err := a.Importer.Preview(r.Context(), *input.Url)
	return response{Body: result}, err
}
