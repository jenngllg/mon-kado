package importer

import (
	"encoding/json"
	"net/url"
	"strings"
	"unicode"

	"golang.org/x/net/html"
)

// passiveVariantPrice reads JSON arrays embedded in scripts without executing
// them. A single URL-matched variant and a matching displayed price beside the
// product heading are required; ranges and selected-query guessing are excluded.
func passiveVariantPrice(root *html.Node, base *url.URL, name *string) *json.Number {
	clean, valid := unselectedVariantPage(base)
	if !valid || name == nil || *name == "" || multiplePageChoices(root) {
		return nil
	}
	type variant struct {
		ID      string `json:"id"`
		Product struct {
			Title string `json:"title"`
			URL   string `json:"url"`
		} `json:"product"`
		Price struct {
			Amount   json.Number `json:"amount"`
			Currency string      `json:"currencyCode"`
		} `json:"price"`
	}
	matched := map[string]variant{}
	arrays := 0
	for _, script := range selectNodes(root, func(n *html.Node) bool { return n.Data == "script" }) {
		text := nodeText(script)
		if len(text) > 512*1024 {
			continue
		}
		const key = `"productVariants"`
		for offset := 0; offset < len(text); {
			index := strings.Index(text[offset:], key)
			if index < 0 {
				break
			}
			index += offset
			offset = index + len(key)
			if index > 0 && text[index-1] == '\\' {
				continue
			}
			remaining := strings.TrimLeft(text[offset:], " \t\r\n")
			if !strings.HasPrefix(remaining, ":") {
				continue
			}
			remaining = strings.TrimLeft(remaining[1:], " \t\r\n")
			if !strings.HasPrefix(remaining, "[") {
				continue
			}
			arrays++
			if arrays > 16 {
				return nil
			}
			decoder := json.NewDecoder(strings.NewReader(remaining))
			decoder.UseNumber()
			var values []variant
			if decoder.Decode(&values) != nil || len(values) > maximumProductCandidates || decoder.InputOffset() > 128*1024 {
				continue
			}
			for _, item := range values {
				if !matchesDocument(item.Product.URL, clean) || !strings.Contains(selectionText(*name), selectionText(item.Product.Title)) || item.Product.Title == "" {
					continue
				}
				if item.ID == "" || len(item.ID) > 128 {
					return nil
				}
				if previous, exists := matched[item.ID]; exists && (previous.Price.Amount != item.Price.Amount || previous.Price.Currency != item.Price.Currency) {
					return nil
				}
				matched[item.ID] = item
			}
		}
	}
	if len(matched) != 1 {
		return nil
	}
	for _, item := range matched {
		amount := parseAmount(item.Price.Amount.String())
		if amount == nil || amount.String() == "0" || !strings.EqualFold(item.Price.Currency, "EUR") || !displayedVariantPrice(root, item.Product.Title, amount.String()) {
			return nil
		}
		return amount
	}
	return nil
}

// An incomplete serialized array must not hide a choice exposed in the page.
func multiplePageChoices(root *html.Node) bool {
	radios := map[string]map[string]bool{}
	for _, node := range selectNodes(root, func(n *html.Node) bool {
		role := strings.ToLower(attr(n, "role"))
		return n.Data == "select" || n.Data == "input" && strings.EqualFold(attr(n, "type"), "radio") || role == "radiogroup" || role == "listbox"
	}) {
		if hiddenMarkup(node) {
			continue
		}
		role := strings.ToLower(attr(node, "role"))
		if role == "radiogroup" || role == "listbox" {
			return true
		}
		if node.Data == "select" {
			values := map[string]bool{}
			for _, option := range selectNodes(node, func(n *html.Node) bool { return n.Data == "option" }) {
				if value := strings.TrimSpace(attr(option, "value")); value != "" {
					values[value] = true
				}
			}
			if len(values) > 1 {
				return true
			}
			continue
		}
		name, value := attr(node, "name"), attr(node, "value")
		if name == "" || value == "" {
			return true
		}
		if radios[name] == nil {
			radios[name] = map[string]bool{}
		}
		radios[name][value] = true
		if len(radios[name]) > 1 {
			return true
		}
	}
	return false
}

func displayedVariantPrice(root *html.Node, productName, amount string) bool {
	for _, element := range selectNodes(root, func(n *html.Node) bool {
		return hasAttr(n, "data-price") || strings.EqualFold(attr(n, "data-element"), "price") || token(attr(n, "itemprop"), "price")
	}) {
		if hiddenMarkup(element) || struckThrough(element) || !visibleAmount(element, amount) {
			continue
		}
		for scope, depth := element.Parent, 0; scope != nil && depth < 8 && scope.Data != "body"; scope, depth = scope.Parent, depth+1 {
			headings := selectNodes(scope, func(n *html.Node) bool { return n.Data == "h1" })
			if len(headings) == 1 && headingNamesProduct(nodeText(headings[0]), productName) {
				return true
			}
			if scope.Data == "article" || scope.Data == "aside" || scope.Data == "nav" || scope.Data == "footer" || scope.Data == "a" && attr(scope, "href") != "" {
				break
			}
		}
	}
	return false
}

func headingNamesProduct(heading, product string) bool {
	words := func(value string) []string {
		return strings.FieldsFunc(strings.ToLower(value), func(r rune) bool { return !unicode.IsLetter(r) && !unicode.IsDigit(r) })
	}
	nameWords := map[string]bool{}
	for _, word := range words(product) {
		nameWords[word] = true
	}
	count, matches := 0, 0
	for _, word := range words(heading) {
		if len([]rune(word)) < 3 {
			continue
		}
		count++
		if nameWords[word] {
			matches++
		}
	}
	return count >= 2 && matches >= 2 && matches*3 >= count*2
}
