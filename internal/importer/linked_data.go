package importer

import (
	"net/url"
	"reflect"
)

// Resolve only in-document schema relationships. Remote contexts and @ids are
// identifiers, never fetch targets. A shared budget bounds cycles and fan-out.
func linkedProducts(documents []any, base *url.URL) ([]map[string]any, bool) {
	index := map[string]map[string]any{}
	remaining := 32768
	key := func(id string) string {
		if id == "" {
			return ""
		}
		if u := resolveReference(base, id); u != nil {
			return u.String()
		}
		return ""
	}
	var visit func(any, int) bool
	visit = func(value any, depth int) bool {
		remaining--
		if remaining < 0 || depth > 64 {
			return false
		}
		switch v := value.(type) {
		case []any:
			for _, child := range v {
				if !visit(child, depth+1) {
					return false
				}
			}
		case map[string]any:
			if id := key(scalar(v, "@id")); id != "" && len(v) > 1 {
				if existing, exists := index[id]; !exists {
					index[id] = v
				} else if existing != nil {
					merged := map[string]any{}
					for k, val := range existing {
						merged[k] = val
					}
					for k, val := range v {
						if old, present := merged[k]; present && !reflect.DeepEqual(old, val) {
							// Conflicting definitions must not depend on document order.
							merged = nil
							break
						}
						merged[k] = val
					}
					index[id] = merged
				}
			}
			for _, child := range v {
				if !visit(child, depth+1) {
					return false
				}
			}
		}
		return true
	}
	if !visit(documents, 0) {
		return nil, false
	}
	remaining = 32768
	var expand func(any, map[string]bool, int) (any, bool)
	expand = func(value any, active map[string]bool, depth int) (any, bool) {
		remaining--
		if remaining < 0 || depth > 64 {
			return nil, false
		}
		switch v := value.(type) {
		case []any:
			out := make([]any, 0, len(v))
			for _, item := range v {
				child, ok := expand(item, active, depth+1)
				if !ok {
					return nil, false
				}
				out = append(out, child)
			}
			return out, true
		case map[string]any:
			id := key(scalar(v, "@id"))
			out := map[string]any{}
			if target := index[id]; target != nil && !active[id] {
				for k, val := range target {
					out[k] = val
				}
			}
			for k, val := range v {
				out[k] = val
			}
			if id != "" && active[id] {
				return map[string]any{"@id": scalar(v, "@id")}, true
			}
			active[id] = true
			defer delete(active, id)
			for _, k := range []string{"offers", "image", "hasVariant", "mainEntity", "priceSpecification", "itemOffered"} {
				if val, exists := out[k]; exists {
					child, ok := expand(val, active, depth+1)
					if !ok {
						return nil, false
					}
					out[k] = child
				}
			}
			return out, true
		}
		return value, true
	}
	var products []map[string]any
	for _, doc := range documents {
		for _, p := range findProducts(doc, 0) {
			value, ok := expand(p, map[string]bool{}, 0)
			if !ok {
				return nil, false
			}
			products = append(products, value.(map[string]any))
			if len(products) > maximumProductCandidates {
				return nil, false
			}
		}
	}
	return products, true
}
