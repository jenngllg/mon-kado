package importer

import (
	"encoding/json"
	"golang.org/x/net/html"
	"net/url"
	"reflect"
	"strings"
)

func schemaKind(value any, kind string) bool {
	switch v := value.(type) {
	case string:
		return v == kind || v == "https://schema.org/"+kind || v == "http://schema.org/"+kind
	case []any:
		for _, item := range v {
			if schemaKind(item, kind) {
				return true
			}
		}
	}
	return false
}

func objectList(value any) []map[string]any {
	switch v := value.(type) {
	case map[string]any:
		return []map[string]any{v}
	case []any:
		var result []map[string]any
		for _, item := range v {
			if obj, ok := item.(map[string]any); ok {
				result = append(result, obj)
			}
		}
		return result
	}
	return nil
}

func documentReference(root *html.Node, base *url.URL) *url.URL {
	for _, n := range selectNodes(root, func(n *html.Node) bool {
		return n.Data == "link" && token(strings.ToLower(attr(n, "rel")), "canonical")
	}) {
		u, err := url.Parse(attr(n, "href"))
		if err == nil && attr(n, "href") != "" {
			u = base.ResolveReference(u)
			if u.User == nil && (u.Scheme == "https" || u.Scheme == "http") {
				return u
			}
		}
	}
	return base
}

func productMatch(p map[string]any, base, canonical *url.URL) int {
	score := 0
	for _, candidate := range append([]map[string]any{p}, offerNodes(p)...) {
		v := scalar(candidate, "url")
		if v == "" {
			v = scalar(candidate, "@id")
		}
		if matchesDocument(v, canonical) {
			score = max(score, 1)
		}
		if matchesDocument(v, base) {
			score = max(score, 2)
			u, e := url.Parse(v)
			if e == nil && u.RawQuery != "" {
				score = 3
			}
		}
	}
	return score
}

func sameProduct(a, b map[string]any, base *url.URL) bool {
	leftGTIN, leftConflict := productGTIN(a)
	rightGTIN, rightConflict := productGTIN(b)
	if leftConflict || rightConflict {
		return false
	}
	if leftGTIN != "" && rightGTIN != "" {
		if leftISBN, rightISBN := scalar(a, "isbn"), scalar(b, "isbn"); leftISBN != "" && rightISBN != "" && leftISBN != rightISBN {
			return false
		}
		// Independent feeds can assign different local SKUs to one trade item.
		// A validated global identity is stronger; conflicting prices still survive
		// deduplication and are rejected by the existing offer selection.
		return leftGTIN == rightGTIN
	}
	for _, key := range []string{"sku", "gtin", "gtin13", "productID", "isbn"} {
		if x, y := scalar(a, key), scalar(b, key); x != "" && y != "" {
			return x == y
		}
	}
	if x, y := scalar(a, "@id"), scalar(b, "@id"); x != "" && y != "" {
		left, right := resolveReference(base, x), resolveReference(base, y)
		if left != nil && right != nil && left.String() == right.String() {
			return true
		}
	}
	if strings.EqualFold(normalizeText(scalar(a, "name")), normalizeText(scalar(b, "name"))) && scalar(a, "name") != "" {
		x, y := scalar(a, "url"), scalar(b, "url")
		if x != "" && y != "" {
			target := resolveReference(base, y)
			if target == nil || !matchesDocument(x, target) {
				return false
			}
		}
		return true
	}
	return reflect.DeepEqual(a, b)
}

func resolveReference(base *url.URL, value string) *url.URL {
	u, err := url.Parse(value)
	if err != nil {
		return nil
	}
	return base.ResolveReference(u)
}

func deduplicateProducts(products []map[string]any, base *url.URL) []map[string]any {
	var result []map[string]any
	for _, p := range products {
		found := false
		for _, existing := range result {
			if !sameProduct(existing, p, base) {
				continue
			}
			found = true
			// Equivalent Product and ProductGroup declarations may split their fields.
			// Retain group semantics so merged variants still determine price ambiguity.
			if schemaKind(p["@type"], "ProductGroup") {
				existing["@type"] = "ProductGroup"
			}
			for key, value := range p {
				if key == "offers" {
					var offers []any
					seen := map[string]bool{}
					for _, o := range append(offerNodes(existing), offerNodes(p)...) {
						encoded, _ := json.Marshal(o)
						if !seen[string(encoded)] {
							seen[string(encoded)] = true
							offers = append(offers, o)
						}
					}
					existing[key] = offers
				} else if existing[key] == nil || existing[key] == "" {
					existing[key] = value
				}
			}
			break
		}
		if !found {
			copy := map[string]any{}
			for k, v := range p {
				copy[k] = v
			}
			result = append(result, copy)
		}
	}
	return result
}

