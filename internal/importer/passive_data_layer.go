package importer

import (
	"encoding/json"
	"net/url"
	"strings"

	"golang.org/x/net/html"
)

// passiveDataLayerPrice reads a serialized commerce data layer without executing
// page scripts. Require a uniquely identified product, selected offer, matching
// in-stock offer entry and a corroborating displayed euro amount. Structured
// product data remains authoritative, including unresolved variant prices.
func passiveDataLayerPrice(root *html.Node, base *url.URL) *json.Number {
	if base.RawQuery != "" || hasUnresolvedChoices(root) {
		return nil
	}
	headings := selectNodes(root, func(n *html.Node) bool { return n.Data == "h1" })
	if len(headings) != 1 || strings.TrimSpace(nodeText(headings[0])) == "" {
		return nil
	}
	name := selectionText(nodeText(headings[0]))
	var candidates []price
	count := 0
	for _, script := range selectNodes(root, func(n *html.Node) bool { return n.Data == "script" }) {
		text := nodeText(script)
		if len(text) > 512*1024 || !strings.Contains(text, `\"currentOffer\"`) {
			continue
		}
		for _, raw := range serializedJSONStrings(text) {
			count++
			if count > 32 {
				return nil
			}
			var layer struct {
				Product []struct {
					Info struct {
						Name string `json:"productName"`
						URL  string `json:"productURL"`
					} `json:"productInfo"`
					Attributes struct {
						Availability string       `json:"availabilityType"`
						OfferID      string       `json:"offerID"`
						Current      layerOffer   `json:"currentOffer"`
						Offers       []layerOffer `json:"offer"`
					} `json:"attributes"`
				} `json:"product"`
			}
			if json.Unmarshal([]byte(raw), &layer) != nil || len(layer.Product) > maximumProductCandidates {
				continue
			}
			for _, item := range layer.Product {
				if selectionText(item.Info.Name) != name || !matchesDocument(item.Info.URL, base) {
					continue
				}
				a := item.Attributes
				if a.Availability != "in-stock" || a.OfferID == "" || a.OfferID != a.Current.ID || len(a.Offers) == 0 || len(a.Offers) > maximumProductCandidates {
					return nil
				}
				candidate := a.Current.euroAmount()
				matched := 0
				for _, offer := range a.Offers {
					if offer.ID == a.OfferID {
						matched++
						if candidate == "" || offer.euroAmount() != candidate {
							return nil
						}
					}
				}
				if matched != 1 || !visibleAmount(root, candidate) {
					return nil
				}
				candidates = append(candidates, price{candidate, "EUR"})
			}
		}
	}
	selected, _ := choosePrice(candidates)
	return selected
}

type layerOffer struct {
	ID    string `json:"offerID"`
	Price struct {
		Amount   json.Number `json:"price"`
		WithTax  json.Number `json:"priceWithTax"`
		Shipping json.Number `json:"shipping"`
		Currency string      `json:"currency"`
	} `json:"price"`
}

func (offer layerOffer) euroAmount() string {
	amount, taxed := parseAmount(offer.Price.Amount.String()), parseAmount(offer.Price.WithTax.String())
	if amount == nil || taxed == nil || amount.String() != taxed.String() || amount.String() == "0" || !strings.EqualFold(offer.Price.Currency, "EUR") {
		return ""
	}
	if shipping := offer.Price.Shipping.String(); shipping != "" {
		if !decimalAmount.MatchString(shipping) || !strings.Contains(shipping, "0") || strings.Trim(shipping, "0.") != "" {
			return ""
		}
	}
	return amount.String()
}

// Limit work and decode JSON literals only; no JavaScript is interpreted.
func serializedJSONStrings(script string) []string {
	var result []string
	for i := 0; i < len(script); i++ {
		if script[i] != '"' {
			continue
		}
		start := i
		for i++; i < len(script); i++ {
			if script[i] == '\\' {
				i++
				continue
			}
			if script[i] == '"' {
				break
			}
		}
		if i >= len(script) {
			break
		}
		literal := script[start : i+1]
		if !strings.HasPrefix(literal, `"{`) || !strings.Contains(literal, `\"currentOffer\"`) {
			continue
		}
		var decoded string
		if json.Unmarshal([]byte(literal), &decoded) == nil && json.Valid([]byte(decoded)) {
			result = append(result, decoded)
		}
		if len(result) > 32 {
			return result
		}
	}
	return result
}

func hasUnresolvedChoices(root *html.Node) bool {
	return len(selectNodes(root, func(n *html.Node) bool {
		role := strings.ToLower(attr(n, "role"))
		return n.Data == "select" || n.Data == "input" && strings.EqualFold(attr(n, "type"), "radio") || role == "listbox" || role == "radiogroup" || role == "radio"
	})) != 0
}

func visibleAmount(root *html.Node, expected string) bool {
	var visit func(*html.Node) bool
	visit = func(n *html.Node) bool {
		if n.Data == "script" || n.Data == "style" || n.Data == "noscript" || hasAttr(n, "hidden") || attr(n, "aria-hidden") == "true" {
			return false
		}
		if n.Type == html.TextNode && len(n.Data) < 256 {
			for _, match := range visibleEuroPrice.FindAllStringSubmatch(n.Data, -1) {
				if match[2] != "" {
					continue
				}
				amount := strings.TrimSpace(match[1])
				if commaAmount.MatchString(amount) {
					amount = strings.Replace(amount, ",", ".", 1)
				}
				if parsed := parseAmount(amount); parsed != nil && parsed.String() == expected {
					return true
				}
			}
		}
		for child := n.FirstChild; child != nil; child = child.NextSibling {
			if visit(child) {
				return true
			}
		}
		return false
	}
	return visit(root)
}
