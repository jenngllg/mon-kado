package importer

import (
	"bytes"
	"encoding/json"
	"fmt"
	"github.com/JennGllg/mon-kado/internal/validation"
	"golang.org/x/net/html"
	"net/url"
	"regexp"
	"strings"
	"unicode/utf8"
)

// Metadata contains untrusted suggestions that have passed text and monetary validation.
type Metadata struct {
	Name, Image         *string
	Price               *json.Number
	UnsupportedCurrency bool
	// Images contains ordered alternative product images, bounded by the preview pipeline.
	Images []string
}
type price struct{ Amount, Currency string }

const maximumProductCandidates = 256

// Extract reads passive metadata, selecting only unambiguous page identities and euro amounts.
func Extract(content []byte, base *url.URL) Metadata {
	root, err := html.Parse(bytes.NewReader(content))
	if err != nil {
		return Metadata{}
	}
	var documents []any
	for _, script := range selectNodes(root, func(n *html.Node) bool {
		return n.Data == "script" && strings.EqualFold(attr(n, "type"), "application/ld+json")
	}) {
		var value any
		text := escapeControls(nodeText(script))
		decoder := json.NewDecoder(strings.NewReader(text))
		decoder.UseNumber()
		if json.Valid([]byte(text)) && decoder.Decode(&value) == nil {
			documents = append(documents, value)
		}
	}
	products, valid := linkedProducts(documents, base)
	if !valid {
		return Metadata{}
	}
	if len(products) == 0 {
		products = microdata(root, base)
	}
	if len(products) > maximumProductCandidates {
		return Metadata{}
	}
	if len(products) == 0 && browserChallenge(root) {
		return Metadata{}
	}
	canonical := documentReference(root, base)
	product, ambiguous := selectProduct(products, base, canonical)
	if ambiguous {
		return Metadata{}
	}
	product = selectedMarkupVariant(root, product, base, canonical)
	// A redirected storefront or category must not become a product suggestion.
	if product == nil && canonical.String() != base.String() &&
		(canonical.Path == "" || canonical.Path == "/" || canonical.Hostname() != base.Hostname()) {
		return Metadata{}
	}
	result := Metadata{}
	names := []string{scalar(product, "name"), readMeta(root, "og:title"), firstText(root, func(n *html.Node) bool { return n.Data == "title" }), firstText(root, func(n *html.Node) bool { return n.Data == "h1" })}
	if variantLabel.MatchString(strings.TrimSpace(names[0])) {
		names = append(names[1:], names[0])
	}
	for _, name := range names {
		if result.Name = cleanName(name); result.Name != nil {
			if name != scalar(product, "name") && siteBrandName(*result.Name, root, base) {
				result.Name = nil
				continue
			}
			break
		}
	}
	result.Images = imageCandidates(root, base, product, result.Name)
	if len(result.Images) > 0 {
		result.Image = &result.Images[0]
	}
	prices := offerPrices(productOffers(product, base, canonical))
	if len(prices) == 0 {
		prices = standaloneOfferPrices(root, base, canonical)
	}
	if len(prices) == 0 {
		// Both metadata vocabularies describe the page's product. Keep conflicting
		// declarations visible to choosePrice instead of silently picking one.
		for _, prefix := range []string{"product:price:", "og:price:"} {
			amount, currency := readMeta(root, prefix+"amount"), readMeta(root, prefix+"currency")
			// Localized HTML metadata can use a decimal comma. JSON-LD remains strict;
			// grouped or mixed separators are never interpreted as a price.
			if commaAmount.MatchString(amount) {
				amount = strings.Replace(amount, ",", ".", 1)
			}
			if amount != "" || currency != "" {
				prices = append(prices, price{amount, currency})
			}
		}
	}
	result.Price, result.UnsupportedCurrency = choosePrice(prices)
	if result.Price == nil && !result.UnsupportedCurrency {
		result.Price = selectedStateOffer(root, product, base, canonical)
	}
	if len(prices) == 0 && result.Price == nil && !result.UnsupportedCurrency {
		result.Price = passiveProductStatePrice(root, product, base, canonical)
	}
	return result
}

