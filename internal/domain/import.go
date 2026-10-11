package domain

import "encoding/json"

// WishImportPreview contains editable suggestions without creating a wish.
type WishImportPreview struct {
	Name     *string          `json:"name"`
	Url      *string          `json:"url"`
	Price    *json.Number     `json:"price"`
	Quantity int              `json:"quantity"`
	Image    *WishImportImage `json:"image"`
	Warnings []string         `json:"warnings"`
}

// WishImportImage contains normalized bytes held in browser memory only.
type WishImportImage struct {
	ContentBase64 string `json:"contentBase64"`
	ContentType   string `json:"contentType"`
}
