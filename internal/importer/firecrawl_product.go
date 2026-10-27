package importer

import (
	"bytes"
	"encoding/json"
	"net/url"
	"strings"

	"golang.org/x/net/html"
)

type firecrawlProduct struct {
	Title    string `json:"title"`
	URL      string `json:"url"`
	Variants []struct {
		Price *struct {
			Amount   json.Number `json:"amount"`
			Currency string      `json:"currency"`
		} `json:"price"`
		Images []struct {
			URL string `json:"url"`
		} `json:"images"`
	} `json:"variants"`
}

// ExtractFirecrawlProduct completes passive HTML suggestions with a bounded,
// identity-checked product representation. It never fetches entity or image URLs.
func ExtractFirecrawlProduct(content []byte, product json.RawMessage, base, requested *url.URL) Metadata {
	if base == nil || requested == nil || len(content) > 4*1024*1024 {
		return Metadata{}
	}
	return supplementFirecrawlProduct(Extract(content, base), product, content, base, requested)
}

// Product is an additional untrusted representation of the same page, never a
// cross-product catalogue lookup. Keep original fields and all price ambiguity.
func supplementFirecrawlProduct(current Metadata, raw json.RawMessage, content []byte, base, requested *url.URL) Metadata {
	if len(raw) == 0 || len(raw) > 1024*1024 {
		return current
	}
	var product firecrawlProduct
	if json.Unmarshal(raw, &product) != nil || len(product.Variants) == 0 || len(product.Variants) > maximumProductCandidates || cleanName(product.Title) == nil {
		return current
	}
	root, err := html.Parse(bytes.NewReader(content))
	if err != nil || browserChallenge(root) {
		return current
	}
	canonical := documentReference(root, base)
	if !matchesDocument(product.URL, base) && !(canonical.Hostname() == base.Hostname() && matchesDocument(product.URL, canonical)) {
		return current
	}
	// A canonical page may describe an unselected variant. Tracking parameters
	// do not select merchandise, but unknown query parameters cannot be guessed.
	productURL := resolveReference(base, product.URL)
	if productURL == nil {
		return current
	}
	if requested.RawQuery != productURL.RawQuery {
		for key := range requested.Query() {
			if !strings.HasPrefix(strings.ToLower(key), "utm_") && key != "gclid" && key != "fbclid" {
				return current
			}
		}
	}
	var prices []price
	var photos []string
	seen := map[string]bool{}
	for _, variant := range product.Variants {
		if variant.Price == nil {
			prices = append(prices, price{})
		} else {
			prices = append(prices, price{variant.Price.Amount.String(), variant.Price.Currency})
		}
		if len(product.Variants) != 1 {
			continue
		}
		for _, image := range variant.Images {
			if len(photos) == 5 {
				break
			}
			u := resolveReference(base, html.UnescapeString(image.URL))
			if strings.TrimSpace(image.URL) == "" || len(image.URL) > 8192 || u == nil || u.User != nil || u.Hostname() == "" || u.Scheme != "https" && u.Scheme != "http" || brandingImage(u.String()) {
				continue
			}
			u.Fragment, u.RawFragment = "", ""
			if !seen[u.String()] {
				seen[u.String()] = true
				photos = append(photos, u.String())
			}
		}
	}
	// Existing structured offers may already have rejected a genuine price
	// conflict. A product response must not override that decision with one value.
	var documents []any
	for _, script := range selectNodes(root, func(n *html.Node) bool {
		return n.Data == "script" && strings.EqualFold(attr(n, "type"), "application/ld+json")
	}) {
		var value any
		decoder := json.NewDecoder(strings.NewReader(escapeControls(nodeText(script))))
		decoder.UseNumber()
		if decoder.Decode(&value) == nil {
			documents = append(documents, value)
		}
	}
	products, valid := linkedProducts(documents, base)
	if len(products) == 0 {
		products = microdata(root, base)
	}
	selected, ambiguous := selectProduct(products, base, canonical)
	if !valid || ambiguous {
		return current
	}
	if current.Name == nil {
		current.Name = cleanName(product.Title)
	}
	hasOfferEvidence := len(offerPrices(productOffers(selected, base, canonical))) > 0 || len(standaloneOfferPrices(root, base, canonical)) > 0
	for _, prefix := range []string{"product:price:", "og:price:"} {
		if readMeta(root, prefix+"amount") != "" || readMeta(root, prefix+"currency") != "" {
			hasOfferEvidence = true
		}
	}
	if current.Price == nil && valid && !ambiguous && !hasOfferEvidence && !current.UnsupportedCurrency {
		current.Price, current.UnsupportedCurrency = choosePrice(prices)
	}
	for _, photo := range photos {
		if len(current.Images) == 5 {
			break
		}
		found := false
		for _, old := range current.Images {
			if old == photo {
				found = true
				break
			}
		}
		if !found {
			current.Images = append(current.Images, photo)
		}
	}
	if current.Image == nil && len(current.Images) > 0 {
		current.Image = &current.Images[0]
	}
	return current
}
