package httpapi

import (
	"context"
	"encoding/base64"
	"net/url"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/images"
)

// SelectedImageResolver obtains a bounded image selected by a member.
type SelectedImageResolver interface {
	// Resolve downloads and normalizes only the given image, without persisting it.
	Resolve(context.Context, string) (domain.WishImportImage, error)
}

type selectedImageClient struct {
	client  selectedImageDownloader
	permits chan struct{}
	timeout time.Duration
}

type selectedImageDownloader interface {
	DownloadSelectedImage(context.Context, string, int64) ([]byte, *url.URL, string, error)
}

func (c *selectedImageClient) Resolve(ctx context.Context, value string) (domain.WishImportImage, error) {
	ctx, cancel := context.WithTimeout(ctx, c.timeout)
	defer cancel()
	select {
	case c.permits <- struct{}{}:
		defer func() { <-c.permits }()
	case <-ctx.Done():
		return domain.WishImportImage{}, ctx.Err()
	}
	content, _, _, err := c.client.DownloadSelectedImage(ctx, value, images.MaximumInputBytes)
	if err == nil {
		var normalized images.Result
		normalized, err = images.Process(ctx, content, 1024, false)
		// The extension's message limit includes the data-URL prefix and base64 expansion.
		if err == nil && len(normalized.Content) <= 2_099_982 {
			return domain.WishImportImage{ContentBase64: base64.StdEncoding.EncodeToString(normalized.Content), ContentType: "image/webp"}, nil
		}
	}
	if ctx.Err() != nil {
		return domain.WishImportImage{}, ctx.Err()
	}
	return domain.WishImportImage{}, domain.Failure(422, domain.WishImageImportUnavailable, "The selected image could not be retrieved.")
}

func (a *API) resolveWishImage(r *request) (response, error) {
	input, err := body[domain.ResolveWishImageRequest](r)
	if err != nil {
		return response{}, err
	}
	if err = a.Service.AuthorizeWishlist(r.Context(), r.Principal.MemberID, r.PathValue("wishlistId"), true); err != nil {
		return response{}, err
	}
	result, err := a.ImageImporter.Resolve(r.Context(), *input.Url)
	return response{Body: result}, err
}
