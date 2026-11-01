package importer

import (
	"net/url"
	"strings"

	"golang.org/x/net/html"
)

// partialVariantGroup recovers identity only when an otherwise ambiguous
// document explicitly describes the same product and its variant group. It
// does not choose a variant or price for a URL without a variant selection.
func partialVariantGroup(root *html.Node, products []map[string]any, base, canonical *url.URL) map[string]any {
	base, ok := unselectedVariantPage(base)
	if !ok {
		return nil
	}
	best := 0
	var selected []map[string]any
	for _, product := range products {
		score := productMatch(product, base, canonical)
		if score > best {
			best, selected = score, nil
		}
		if score == best && score > 0 {
			selected = append(selected, product)
		}
	}
	if len(selected) < 2 {
		return nil
	}
	var group map[string]any
	for _, product := range selected {
		if schemaKind(product["@type"], "ProductGroup") {
			if group != nil {
				return nil
			}
			group = product
		}
	}
	if group == nil {
		return nil
	}
	name := selectionText(scalar(group, "name"))
	headings := selectNodes(root, func(n *html.Node) bool { return n.Data == "h1" })
	if name == "" || len(headings) != 1 || selectionText(nodeText(headings[0])) != name {
		return nil
	}
	variants := objectList(group["hasVariant"])
	if len(variants) < 2 || len(variants) > maximumProductCandidates {
		return nil
	}
	for _, product := range selected {
		if schemaKind(product["@type"], "ProductGroup") {
			continue
		}
		if !productType(product["@type"]) || selectionText(scalar(product, "name")) != name {
			return nil
		}
		sku := scalar(product, "sku")
		if sku == "" {
			return nil
		}
		productGTIN, conflict := declaredGTIN(product)
		if conflict {
			return nil
		}
		matches := 0
		for _, variant := range variants {
			if scalar(variant, "sku") != sku {
				continue
			}
			variantGTIN, conflict := declaredGTIN(variant)
			if conflict || productGTIN != "" && variantGTIN != "" && productGTIN != variantGTIN {
				return nil
			}
			matches++
		}
		if matches != 1 {
			return nil
		}
	}
	return group
}

// Different explicit euro offers on an unselected page are a completed
// ambiguity decision, not a missing value that another provider may guess.
func hasDifferentUnselectedOfferPrices(product map[string]any, base, canonical *url.URL) bool {
	base, ok := unselectedVariantPage(base)
	if !ok || product == nil {
		return false
	}
	values := map[string]bool{}
	for _, candidate := range offerPrices(productOffers(product, base, canonical)) {
		amount := parseAmount(candidate.Amount)
		if amount == nil || !strings.EqualFold(candidate.Currency, "EUR") {
			return false
		}
		values[amount.String()] = true
	}
	return len(values) > 1
}

// An incomplete variant price set cannot justify the page's default choice.
// Unlike a proven conflict, it remains eligible for recovery by another source.
func hasIncompleteUnselectedVariantPrices(product map[string]any, base, canonical *url.URL) (bool, bool) {
	base, ok := unselectedVariantPage(base)
	if !ok || !schemaKind(product["@type"], "ProductGroup") {
		return false, false
	}
	variants := groupVariants(product, base, canonical)
	if len(variants) < 2 {
		return false, false
	}
	incomplete, unsupportedCurrency := false, false
	for _, variant := range variants {
		prices := offerPrices(offerNodes(variant))
		amount, unsupported := choosePrice(prices)
		if len(prices) == 0 || amount == nil || unsupported {
			incomplete = true
		}
		unsupportedCurrency = unsupportedCurrency || unsupported
	}
	return incomplete, unsupportedCurrency
}

// Only the same well-known tracking parameters accepted by the Firecrawl
// product identity check are ignored here. Unknown parameters may select an
// offer, so they cannot be treated as an unselected variant group.
func unselectedVariantPage(base *url.URL) (*url.URL, bool) {
	values, err := url.ParseQuery(base.RawQuery)
	if err != nil {
		return base, false
	}
	for key := range values {
		if !strings.HasPrefix(strings.ToLower(key), "utm_") && key != "gclid" && key != "fbclid" {
			return base, false
		}
	}
	clean := *base
	clean.RawQuery = ""
	clean.ForceQuery = false
	return &clean, true
}
