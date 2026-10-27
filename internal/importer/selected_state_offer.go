package importer

import (
	"encoding/json"
	"net/url"
	"regexp"
	"strconv"
	"strings"

	"golang.org/x/net/html"
)

var visibleEuroPrice = regexp.MustCompile(`(?i)([0-9][0-9.,\s\x{00a0}\x{202f}]*)(?:€|EUR\b)([0-9.,]?)`)

// A displayed offer can refine an aggregate range when passive component state
// explicitly binds its selected SKU to that button's price. The range minimum,
// arbitrary page text and undocumented integer price units are never inferred.
func selectedStateOffer(root *html.Node, product map[string]any, base, canonical *url.URL) *json.Number {
	if productMatch(product, base, canonical) == 0 || scalar(product, "name") == "" || base.RawQuery != "" {
		return nil
	}
	offers := productOffers(product, base, canonical)
	if len(offers) != 1 || len(offerNodes(offers[0])) > 0 || !schemaKind(offers[0]["@type"], "AggregateOffer") || !strings.EqualFold(scalar(offers[0], "priceCurrency"), "EUR") || !schemaKind(scalar(offers[0], "availability"), "InStock") {
		return nil
	}
	low, high := parseAmount(scalar(offers[0], "lowPrice")), parseAmount(scalar(offers[0], "highPrice"))
	if low == nil || high == nil || minorAmount(low) > minorAmount(high) {
		return nil
	}
	// Scope component state to the article containing the product heading, not
	// recommendation cards, navigation or unrelated embedded applications.
	var scope *html.Node
	headings := 0
	for _, heading := range selectNodes(root, func(n *html.Node) bool { return n.Data == "h1" }) {
		if selectionText(nodeText(heading)) != selectionText(scalar(product, "name")) {
			continue
		}
		headings++
		if headings > 1 {
			return nil
		}
		scope = containingArticle(heading)
	}
	if scope == nil {
		return nil
	}
	var candidates []price
	states := 0
	for _, node := range selectNodes(scope, func(n *html.Node) bool {
		return n.Data != "script" && n.Data != "style" && containingArticle(n) == scope
	}) {
		for _, attribute := range node.Attr {
			if !strings.HasPrefix(attribute.Key, "data-") || !strings.HasSuffix(attribute.Key, "-value") && attribute.Key != "data-state" || !strings.HasPrefix(strings.TrimSpace(attribute.Val), "{") {
				continue
			}
			states++
			if states > 32 || len(attribute.Val) > 128*1024 {
				return nil
			}
			var state struct {
				SelectedSKU string `json:"selectedSku"`
				Offers      []struct {
					SKU string `json:"sku"`
				} `json:"offers"`
			}
			if json.Unmarshal([]byte(attribute.Val), &state) != nil || state.SelectedSKU == "" || len(state.SelectedSKU) > 128 || len(state.Offers) < 1 || len(state.Offers) > maximumProductCandidates {
				continue
			}
			matched := 0
			for _, offer := range state.Offers {
				if offer.SKU == state.SelectedSKU {
					matched++
				}
			}
			if matched != 1 {
				continue
			}
			if count := scalar(offers[0], "offerCount"); count != "" && count != strconv.Itoa(len(state.Offers)) {
				continue
			}
			buttons := selectNodes(node, func(n *html.Node) bool {
				if n.Data != "button" || hasAttr(n, "disabled") || attr(n, "aria-disabled") == "true" || containingArticle(n) != scope {
					return false
				}
				for _, a := range n.Attr {
					if (a.Key == "data-sku" || strings.HasPrefix(a.Key, "data-") && strings.HasSuffix(a.Key, "-sku-param")) && a.Val == state.SelectedSKU {
						return true
					}
				}
				return false
			})
			if len(buttons) != 1 || len(nodeText(buttons[0])) > 2048 {
				continue
			}
			amounts := visibleEuroPrice.FindAllStringSubmatch(nodeText(buttons[0]), 3)
			if len(amounts) != 1 || amounts[0][2] != "" {
				continue
			}
			value := strings.TrimSpace(amounts[0][1])
			if commaAmount.MatchString(value) {
				value = strings.Replace(value, ",", ".", 1)
			}
			amount := parseAmount(value)
			if amount == nil || minorAmount(amount) < minorAmount(low) || minorAmount(amount) > minorAmount(high) {
				continue
			}
			candidates = append(candidates, price{amount.String(), "EUR"})
		}
	}
	selected, _ := choosePrice(candidates)
	return selected
}

func containingArticle(node *html.Node) *html.Node {
	for parent := node; parent != nil; parent = parent.Parent {
		if parent.Data == "article" {
			return parent
		}
	}
	return nil
}

// Monetary values already passed parseAmount; integer hundredths preserve their
// exact decimal comparison without binary floating point or guessed units.
func minorAmount(amount *json.Number) int64 {
	whole, fraction, _ := strings.Cut(amount.String(), ".")
	minor, _ := strconv.ParseInt(whole+fraction+strings.Repeat("0", 2-len(fraction)), 10, 64)
	return minor
}
