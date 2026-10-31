package importer

import (
	"net/url"
	"strings"

	"golang.org/x/net/html"
)

func rdfaTerm(n *html.Node, value, term string) bool {
	for _, word := range strings.Fields(value) {
		if schemaKind(word, term) && word != term {
			return true
		}
		prefix, local, qualified := strings.Cut(word, ":")
		if !qualified && word != term || qualified && local != term {
			continue
		}
		for p := n; p != nil; p = p.Parent {
			if !qualified && hasAttr(p, "vocab") {
				return strings.TrimRight(attr(p, "vocab"), "/") == "https://schema.org" || strings.TrimRight(attr(p, "vocab"), "/") == "http://schema.org"
			}
			if qualified {
				parts := strings.Fields(attr(p, "prefix"))
				for i := 0; i+1 < len(parts); i += 2 {
					if parts[i] == prefix+":" {
						return parts[i+1] == "https://schema.org/" || parts[i+1] == "http://schema.org/"
					}
				}
			}
		}
	}
	return false
}

func markupType(n *html.Node, kind string, rdfa bool) bool {
	if rdfa {
		return rdfaTerm(n, attr(n, "typeof"), kind)
	}
	return schemaType(n, kind)
}

func markupScope(n *html.Node, rdfa bool) *html.Node {
	if !rdfa {
		return nearestScope(n)
	}
	for ; n != nil; n = n.Parent {
		if hasAttr(n, "typeof") {
			return n
		}
	}
	return nil
}

func markupProperty(n *html.Node, key string, rdfa bool) bool {
	if rdfa {
		return rdfaTerm(n, attr(n, "property"), key)
	}
	return token(attr(n, "itemprop"), key)
}

func markupNodes(root, scope *html.Node, rdfa bool) []*html.Node {
	nodes := selectNodes(scope, func(n *html.Node) bool { return n != scope && markupScope(n.Parent, rdfa) == scope })
	if rdfa {
		return nodes
	}
	// itemref explicitly links detached properties to this item. Duplicate IDs
	// and properties already owned by another item cannot establish ownership.
	refs := strings.Fields(attr(scope, "itemref"))
	if len(refs) > 32 {
		return nodes
	}
	seen := map[*html.Node]bool{}
	for _, n := range nodes {
		seen[n] = true
	}
	for _, id := range refs {
		matches := selectNodes(root, func(n *html.Node) bool { return attr(n, "id") == id })
		if len(matches) != 1 || markupScope(matches[0].Parent, false) != nil {
			continue
		}
		for _, n := range selectNodes(matches[0], func(n *html.Node) bool { return markupScope(n.Parent, false) == nil }) {
			if !seen[n] {
				seen[n] = true
				nodes = append(nodes, n)
			}
		}
	}
	return nodes
}

func markupObject(root, scope *html.Node, rdfa bool, depth int) map[string]any {
	result := map[string]any{}
	if depth > 16 {
		return result
	}
	for _, kind := range []string{"Product", "ProductGroup", "Offer", "AggregateOffer", "ImageObject", "UnitPriceSpecification", "QuantitativeValue", "SizeSpecification"} {
		if markupType(scope, kind, rdfa) {
			result["@type"] = kind
		}
	}
	nodes := markupNodes(root, scope, rdfa)
	for _, key := range []string{"name", "description", "color", "size", "width", "height", "depth", "value", "unitText", "url", "image", "sku", "price", "priceCurrency", "lowPrice", "highPrice", "offerCount", "contentUrl", "thumbnailUrl", "priceType"} {
		var values []string
		for _, n := range nodes {
			if markupScope(n, rdfa) != scope && markupScope(n, rdfa) != nil || !markupProperty(n, key, rdfa) {
				continue
			}
			value := nodeText(n)
			for _, a := range []string{"content", "resource", "href", "src", "datetime"} {
				if hasAttr(n, a) {
					value = attr(n, a)
					break
				}
			}
			values = append(values, normalizeText(value))
		}
		if len(values) > 0 {
			result[key] = unique(values)
		}
	}
	for _, key := range []string{"offers", "image", "hasVariant", "priceSpecification", "itemOffered", "size", "width", "height", "depth"} {
		var children []any
		for _, n := range nodes {
			if markupScope(n, rdfa) == n && markupProperty(n, key, rdfa) {
				children = append(children, markupObject(root, n, rdfa, depth+1))
				if len(children) > maximumProductCandidates {
					return map[string]any{}
				}
			}
		}
		if len(children) > 0 {
			result[key] = children
		}
	}
	return result
}

func standaloneOfferPrices(root *html.Node, base, canonical *url.URL) []price {
	var offers []map[string]any
	for _, rdfa := range []bool{false, true} {
		for _, n := range selectNodes(root, func(n *html.Node) bool { return markupType(n, "Offer", rdfa) && markupScope(n.Parent, rdfa) == nil }) {
			offer := markupObject(root, n, rdfa, 0)
			// An offer without explicit page identity may describe a recommendation
			// or an unselected condition/variant, even when it is the only offer.
			if productMatch(offer, base, canonical) > 0 {
				offers = append(offers, offer)
			}
			if len(offers) > maximumProductCandidates {
				return []price{{}}
			}
		}
	}
	return offerPrices(offers)
}
