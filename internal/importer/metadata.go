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
)

// Metadata contains untrusted suggestions that have passed text and monetary validation.
type Metadata struct {
	Name, Image         *string
	Price               *json.Number
	UnsupportedCurrency bool
}
type price struct{ Amount, Currency string }

// Extract reads passive metadata, selecting only unambiguous page identities and euro amounts.
func Extract(content []byte, base *url.URL) Metadata {
	root, err := html.Parse(bytes.NewReader(content))
	if err != nil {
		return Metadata{}
	}
	if amazonHost(base.Hostname()) {
		return extractAmazon(root, base)
	}
	var products []map[string]any
	for _, script := range selectNodes(root, func(n *html.Node) bool {
		return n.Data == "script" && strings.EqualFold(attr(n, "type"), "application/ld+json")
	}) {
		var value any
		text := escapeControls(nodeText(script))
		decoder := json.NewDecoder(strings.NewReader(text))
		decoder.UseNumber()
		if json.Valid([]byte(text)) && decoder.Decode(&value) == nil {
			products = append(products, findProducts(value, 0)...)
		}
	}
	if len(products) == 0 {
		products = microdata(root)
	}
	if len(products) == 0 && browserChallenge(root) {
		return Metadata{}
	}
	if len(products) == 0 && (base.Hostname() == "fnac.com" || strings.HasSuffix(base.Hostname(), ".fnac.com")) {
		return extractFnac(root, base)
	}
	var matching []map[string]any
	for _, p := range products {
		matched := matchesDocument(scalar(p, "url"), base)
		for _, offer := range offerNodes(p) {
			matched = matched || matchesDocument(scalar(offer, "url"), base)
		}
		if matched {
			matching = append(matching, p)
		}
	}
	if len(products) > 1 && len(matching) != 1 {
		return Metadata{}
	}
	var product map[string]any
	if len(matching) == 1 {
		product = matching[0]
	} else if len(products) == 1 {
		product = products[0]
	}
	result := Metadata{}
	for _, name := range []string{scalar(product, "name"), readMeta(root, "og:title"), firstText(root, func(n *html.Node) bool { return n.Data == "title" })} {
		if result.Name = cleanName(name); result.Name != nil {
			break
		}
	}
	image := readImage(product["image"])
	if strings.TrimSpace(image) == "" {
		image = readMeta(root, "og:image")
	}
	result.Image = resolveImage(root, base, image)
	offers := offerNodes(product)
	var selected []map[string]any
	for _, offer := range offers {
		if matchesDocument(scalar(offer, "url"), base) {
			selected = append(selected, offer)
		}
	}
	if len(selected) == 0 && strings.TrimSpace(scalar(product, "sku")) != "" {
		for _, offer := range offers {
			if scalar(offer, "sku") == scalar(product, "sku") {
				selected = append(selected, offer)
			}
		}
	}
	if len(selected) > 0 {
		offers = selected
	}
	var prices []price
	hasAmount := false
	for _, offer := range offers {
		p := price{scalar(offer, "price"), scalar(offer, "priceCurrency")}
		prices = append(prices, p)
		hasAmount = hasAmount || strings.TrimSpace(p.Amount) != ""
	}
	if !hasAmount {
		prices = []price{{readMeta(root, "product:price:amount"), readMeta(root, "product:price:currency")}}
	}
	result.Price, result.UnsupportedCurrency = choosePrice(prices, false)
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
	name = strings.TrimSpace(name)
	if name == "" || !validation.Text(name, 100, false) {
		return nil
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
		}
	case map[string]any:
		if productType(object["@type"]) {
			products = append(products, object)
		} else {
			products = append(products, findProducts(object["@graph"], depth+1)...)
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
func choosePrice(prices []price, localized bool) (*json.Number, bool) {
	distinct := map[string]*json.Number{}
	unsupported := false
	for _, p := range prices {
		if p.Amount != "" && !strings.EqualFold(p.Currency, "EUR") {
			unsupported = true
		}
		var amount *json.Number
		if localized {
			amount = parseLocalizedPrice(p.Amount)
		} else {
			amount = parseAmount(p.Amount)
		}
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
func microValue(scope *html.Node, key string) string {
	var values []string
	for _, n := range selectNodes(scope, func(n *html.Node) bool {
		return !hasAttr(n, "itemscope") && token(attr(n, "itemprop"), key) && nearestScope(n) == scope
	}) {
		value := nodeText(n)
		for _, a := range []string{"content", "href", "src"} {
			if hasAttr(n, a) {
				value = attr(n, a)
				break
			}
		}
		values = append(values, normalizeText(value))
	}
	return unique(values)
}
func schemaType(n *html.Node, kind string) bool {
	return hasAttr(n, "itemscope") && (token(attr(n, "itemtype"), "https://schema.org/"+kind) || token(attr(n, "itemtype"), "http://schema.org/"+kind))
}
func microdata(root *html.Node) []map[string]any {
	var products []map[string]any
	for _, node := range selectNodes(root, func(n *html.Node) bool { return schemaType(n, "Product") }) {
		product := map[string]any{}
		for _, key := range []string{"name", "url", "image", "sku"} {
			product[key] = microValue(node, key)
		}
		if product["sku"] == "" {
			product["sku"] = normalizeText(attr(node, "data-product-sku"))
		}
		var offers []any
		for _, offer := range selectNodes(node, func(n *html.Node) bool {
			return schemaType(n, "Offer") && token(attr(n, "itemprop"), "offers") && nearestScope(n.Parent) == node
		}) {
			data := map[string]any{}
			for _, key := range []string{"price", "priceCurrency", "url", "sku"} {
				data[key] = microValue(offer, key)
			}
			offers = append(offers, data)
		}
		product["offers"] = offers
		products = append(products, product)
	}
	return products
}
func browserChallenge(root *html.Node) bool {
	return len(selectNodes(root, func(n *html.Node) bool {
		switch attr(n, "id") {
		case "challenge-form", "cf-challenge-running", "px-captcha", "sec-if-cpt-container":
			return true
		}
		return n.Data == "script" && (strings.Contains(attr(n, "src"), "/cdn-cgi/challenge-platform/") || strings.Contains(nodeText(n), "/cdn-cgi/challenge-platform/"))
	})) > 0
}
