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
	for _, key := range []string{"og:image:secure_url", "og:image", "og:image:url", "twitter:image", "twitter:image:src"} {
		candidates = append(candidates, metaValues(root, key)...)
	}
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
		type size struct {
			url   string
			width int
		}
		var sources []size
		for _, part := range strings.Split(attr(n, "srcset"), ",") {
			fields := strings.Fields(part)
			if len(fields) == 2 {
				width, _ := strconv.Atoi(strings.TrimSuffix(fields[1], "w"))
				sources = append(sources, size{fields[0], width})
			}
		}
		sort.SliceStable(sources, func(i, j int) bool { return sources[i].width > sources[j].width })
		for _, s := range sources {
			candidates = append(candidates, s.url)
		}
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
	return result
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
