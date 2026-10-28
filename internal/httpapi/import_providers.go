package httpapi

import (
	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/importer"
)

func newWishImporter(service *application.Service) *importer.Client {
	options := service.Config.Imports()
	client := importer.NewWithPreviewCache(service.Now)
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
		if product {
			provider = importer.NewLocalFirecrawlProduct(client, key, service.Now)
		}
		client.FallbackSource = provider
		if images {
			client.FinalImageSource = provider
		}
	}
	if options.ManagedProvidersEnabled && options.LocalCloudflareRenderEnabled && !service.Config.Production {
		client.RenderedSource = importer.NewLocalCloudflareRenderer(client, options.LocalCloudflareRenderOrigin, options.LocalCloudflareRenderKey)
		client.ImageSource = client.PageSource.(importer.ImageSource)
	}
	return client
}