func attr(n *html.Node, key string) string {
	if n == nil {
		return ""
	}
	for _, a := range n.Attr {
		if a.Key == key {
			return a.Val
		}
	}
	return ""
}
func hasAttr(n *html.Node, key string) bool {
	if n == nil {
		return false
	}
	for _, a := range n.Attr {
		if a.Key == key {
			return true
		}
	}
	return false
}
func token(value, key string) bool {
	for _, part := range strings.Fields(value) {
		if part == key {
			return true
		}
	}
	return false
}
func selectNodes(root *html.Node, predicate func(*html.Node) bool) []*html.Node {
	var result []*html.Node
	var visit func(*html.Node)
	visit = func(n *html.Node) {
		if n.Type == html.ElementNode && predicate(n) {
			result = append(result, n)
		}
		for child := n.FirstChild; child != nil; child = child.NextSibling {
			visit(child)
		}
	}
	visit(root)
	return result
}
func nodeText(root *html.Node) string {
	if root == nil {
		return ""
	}
	var text strings.Builder
	var visit func(*html.Node)
	visit = func(n *html.Node) {
		if n.Type == html.TextNode {
			text.WriteString(n.Data)
		}
		for child := n.FirstChild; child != nil; child = child.NextSibling {
			visit(child)
		}
	}
	visit(root)
	return text.String()
}
func normalizeText(value string) string { return strings.Join(strings.Fields(value), " ") }
func firstText(root *html.Node, predicate func(*html.Node) bool) string {
	nodes := selectNodes(root, predicate)
	if len(nodes) == 0 {
		return ""
	}
	return nodeText(nodes[0])
}
func unique(values []string) string {
	distinct := map[string]bool{}
	for _, v := range values {
		if v != "" {
			distinct[v] = true
		}
	}
	if len(distinct) == 1 {
		for v := range distinct {
			return v
		}
	}
	return ""
}
func cleanName(name string) *string {
	name = normalizeText(html.UnescapeString(name))
	if name == "" || !validation.Text(name, utf8.RuneCountInString(name), false) {
		return nil
	}
	if runes := []rune(name); len(runes) > 100 {
		name = strings.TrimSpace(string(runes[:100]))
		if i := strings.LastIndex(name, " "); i >= 60 {
			name = name[:i]
		}
	}
	return &name
}
func readMeta(root *html.Node, key string) string {
	var values []string
	for _, n := range selectNodes(root, func(n *html.Node) bool { return n.Data == "meta" }) {
		property := attr(n, "property")
		if !hasAttr(n, "property") {
			property = attr(n, "name")
		}
		if strings.EqualFold(property, key) {
			values = append(values, strings.TrimSpace(attr(n, "content")))
		}
	}
	return unique(values)
}
func scalar(object map[string]any, key string) string {
	switch value := object[key].(type) {
	case string:
		return value
	case json.Number:
		return value.String()
	}
	return ""
}
func findProducts(value any, depth int) []map[string]any {
	if depth > 64 {
		return nil
	}
	var products []map[string]any
	switch object := value.(type) {
	case []any:
		for _, item := range object {
			products = append(products, findProducts(item, depth+1)...)
			if len(products) > maximumProductCandidates {
				return products[:maximumProductCandidates+1]
			}
		}
	case map[string]any:
		if productType(object["@type"]) || schemaKind(object["@type"], "ProductGroup") || offeredEntity(object) {
			products = append(products, object)
		} else {
			products = append(products, findProducts(object["@graph"], depth+1)...)
			products = append(products, findProducts(object["mainEntity"], depth+1)...)
		}
	}
	return products
}
func productType(value any) bool {
	switch kind := value.(type) {
	case string:
		return kind == "Product" || kind == "https://schema.org/Product" || kind == "http://schema.org/Product"
	case []any:
		for _, v := range kind {
			if productType(v) {
				return true
			}
		}
	}
	return false
}
func escapeControls(value string) string {
	var result strings.Builder
	inside, escaped := false, false
	for _, r := range value {
		if inside && !escaped && r < ' ' {
			fmt.Fprintf(&result, `\u%04x`, r)
		} else {
			result.WriteRune(r)
		}
		if r == '"' && !escaped {
			inside = !inside
		}
		escaped = inside && r == '\\' && !escaped
	}
	return result.String()
}
func offerNodes(product map[string]any) []map[string]any {
	var result []map[string]any
	switch offers := product["offers"].(type) {
	case map[string]any:
		result = append(result, offers)
	case []any:
		for _, raw := range offers {
			offer, _ := raw.(map[string]any)
			result = append(result, offer)
		}
	}
	return result
}
func matchesDocument(candidate string, base *url.URL) bool {
	if strings.TrimSpace(candidate) == "" {
		return false
	}
	u, err := url.Parse(candidate)
	if err != nil {
		return false
	}
	u = base.ResolveReference(u)
	return u.Scheme == base.Scheme && u.Host == base.Host && u.EscapedPath() == base.EscapedPath() && (u.RawQuery == "" || u.RawQuery == base.RawQuery)
}
func readImage(value any) string {
	if list, ok := value.([]any); ok {
		if len(list) == 0 {
			return ""
		}
		value = list[0]
	}
	switch image := value.(type) {
	case string:
		return image
	case map[string]any:
		return scalar(image, "url")
	}
	return ""
}
func resolveImage(root *html.Node, base *url.URL, value string) *string {
	value = strings.TrimSpace(value)
	if value == "" {
		return nil
	}
	u, err := url.Parse(value)
	if err != nil {
		return nil
	}
	resolved := base.ResolveReference(u).String()
	if !u.IsAbs() {
		var visible []string
		for _, n := range selectNodes(root, func(n *html.Node) bool { return n.Data == "img" && hasAttr(n, "src") && attr(n, "alt") == value }) {
			v, err := url.Parse(attr(n, "src"))
			if err == nil {
				address := base.ResolveReference(v).String()
				if validation.HTTPURL(address) {
					visible = append(visible, address)
				}
			}
		}
		if equivalent := unique(visible); equivalent != "" {
			resolved = equivalent
		}
	}
	if !validation.HTTPURL(resolved) {
		return nil
	}
	return &resolved
}

