package importer

import (
	"golang.org/x/net/html"
	"net/url"
	"path"
	"regexp"
	"sort"
	"strconv"
	"strings"
)

var variantLabel = regexp.MustCompile(`(?i)^\d+(?:[.,]\d+)?\s*(?:ml|cl|litres?|kg|g|cm|mm|oz)\b|^(?:taille|size)\s`)
var safeVariantIdentifier = regexp.MustCompile(`^[A-Za-z0-9_-]{4,128}$`)

func imageValues(value any) []string {
	return collectImageValues(value, 0)
}

func collectImageValues(value any, depth int) []string {
	if depth > 64 {
		return nil
	}
	var result []string
	switch v := value.(type) {
	case string:
		result = append(result, v)
	case []any:
		for _, item := range v {
			result = append(result, collectImageValues(item, depth+1)...)
			if len(result) >= 5 {
				return result[:5]
			}
		}
	case map[string]any:
		for _, key := range []string{"contentUrl", "url", "thumbnailUrl"} {
			if s := scalar(v, key); s != "" {
				result = append(result, s)
			}
		}
	}
	return result
}

func metaValues(root *html.Node, key string) []string {
	var values []string
	for _, n := range selectNodes(root, func(n *html.Node) bool { return n.Data == "meta" }) {
		property := attr(n, "property")
		if property == "" {
			property = attr(n, "name")
		}
		if strings.EqualFold(property, key) {
			values = append(values, attr(n, "content"))
		}
	}
	return values
}

func imageCandidates(root *html.Node, base *url.URL, product map[string]any, name *string) []string {
	candidates := imageValues(product["image"])
	variants := groupVariants(product, base, documentReference(root, base))
	var variantImages []string
	for _, variant := range variants {
		variantImages = append(variantImages, imageValues(variant["image"])...)
	}
	if len(variants) == 1 {
		candidates = append(variantImages, candidates...)
	} else if unique(variantImages) != "" {
		candidates = append(candidates, variantImages...)
	}
	if selected := priorityVariantImage(root, base, product); selected != "" {
		candidates = append([]string{selected}, candidates...)
	}
	// Keep the selected product's primary picture first, but give the page's
	// explicit preview image a chance before a long gallery consumes the budget.
	var gallery []string
	if len(candidates) > 1 {
		gallery = candidates[1:]
		candidates = candidates[:1:1]
	}
	for _, key := range []string{"og:image:secure_url", "og:image", "og:image:url", "twitter:image", "twitter:image:src"} {
		candidates = append(candidates, metaValues(root, key)...)
	}
	candidates = append(candidates, gallery...)
	for _, n := range selectNodes(root, func(n *html.Node) bool { return n.Data == "link" && token(attr(n, "rel"), "image_src") }) {
		candidates = append(candidates, attr(n, "href"))
	}
	for _, n := range selectNodes(root, func(n *html.Node) bool { return n.Data == "img" }) {
		if !token(attr(n, "itemprop"), "image") && !matchingImageText(attr(n, "alt"), name) {
			continue
		}
		for _, key := range []string{"data-src", "data-original", "src"} {
			if s := attr(n, key); s != "" {
				candidates = append(candidates, s)
			}
		}
		candidates = append(candidates, widthImageSources(attr(n, "srcset"))...)
	}
	var result []string
	seen := map[string]bool{}
	for _, candidate := range candidates {
		address := resolveImage(root, base, html.UnescapeString(candidate))
		if address != nil && !brandingImage(*address) && !seen[*address] {
			seen[*address] = true
			result = append(result, *address)
			if len(result) == 5 {
				break
			}
		}
	}
	// Invalid placeholders or branding are not usable product images. They
	// must not prevent a separately verified selected-variant preload fallback.
	if len(result) == 0 && name != nil {
		if selected := preloadedSelectedImage(root, base); selected != "" {
			result = append(result, selected)
		}
	}
	return result
}

// Commas can occur inside an image URL; only a comma after a width descriptor
// separates srcset candidates. Unsupported descriptor forms are ignored.
func widthImageSources(srcset string) []string {
	type source struct {
		url   string
		width int
	}
	var sources []source
	for i, count := 0, 0; i < len(srcset) && count < 64; count++ {
		for i < len(srcset) && (srcsetSpace(srcset[i]) || srcset[i] == ',') {
			i++
		}
		start := i
		for i < len(srcset) && !srcsetSpace(srcset[i]) {
			i++
		}
		address := srcset[start:i]
		for i < len(srcset) && srcsetSpace(srcset[i]) {
			i++
		}
		start = i
		for i < len(srcset) && !srcsetSpace(srcset[i]) && srcset[i] != ',' {
			i++
		}
		descriptor := srcset[start:i]
		for i < len(srcset) && srcsetSpace(srcset[i]) {
			i++
		}
		validEnd := i == len(srcset) || srcset[i] == ','
		for i < len(srcset) && srcset[i] != ',' {
			i++
		}
		if !validEnd || !strings.HasSuffix(descriptor, "w") {
			continue
		}
		width, err := strconv.Atoi(strings.TrimSuffix(descriptor, "w"))
		if err == nil && width > 0 && address != "" {
			sources = append(sources, source{address, width})
		}
	}
	sort.SliceStable(sources, func(i, j int) bool {
		if (sources[i].width <= 1600) != (sources[j].width <= 1600) {
			return sources[i].width <= 1600
		}
		if sources[i].width > 1600 {
			return sources[i].width < sources[j].width
		}
		return sources[i].width > sources[j].width
	})
	result := make([]string, 0, len(sources))
	for _, item := range sources {
		result = append(result, item.url)
	}
	return result
}

