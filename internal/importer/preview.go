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

type previewImageSourceFactory interface {
	newPreviewImageSource() ImageSource
}

const partialPriceRenderBudget = 4 * time.Second
const fallbackReserve = 3 * time.Second
const localCompleteTextImageBudget = 2 * time.Second

// Preview downloads suggestions without persisting any resource or source image.
func (c *Client) Preview(ctx context.Context, value string) (domain.WishImportPreview, error) {
	result := domain.WishImportPreview{Url: &value, Quantity: 1, Warnings: []string{}}
	var observedAt time.Time
	if c.cache != nil && c.cache.adaptive {
		observedAt = c.cache.now()
	}
	budget, cancel := context.WithTimeout(ctx, c.Timeout)
	defer cancel()
	validationErr := c.Validate(budget, value)
	var business *domain.Error
	if errors.As(validationErr, &business) {
		return result, validationErr
	}
	var key previewKey
	if validationErr == nil && c.cache != nil {
		key = cacheKey(value)
		cached, hit, release, err := c.cache.acquire(budget, key)
		if err != nil {
			validationErr = err
		} else if hit {
			cached.Url = &value
			return cached, nil
		} else {
			defer release()
		}
	}
	var metadata Metadata
	var priceFreshUntil time.Time
	priceFromFallback := false
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
	result.Note, result.Color, result.Size = metadata.Note, metadata.Color, metadata.Size
	if result.Price != nil && !observedAt.IsZero() {
		priceFreshUntil = observedAt.Add(maximumPriceFreshness - providerFreshnessSafety)
	}
	needsPrice := func() bool {
		return result.Price == nil && !metadata.PriceAmbiguous && !metadata.UnsupportedCurrency
	}
	original, _ := url.Parse(value)
	fallbackReady := primaryUsed && c.FallbackSource != nil && c.FallbackSource.CanHandle(original)
	renderReady := primaryUsed && c.RenderedSource != nil && c.RenderedSource.CanHandle(original)
	finalSource := c.FinalImageSource
	if factory, ok := finalSource.(previewImageSourceFactory); ok {
		finalSource = factory.newPreviewImageSource()
	}
	tried := make(map[string]bool)
	finalTried := make(map[string]bool)
	imageContext := budget
	imageCancel := func() {}
	if fallbackReady || renderReady {
		// Reserve time for the final provider when a primary image host stalls.
		imageBudget := time.Second
		if c.cache != nil && c.cache.adaptive && result.Name != nil && !needsPrice() && (metadata.Image != nil || len(metadata.Images) > 0) {
			// When only the image is missing, allow the free image path more time
			// before spending a browser session or another provider request.
			imageBudget = localCompleteTextImageBudget
			if deadline, ok := budget.Deadline(); fallbackReady && ok {
				imageBudget = min(imageBudget, max(0, time.Until(deadline)-fallbackReserve))
			}
		}
		imageContext, imageCancel = context.WithTimeout(budget, imageBudget)
	}
	result.Image = normalizePreviewImage(imageContext, metadata.ImageData)
	if result.Image == nil {
		result.Image = c.previewImage(imageContext, metadata, tried, nil, finalSource)
	}
	imageCancel()
	if renderReady && budget.Err() == nil && (result.Name == nil || needsPrice() || result.Image == nil) {
		renderContext := budget
		renderCancel := func() {}
		// Leave the fallback enough time even when the primary document has no
		// usable fields. A stalled browser otherwise consumes the whole budget.
		if deadline, ok := budget.Deadline(); fallbackReady && ok {
			duration := time.Until(deadline) - fallbackReserve
			if result.Name != nil && result.Image != nil && needsPrice() {
				duration = min(duration, partialPriceRenderBudget)
			}
			if duration < 0 {
				duration = 0
			}
			renderContext, renderCancel = context.WithTimeout(budget, duration)
		}
		rendered, err := c.RenderedSource.Get(renderContext, original)
		renderCancel()
		if err == nil && compatibleProductIdentity(metadata, rendered) {
			mergePreviewDetails(&result, &metadata, rendered)
			pageUnavailable = false
			if metadata.productIdentity == "" {
				metadata.productIdentity = rendered.productIdentity
			}
			if result.Name == nil {
				result.Name = rendered.Name
			}
			if needsPrice() {
				result.Price = rendered.Price
				if result.Price != nil && !observedAt.IsZero() {
					priceFreshUntil = observedAt.Add(maximumPriceFreshness - providerFreshnessSafety)
				}
			}
			if result.Price == nil {
				metadata.PriceAmbiguous = metadata.PriceAmbiguous || rendered.PriceAmbiguous
			}
			metadata.UnsupportedCurrency = metadata.UnsupportedCurrency || rendered.UnsupportedCurrency
			if result.Image == nil {
				result.Image = normalizePreviewImage(budget, rendered.ImageData)
				if result.Image == nil {
					imageContext := budget
					imageCancel := func() {}
					if deadline, ok := budget.Deadline(); fallbackReady && ok {
						imageContext, imageCancel = context.WithDeadline(budget, deadline.Add(-fallbackReserve))
					}
					result.Image = c.previewImage(imageContext, rendered, tried, nil, finalSource)
					imageCancel()
				}
			}
			// Later byte recovery must use the document that supplied the product.
			metadata.Name, metadata.Price = result.Name, result.Price
			if rendered.Image != nil || len(rendered.Images) != 0 {
				metadata.Image, metadata.Images = rendered.Image, rendered.Images
			}
		}
	}
	if fallbackReady && result.Image == nil && result.Name != nil && !needsPrice() {
		// Known product text needs image bytes, not another rendered document.
		// Spend the remaining short budget on the missing resource first.
		imageContext := budget
		imageCancel := func() {}
		if c.cache != nil && c.cache.adaptive {
			// The final image source can stall too. Keep time for a different
			// image discovered by the page fallback in this local experiment.
			if deadline, ok := budget.Deadline(); ok {
				imageContext, imageCancel = context.WithDeadline(budget, deadline.Add(-fallbackReserve))
			}
		} else if finalSource == nil {
			// Do not let a previously interrupted image consume the page fallback budget.
			imageContext, imageCancel = context.WithTimeout(budget, 2*time.Second)
		}
		result.Image = c.previewImage(imageContext, metadata, tried, finalTried, finalSource)
		imageCancel()
	}
	if fallbackReady && budget.Err() == nil && (result.Name == nil || needsPrice() || result.Image == nil) {
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
		if err == nil && compatibleProductIdentity(metadata, alternative) {
			mergePreviewDetails(&result, &metadata, alternative)
			finalMetadata = alternative
			if len(finalMetadata.Images) == 0 && finalMetadata.Image == nil {
				finalMetadata.Images, finalMetadata.Image = metadata.Images, metadata.Image
			}
			pageUnavailable = false
			if result.Name == nil {
				result.Name = alternative.Name
			}
			if needsPrice() {
				result.Price = alternative.Price
				if result.Price != nil {
					priceFreshUntil = alternative.priceFreshUntil
					priceFromFallback = true
				}
			}
			if result.Price == nil {
				metadata.PriceAmbiguous = metadata.PriceAmbiguous || alternative.PriceAmbiguous
			}
			metadata.UnsupportedCurrency = metadata.UnsupportedCurrency || alternative.UnsupportedCurrency
		}
		if result.Image == nil && budget.Err() == nil {
			result.Image = normalizePreviewImage(budget, finalMetadata.ImageData)
			if result.Image == nil {
				result.Image = c.previewImage(budget, finalMetadata, tried, finalTried, finalSource)
			}
		}
	}
	if ctx.Err() != nil {
		return result, ctx.Err()
	}
	// A successful HTTP response can still be an empty or blocked document.
	// Treat it as unavailable when no suggestion survived the whole pipeline.
	if result.Name == nil && result.Price == nil && result.Image == nil {
		pageUnavailable = true
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
	if priceFromFallback && c.cache != nil && c.cache.adaptive {
		result.Warnings = append(result.Warnings, domain.WishImportPriceMayBeStale)
	}
	if c.cache != nil && validationErr == nil && budget.Err() == nil {
		c.cache.put(key, result, metadata.PriceAmbiguous, priceFreshUntil)
	}
	return result, nil
}

// Missing optional details do not cause additional provider requests or extend
// the preview deadline. Compatible fallbacks can fill them during existing work.
func mergePreviewDetails(result *domain.WishImportPreview, primary *Metadata, metadata Metadata) {
	primary.colorAmbiguous = primary.colorAmbiguous || metadata.colorAmbiguous
	primary.sizeAmbiguous = primary.sizeAmbiguous || metadata.sizeAmbiguous
	if result.Note == nil {
		result.Note = metadata.Note
	}
	if result.Color == nil && !primary.colorAmbiguous {
		result.Color = metadata.Color
	}
	if result.Size == nil && !primary.sizeAmbiguous {
		result.Size = metadata.Size
	}
}

// A shared URL alone does not prove that two providers returned the same item.
// Reject a mixture when both documents identify different global trade items;
// missing identifiers remain inconclusive and do not prevent recovery.
func compatibleProductIdentity(left, right Metadata) bool {
	return left.productIdentity == "" || right.productIdentity == "" || left.productIdentity == right.productIdentity
}

func (c *Client) previewImage(ctx context.Context, metadata Metadata, tried, finalTried map[string]bool, finalSource ImageSource) *domain.WishImportImage {
	candidates := metadata.Images
	if len(candidates) == 0 && metadata.Image != nil {
		candidates = []string{*metadata.Image}
	}
	attempts := 0
	for _, candidate := range candidates {
		if attempts == 3 || ctx.Err() != nil {
			break
		}
		useFinal := finalTried != nil && finalSource != nil && !finalTried[candidate]
		if tried[candidate] && !useFinal {
			continue
		}
		attempts++
		if !tried[candidate] {
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
			// A short preliminary budget may expire before image sources run.
			// Leave that candidate eligible when the remaining budget is available.
			if ctx.Err() == nil {
				tried[candidate] = true
			}
		}
		if useFinal && ctx.Err() == nil {
			finalTried[candidate] = true
			target, err := url.Parse(candidate)
			if err == nil && c.Validate(ctx, candidate) == nil {
				content, err := finalSource.GetImage(ctx, target)
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
	if len(content) == 0 {
		return nil
	}
	normalized, err := images.ProcessImported(ctx, content, 1600)
	if err != nil {
		return nil
	}
	return &domain.WishImportImage{ContentBase64: base64.StdEncoding.EncodeToString(normalized.Content), ContentType: "image/webp"}
}
