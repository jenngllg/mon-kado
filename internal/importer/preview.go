package importer

import (
	"context"
	"encoding/base64"
	"errors"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/images"
	"net/url"
	"time"
)

// PageSource supplies passive product metadata within the caller's import budget.
type PageSource interface {
	CanHandle(*url.URL) bool
	Get(context.Context, *url.URL) (Metadata, error)
}

type htmlPageSource interface {
	GetHTML(context.Context, *url.URL) (Metadata, error)
}

// ImageSource supplies bounded merchant bytes when pinned direct image import fails.
type ImageSource interface {
	GetImage(context.Context, *url.URL) ([]byte, error)
}

// Preview downloads suggestions without persisting any resource or source image.
func (c *Client) Preview(ctx context.Context, value string) (domain.WishImportPreview, error) {
	result := domain.WishImportPreview{Url: &value, Quantity: 1, Warnings: []string{}}
	budget, cancel := context.WithTimeout(ctx, c.Timeout)
	defer cancel()
	validationErr := c.Validate(budget, value)
	var business *domain.Error
	if errors.As(validationErr, &business) {
		return result, validationErr
	}
	var metadata Metadata
	pageUnavailable, primaryUsed := false, false
	if validationErr == nil {
		original, _ := url.Parse(value)
		if c.PageSource != nil {
			primaryUsed = true
			if c.PageSource.CanHandle(original) {
				var err error
				metadata, err = c.PageSource.Get(budget, original)
				pageUnavailable = err != nil
			} else {
				// A configured relay owns page access; unsupported inputs must not bypass it.
				pageUnavailable = true
			}
		} else {
			maximum := c.MaximumHTMLBytes
			if maximum == 0 {
				maximum = config.MaximumImportHTMLBytes
			}
			content, base, media, err := c.Download(budget, value, int64(maximum))
			if err == nil && (media == "text/html" || media == "application/xhtml+xml") {
				metadata = Extract(content, base)
			} else {
				pageUnavailable = true
			}
		}
	} else {
		pageUnavailable = true
	}
	result.Name, result.Price = metadata.Name, metadata.Price
	original, _ := url.Parse(value)
	fallbackReady := primaryUsed && c.FallbackSource != nil && c.FallbackSource.CanHandle(original)
	tried := make(map[string]bool)
	finalTried := make(map[string]bool)
	imageContext := budget
	imageCancel := func() {}
	if fallbackReady {
		// Reserve time for the final provider when a primary image host stalls.
		imageContext, imageCancel = context.WithTimeout(budget, time.Second)
	}
	result.Image = c.previewImage(imageContext, metadata, tried, nil)
	imageCancel()
	if fallbackReady && result.Image == nil && result.Name != nil && result.Price != nil && c.FinalImageSource != nil {
		// Known product text needs image bytes, not another rendered document.
		// Spend the remaining short budget on the missing resource first.
		result.Image = c.previewImage(budget, metadata, tried, finalTried)
	}
	if fallbackReady && budget.Err() == nil && (result.Name == nil || result.Price == nil || result.Image == nil) {
		var alternative Metadata
		var err error
		if html, ok := c.FallbackSource.(htmlPageSource); ok && metadata.Name == nil && metadata.Price == nil && metadata.Image == nil && len(metadata.Images) == 0 {
			// A rendered document already carries the generic schemas we need.
			// Avoid blocking an otherwise empty import on optional enrichment.
			alternative, err = html.GetHTML(budget, original)
		} else {
			alternative, err = c.FallbackSource.Get(budget, original)
		}
		finalMetadata := metadata
		if err == nil {
			finalMetadata = alternative
			if len(finalMetadata.Images) == 0 && finalMetadata.Image == nil {
				finalMetadata.Images, finalMetadata.Image = metadata.Images, metadata.Image
			}
			pageUnavailable = false
			if result.Name == nil {
				result.Name = alternative.Name
			}
			if result.Price == nil {
				result.Price = alternative.Price
				metadata.UnsupportedCurrency = metadata.UnsupportedCurrency || alternative.UnsupportedCurrency
			}
		}
		if result.Image == nil && budget.Err() == nil {
			result.Image = c.previewImage(budget, finalMetadata, tried, finalTried)
		}
	}
	if ctx.Err() != nil {
		return result, ctx.Err()
	}
	if pageUnavailable {
		result.Warnings = append(result.Warnings, "WISH_IMPORT_PAGE_UNAVAILABLE")
	}
	if result.Image == nil {
		result.Warnings = append(result.Warnings, "WISH_IMPORT_IMAGE_UNAVAILABLE")
	}
	if result.Name == nil {
		result.Warnings = append(result.Warnings, "WISH_IMPORT_NAME_UNAVAILABLE")
	}
	if result.Price == nil {
		warning := "WISH_IMPORT_PRICE_UNAVAILABLE"
		if metadata.UnsupportedCurrency {
			warning = "WISH_IMPORT_CURRENCY_UNSUPPORTED"
		}
		result.Warnings = append(result.Warnings, warning)
	}
	return result, nil
}

func (c *Client) previewImage(ctx context.Context, metadata Metadata, tried, finalTried map[string]bool) *domain.WishImportImage {
	candidates := metadata.Images
	if len(candidates) == 0 && metadata.Image != nil {
		candidates = []string{*metadata.Image}
	}
	attempts := 0
	for _, candidate := range candidates {
		if attempts == 3 || ctx.Err() != nil {
			break
		}
		useFinal := finalTried != nil && c.FinalImageSource != nil && !finalTried[candidate]
		if tried[candidate] && !useFinal {
			continue
		}
		attempts++
		if !tried[candidate] {
			tried[candidate] = true
			content, _, _, err := c.download(ctx, candidate, images.MaximumInputBytes, "image/jpeg,image/png,image/webp,image/avif")
			if err == nil {
				if picture := normalizePreviewImage(ctx, content); picture != nil {
					return picture
				}
			}
			if c.ImageSource != nil && ctx.Err() == nil {
				target, err := url.Parse(candidate)
				if err == nil && c.Validate(ctx, candidate) == nil {
					content, err = c.ImageSource.GetImage(ctx, target)
					if err == nil {
						if picture := normalizePreviewImage(ctx, content); picture != nil {
							return picture
						}
					}
				}
			}
		}
		if useFinal && ctx.Err() == nil {
			finalTried[candidate] = true
			target, err := url.Parse(candidate)
			if err == nil && c.Validate(ctx, candidate) == nil {
				content, err := c.FinalImageSource.GetImage(ctx, target)
				if err == nil {
					if picture := normalizePreviewImage(ctx, content); picture != nil {
						return picture
					}
				}
			}
		}
	}
	return nil
}

func normalizePreviewImage(ctx context.Context, content []byte) *domain.WishImportImage {
	normalized, err := images.ProcessImported(ctx, content, 1600)
	if err != nil {
		return nil
	}
	return &domain.WishImportImage{ContentBase64: base64.StdEncoding.EncodeToString(normalized.Content), ContentType: "image/webp"}
}