var decimalAmount = regexp.MustCompile(`^[0-9]*(?:\.[0-9]*)?$`)
var commaAmount = regexp.MustCompile(`^[0-9]+,[0-9]{1,2}$`)

func parseAmount(value string) *json.Number {
	if !decimalAmount.MatchString(value) {
		return nil
	}
	whole, fraction, _ := strings.Cut(value, ".")
	whole = strings.TrimLeft(whole, "0")
	fraction = strings.TrimRight(fraction, "0")
	if len(whole) > 8 || len(fraction) > 2 || whole == "" && fraction == "" {
		return nil
	}
	if whole == "" {
		whole = "0"
	}
	canonical := whole
	if fraction != "" {
		canonical += "." + fraction
	}
	number := json.Number(canonical)
	return &number
}
func choosePrice(prices []price) (*json.Number, bool) {
	distinct := map[string]*json.Number{}
	unsupported := false
	for _, p := range prices {
		if p.Amount != "" && !strings.EqualFold(p.Currency, "EUR") {
			unsupported = true
		}
		amount := parseAmount(p.Amount)
		key := "invalid"
		if amount != nil {
			key = amount.String()
		}
		distinct[key] = amount
	}
	if !unsupported && len(distinct) == 1 {
		for _, amount := range distinct {
			return amount, false
		}
	}
	return nil, unsupported
}
func nearestScope(n *html.Node) *html.Node {
	for ; n != nil; n = n.Parent {
		if hasAttr(n, "itemscope") {
			return n
		}
	}
	return nil
}
func schemaType(n *html.Node, kind string) bool {
	return hasAttr(n, "itemscope") && (token(attr(n, "itemtype"), "https://schema.org/"+kind) || token(attr(n, "itemtype"), "http://schema.org/"+kind))
}
func microdata(root *html.Node, base *url.URL) []map[string]any {
	var products, headingProducts []map[string]any
	canonical := documentReference(root, base)
	for _, rdfa := range []bool{false, true} {
		for _, node := range selectNodes(root, func(n *html.Node) bool {
			return markupType(n, "Product", rdfa) || markupType(n, "ProductGroup", rdfa)
		}) {
			product := markupObject(root, node, rdfa, 0)
			if scalar(product, "sku") == "" {
				product["sku"] = normalizeText(attr(node, "data-product-sku"))
			}
			products = append(products, product)
			headings := selectNodes(node, func(n *html.Node) bool {
				return n.Data == "h1" && markupScope(n, rdfa) == node
			})
			if len(headings) == 1 && scalar(product, "name") != "" && selectionText(nodeText(headings[0])) == selectionText(scalar(product, "name")) {
				_, hasURL := product["url"]
				validIdentity := !hasURL || productMatch(product, base, canonical) > 0
				for _, key := range []string{"itemid", "about"} {
					if declared := attr(node, key); declared != "" && !matchesDocument(declared, base) && !matchesDocument(declared, canonical) {
						validIdentity = false
					}
				}
				if validIdentity {
					headingProducts = append(headingProducts, product)
				}
			}
			if len(products) > maximumProductCandidates {
				return products
			}
		}
	}
	if len(headingProducts) == 1 {
		// A uniquely owned main heading can identify a URL-less primary item
		// among recommendation scopes. Never override a different explicit URL.
		var selected []map[string]any
		for _, product := range products {
			if productMatch(product, base, canonical) > 0 && !sameProduct(product, headingProducts[0], base) {
				return products
			}
			if sameProduct(product, headingProducts[0], base) {
				selected = append(selected, product)
			}
		}
		return selected
	}
	return products
}
func browserChallenge(root *html.Node) bool {
	title := strings.ToLower(firstText(root, func(n *html.Node) bool { return n.Data == "title" }))
	for _, heading := range []string{title, firstText(root, func(n *html.Node) bool { return n.Data == "h1" })} {
		switch strings.Trim(strings.ToLower(normalizeText(heading)), " .!") {
		case "you have been blocked", "access denied", "request blocked", "request rejected":
			return true
		}
	}
	return len(selectNodes(root, func(n *html.Node) bool {
		switch attr(n, "id") {
		case "challenge-form", "cf-challenge-running", "px-captcha", "sec-if-cpt-container":
			return true
		}
		return n.Data == "script" && (strings.Contains(title, "just a moment") || strings.Contains(title, "access denied") || strings.Contains(title, "attention required")) && (strings.Contains(attr(n, "src"), "/cdn-cgi/challenge-platform/") || strings.Contains(nodeText(n), "/cdn-cgi/challenge-platform/"))
	})) > 0
}
