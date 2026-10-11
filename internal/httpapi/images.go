package httpapi

import (
	"io"
	"mime"
	"net/http"
	"reflect"
	"strings"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/images"
	"github.com/google/uuid"
)

func (a *API) imageRoutes() {
	for _, method := range []string{"PUT", "DELETE"} {
		a.route(method+" /api/v1/wishlists/{wishlistId}/wishes/{wishId}/image", options{Auth: true, Rate: 10}, a.wishImage)
	}
	a.route("GET /api/v1/wishlists/{wishlistId}/wishes/{wishId}/image", options{}, a.readWishImage)
	a.route("GET /api/v1/shared-wishlists/{shareLinkId}/wishes/{wishId}/image", options{}, a.readWishImage)
	for _, method := range []string{"PUT", "DELETE"} {
		a.route(method+" /api/v1/members/current/profile/image", options{Auth: true, Rate: 10}, a.profileImage)
	}
	a.route("GET /api/v1/members/{memberId}/profile/image", options{Rate: 120}, a.readProfileImage)
}

func imageUpload(r *request) ([]byte, error) {
	mediaType, _, err := mime.ParseMediaType(r.Header.Get("Content-Type"))
	if err != nil || mediaType != "multipart/form-data" {
		return nil, domain.Failure(415, domain.RequestUnsupportedMediaType, "The request must use multipart/form-data.")
	}
	const limit = images.MaximumInputBytes + 64*1024
	if r.ContentLength > limit {
		return nil, domain.Failure(413, domain.RequestPayloadTooLarge, "The image upload exceeds the allowed size.")
	}
	r.Body = http.MaxBytesReader(nil, r.Body, limit)
	reader, err := r.MultipartReader()
	if err != nil {
		return nil, domain.Failure(400, domain.RequestBadRequest, "The multipart body is invalid.")
	}
	part, err := reader.NextPart()
	if err != nil || part.FormName() != "image" || part.FileName() == "" {
		return nil, domain.Failure(400, domain.RequestValidationError, "Supply exactly one non-empty image field.")
	}
	content, err := io.ReadAll(io.LimitReader(part, images.MaximumInputBytes+1))
	part.Close()
	if err != nil {
		return nil, domain.Failure(400, domain.RequestBadRequest, "The image upload could not be read.")
	}
	if len(content) > images.MaximumInputBytes {
		return nil, domain.Failure(413, domain.RequestPayloadTooLarge, "The image exceeds 10 MiB.")
	}
	if _, err = reader.NextPart(); err != io.EOF {
		return nil, domain.Failure(400, domain.RequestValidationError, "Supply exactly one image field without other fields.")
	}
	return content, nil
}

func (a *API) wishImage(r *request) (response, error) {
	v, err := entityTag(r, true)
	if err != nil {
		return response{}, err
	}
	owner, list, wish := r.Principal.MemberID, r.PathValue("wishlistId"), r.PathValue("wishId")
	if r.Method == "DELETE" {
		version, err := a.Service.DeleteWishImage(r.Context(), owner, list, wish, *v)
		return response{Status: 204, Version: &version}, err
	}
	content, err := imageUpload(r)
	if err != nil {
		return response{}, err
	}
	result, version, err := a.Service.UpsertWishImage(r.Context(), owner, list, wish, content, *v)
	return response{Body: result, Version: &version}, err
}

func (a *API) readWishImage(r *request) (response, error) {
	content, _, err := a.Service.ReadWishImage(r.Context(), r.PathValue("wishlistId"), r.PathValue("shareLinkId"), r.PathValue("wishId"), r.URL.Query().Get("token"))
	return response{Binary: content, MediaType: "image/webp"}, err
}

func (a *API) profileImage(r *request) (response, error) {
	v, err := entityTag(r, true)
	if err != nil {
		return response{}, err
	}
	var content []byte
	if r.Method == "PUT" {
		content, err = imageUpload(r)
		if err != nil {
			return response{}, err
		}
	}
	result, version, err := a.Service.ProfileImage(r.Context(), r.Principal.MemberID, content, *v, r.Method == "DELETE")
	if r.Method == "DELETE" {
		return response{Status: 204, Version: &version}, err
	}
	return response{Body: result, Version: &version}, err
}

func (a *API) readProfileImage(r *request) (response, error) {
	id, err := uuid.Parse(r.URL.Query().Get("imageId"))
	if err != nil || id == uuid.Nil {
		return response{}, domain.Failure(404, domain.ProfileImageNotFound, "The profile image was not found.")
	}
	content, _, err := a.Service.ReadProfileImage(r.Context(), r.PathValue("memberId"), id.String())
	return response{Binary: content, MediaType: "image/webp"}, err
}

func absoluteImageURL(r *http.Request, value *string, scheme string) {
	if value == nil || !strings.HasPrefix(*value, "/") {
		return
	}
	*value = scheme + "://" + r.Host + *value
}

func (a *API) absoluteImageURLs(r *http.Request, body any) {
	scheme := a.requestScheme(r)
	var visit func(reflect.Value)
	visit = func(value reflect.Value) {
		if !value.IsValid() {
			return
		}
		switch value.Kind() {
		case reflect.Pointer, reflect.Interface:
			if !value.IsNil() {
				visit(value.Elem())
			}
		case reflect.Slice, reflect.Array:
			for i := 0; i < value.Len(); i++ {
				visit(value.Index(i))
			}
		case reflect.Struct:
			for i := 0; i < value.NumField(); i++ {
				field := value.Field(i)
				if !field.CanInterface() {
					continue
				}
				name := value.Type().Field(i).Name
				if name == "ImageUrl" || name == "ProfileImageUrl" {
					if url, ok := field.Interface().(*string); ok {
						absoluteImageURL(r, url, scheme)
					}
				} else {
					visit(field)
				}
			}
		}
	}
	visit(reflect.ValueOf(body))
}
