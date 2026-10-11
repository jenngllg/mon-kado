package importer

import (
	"context"
	"encoding/base64"
	"errors"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/images"
)

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
	if validationErr == nil {
		maximum := c.MaximumHTMLBytes
		if maximum == 0 {
			maximum = 2 * 1024 * 1024
		}
		content, base, media, err := c.Download(budget, value, int64(maximum))
		if err == nil && (media == "text/html" || media == "application/xhtml+xml") {
			metadata = Extract(content, base)
		} else {
			result.Warnings = append(result.Warnings, "WISH_IMPORT_PAGE_UNAVAILABLE")
		}
	} else {
		result.Warnings = append(result.Warnings, "WISH_IMPORT_PAGE_UNAVAILABLE")
	}
	result.Name, result.Price = metadata.Name, metadata.Price
	if metadata.Image != nil {
		content, _, _, err := c.Download(budget, *metadata.Image, images.MaximumInputBytes)
		if err == nil {
			normalized, err := images.Process(budget, content, 1600, false)
			if err == nil {
				result.Image = &domain.WishImportImage{ContentBase64: base64.StdEncoding.EncodeToString(normalized.Content), ContentType: "image/webp"}
			}
		}
	}
	if ctx.Err() != nil {
		return result, ctx.Err()
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
