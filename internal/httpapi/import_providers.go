package httpapi

import (
	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/importer"
)

func newWishImporter(service *application.Service) *importer.Client {
	options := service.Config.Imports()
	client := importer.NewWithPreviewCache(service.Now)
	if options.ManagedProvidersEnabled && service.Config.Production || options.LocalFirecrawlRollingEnabled && !service.Config.Production {
		client = importer.NewWithAdaptivePreviewCache(service.Now)
	}
	client.Timeout, client.MaximumRedirects, client.MaximumHTMLBytes = options.Timeout, options.MaximumRedirects, options.MaximumHTMLBytes
	firecrawl, product, images, key := false, false, false, ""
	if options.ManagedProvidersEnabled {
		client.PageSource = importer.NewCloudflare(client, options.CloudflareOrigin, options.CloudflareKey)
		firecrawl, product, images, key = options.FirecrawlEnabled, options.FirecrawlProductEnabled, options.FirecrawlImagesEnabled, options.FirecrawlKey
	} else if options.LocalCloudflareEnabled && !service.Config.Production {
		cloudflare := importer.NewLocalCloudflare(client, options.LocalCloudflareKey)
		client.PageSource = cloudflare
		if options.LocalCloudflareImagesEnabled {
			client.ImageSource = cloudflare
		}
		firecrawl, product, images, key = options.LocalFirecrawlEnabled, options.LocalFirecrawlProductEnabled, options.LocalFirecrawlImagesEnabled, options.LocalFirecrawlKey
	}
	if firecrawl {
		provider := importer.NewLocalFirecrawl(client, key, service.Now)
		if (options.LocalFirecrawlAccountImageFallbackEnabled || options.LocalFirecrawlRollingEnabled) && !service.Config.Production {
			provider = importer.NewLocalFirecrawlRolling(client, key, service.Now, product)
		} else if product {
			provider = importer.NewLocalFirecrawlProduct(client, key, service.Now)
		}
		client.FallbackSource = provider
		if options.LocalFirecrawlKeylessEnabled && !service.Config.Production {
			keyless := importer.NewLocalFirecrawlKeylessFirst(client, provider, service.Now)
			if options.LocalFirecrawlAccountImageFallbackEnabled {
				keyless = importer.NewLocalFirecrawlKeylessWithAccountImages(client, provider, service.Now)
			}
			client.FallbackSource = keyless
			if options.LocalFirecrawlKeylessImagesEnabled {
				client.FinalImageSource = keyless
			}
		}
		if images && client.FinalImageSource == nil {
			if options.LocalFirecrawlRollingEnabled && !service.Config.Production {
				client.FinalImageSource = importer.NewLocalSingleImageAttempt(provider)
			} else {
				client.FinalImageSource = provider
			}
		}
	}
	if options.ManagedProvidersEnabled && options.LocalCloudflareRenderEnabled && !service.Config.Production {
		client.RenderedSource = importer.NewLocalCloudflareRenderer(client, options.LocalCloudflareRenderOrigin, options.LocalCloudflareRenderKey)
		client.ImageSource = client.PageSource.(importer.ImageSource)
	}
	return client
}