func selectProduct(products []map[string]any, base, canonical *url.URL) (map[string]any, bool) {
	products = deduplicateProducts(products, base)
	if len(products) == 0 {
		return nil, false
	}
	if len(products) == 1 {
		return products[0], false
	}
	var selected []map[string]any
	best := 0
	for _, p := range products {
		score := productMatch(p, base, canonical)
		if score > best {
			selected = nil
			best = score
		}
		if score == best && score > 0 {
			selected = append(selected, p)
		}
	}
	if len(selected) == 1 {
		return selected[0], false
	}
	return nil, true
}

func groupVariants(product map[string]any, base, canonical *url.URL) []map[string]any {
	variants := objectList(product["hasVariant"])
	var selected []map[string]any
	best := 0
	for _, p := range variants {
		score := productMatch(p, base, canonical)
		if score > best {
			selected = nil
			best = score
		}
		if score == best && score > 0 {
			selected = append(selected, p)
		}
	}
	if len(selected) > 0 {
		return selected
	}
	return variants
}

func productOffers(product map[string]any, base, canonical *url.URL) []map[string]any {
	offers := offerNodes(product)
	if schemaKind(product["@type"], "ProductGroup") {
		variants := groupVariants(product, base, canonical)
		// Some groups describe the selected colour with an unpriced stub, then
		// list size offers at the same URL with different query parameters. Use
		// those only when no query selects a size, retaining every price conflict.
		if base.RawQuery == "" && len(variants) == 1 && len(offerNodes(variants[0])) == 0 && matchesDocument(scalar(variants[0], "url"), base) {
			for _, variant := range objectList(product["hasVariant"]) {
				u := resolveReference(base, scalar(variant, "url"))
				if scalar(variant, "url") != "" && u != nil && len(offerNodes(variant)) > 0 {
					u.RawQuery, u.Fragment = "", ""
					if matchesDocument(u.String(), base) {
						variants = append(variants, variant)
					}
				}
			}
		}
		for _, p := range variants {
			offers = append(offers, offerNodes(p)...)
		}
	}
	if exact := exactNestedOffers(offers, base); len(exact) > 0 {
		return exact
	}
	var selected []map[string]any
	best := 0
	for _, o := range offers {
		score := productMatch(o, base, canonical)
		if score > best {
			selected = nil
			best = score
		}
		if score == best && score > 0 {
			selected = append(selected, o)
		}
	}
	if len(selected) > 0 {
		return selected
	}
	if sku := scalar(product, "sku"); sku != "" {
		for _, o := range offers {
			if scalar(o, "sku") == sku {
				selected = append(selected, o)
			}
		}
		if len(selected) > 0 {
			return selected
		}
	}
	return offers
}

func offerPrices(offers []map[string]any) []price {
	return collectOfferPrices(offers, 0)
}

func collectOfferPrices(offers []map[string]any, depth int) []price {
	if depth > 64 || len(offers) > maximumProductCandidates {
		return []price{{}}
	}
	var concrete, aggregate []price
	for _, o := range offers {
		currency := scalar(o, "priceCurrency")
		amount := scalar(o, "price")
		if schemaKind(o["@type"], "AggregateOffer") {
			children := collectOfferPrices(objectList(o["offers"]), depth+1)
			if len(children) > 0 {
				concrete = append(concrete, children...)
				continue
			}
			low, high := scalar(o, "lowPrice"), scalar(o, "highPrice")
			if amount == "" && low != "" {
				a, b := parseAmount(low), parseAmount(high)
				if a != nil && b != nil && a.String() == b.String() {
					amount = a.String()
				} else if scalar(o, "offerCount") == "1" {
					amount = low
				}
			}
			aggregate = append(aggregate, price{amount, currency})
			continue
		}
		if amount == "" {
			before := len(concrete)
			for _, spec := range objectList(o["priceSpecification"]) {
				if strings.Contains(scalar(spec, "priceType"), "StrikethroughPrice") {
					continue
				}
				cur := scalar(spec, "priceCurrency")
				if cur == "" {
					cur = currency
				}
				if v := scalar(spec, "price"); v != "" {
					concrete = append(concrete, price{v, cur})
				}
			}
			// An unpriced selected offer must not borrow a different offer's amount
			// or silently fall through to page-level price metadata.
			if len(concrete) == before {
				concrete = append(concrete, price{"", currency})
			}
		} else {
			concrete = append(concrete, price{amount, currency})
		}
	}
	if len(concrete) > 0 {
		return concrete
	}
	return aggregate
}
