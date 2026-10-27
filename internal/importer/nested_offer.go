package importer

import "net/url"

// Aggregate containers can inherit a match from their children. Resolve an exact
// query-bearing child reference first, rather than pricing every sibling offer.
// Unknown parameters never select a child; original ambiguity remains intact.
func exactNestedOffers(offers []map[string]any, base *url.URL) []map[string]any {
	if base.RawQuery == "" {
		return nil
	}
	var selected []map[string]any
	remaining := maximumProductCandidates
	var visit func([]map[string]any, int) bool
	visit = func(nodes []map[string]any, depth int) bool {
		if depth > 64 {
			return false
		}
		for _, offer := range nodes {
			remaining--
			if remaining < 0 {
				return false
			}
			if children := offerNodes(offer); schemaKind(offer["@type"], "AggregateOffer") && len(children) > 0 {
				if !visit(children, depth+1) {
					return false
				}
				continue
			}
			value := scalar(offer, "url")
			if value == "" {
				value = scalar(offer, "@id")
			}
			if value == "" {
				continue
			}
			if target := resolveReference(base, value); target != nil && target.RawQuery == base.RawQuery && matchesDocument(value, base) {
				selected = append(selected, offer)
			}
		}
		return true
	}
	if !visit(offers, 0) {
		return nil
	}
	return selected
}