func srcsetSpace(value byte) bool {
	return value == ' ' || value == '\t' || value == '\n' || value == '\r' || value == '\f'
}

// Only a single unique responsive preload explicitly containing the requested
// variant identifier can stand in for missing product image metadata.
func preloadedSelectedImage(root *html.Node, base *url.URL) string {
	query, err := url.ParseQuery(base.RawQuery)
	if err != nil || len(query["variant"]) != 1 || !safeVariantIdentifier.MatchString(query.Get("variant")) {
		return ""
	}
	for key := range query {
		if key != "variant" && !strings.HasPrefix(strings.ToLower(key), "utm_") && key != "gclid" && key != "fbclid" {
			return ""
		}
	}
	identifier := regexp.MustCompile(`(?:^|[^A-Za-z0-9])` + regexp.QuoteMeta(query.Get("variant")) + `(?:[^A-Za-z0-9]|$)`)
	var found string
	for _, link := range selectNodes(root, func(n *html.Node) bool {
		return n.Data == "link" && token(attr(n, "rel"), "preload") && strings.EqualFold(attr(n, "as"), "image")
	}) {
		sources := widthImageSources(attr(link, "imagesrcset"))
		if len(sources) == 0 {
			continue
		}
		candidate := resolveImage(root, base, sources[0])
		if candidate == nil || brandingImage(*candidate) {
			continue
		}
		address, err := url.Parse(*candidate)
		if err != nil || !identifier.MatchString(address.Path) {
			continue
		}
		if found != "" && found != *candidate {
			return ""
		}
		found = *candidate
	}
	return found
}

// A unique visible, high-priority product image can describe a selected color
// more precisely than the parent group's structured gallery. Require both the
// structured variant relationship and explicit markup signals before preferring it.
func priorityVariantImage(root *html.Node, base *url.URL, product map[string]any) string {
	group, ok := product["isVariantOf"].(map[string]any)
	if !ok || !schemaKind(product["@type"], "Product") || !schemaKind(group["@type"], "ProductGroup") || scalar(product, "color") == "" || len(imageValues(product["image"])) == 0 {
		return ""
	}
	var found string
	for _, n := range selectNodes(root, func(n *html.Node) bool { return n.Data == "img" }) {
		width, widthErr := strconv.Atoi(attr(n, "width"))
		height, heightErr := strconv.Atoi(attr(n, "height"))
		if !strings.EqualFold(attr(n, "fetchpriority"), "high") || !token(attr(n, "class"), "product") || widthErr != nil || heightErr != nil || width < 200 || height < 200 {
			continue
		}
		if hiddenMarkup(n) {
			continue
		}
		candidate := resolveImage(root, base, attr(n, "src"))
		if candidate == nil || brandingImage(*candidate) {
			continue
		}
		if found != "" && found != *candidate {
			return ""
		}
		found = *candidate
	}
	return found
}

func hiddenMarkup(node *html.Node) bool {
	for ancestor := node; ancestor != nil; ancestor = ancestor.Parent {
		style := strings.ToLower(strings.Join(strings.Fields(attr(ancestor, "style")), ""))
		if hasAttr(ancestor, "hidden") || token(attr(ancestor, "class"), "hidden") || strings.EqualFold(attr(ancestor, "aria-hidden"), "true") || strings.Contains(style, "display:none") || strings.Contains(style, "visibility:hidden") {
			return true
		}
	}
	return false
}

func brandingImage(value string) bool {
	u, err := url.Parse(value)
	if err != nil {
		return true
	}
	p := strings.ToLower(u.Path)
	base := path.Base(p)
	return strings.Contains(p, "/logo/") || strings.Contains(p, "/icons/") || strings.HasPrefix(base, "favicon.") || base == "logo.png" || base == "logo.svg" || base == "logo.jpg" || base == "logo.webp"
}

func matchingImageText(alt string, name *string) bool {
	if name == nil || len([]rune(strings.TrimSpace(alt))) < 5 {
		return false
	}
	a := strings.Fields(strings.ToLower(html.UnescapeString(alt)))
	n := strings.Fields(strings.ToLower(*name))
	if len(a) == 0 || len(n) == 0 {
		return false
	}
	matches := 0
	for _, word := range n {
		if len([]rune(word)) < 3 {
			continue
		}
		for _, other := range a {
			if word == other {
				matches++
				break
			}
		}
	}
	return matches >= 2 && matches*2 >= len(n)
}
