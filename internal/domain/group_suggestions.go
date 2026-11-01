package domain

import (
	"encoding/json"
	"time"
)

// SaveGroupSuggestionRequest contains the editable fields of a private suggestion.
type SaveGroupSuggestionRequest struct {
	Name     *string      `json:"name"`
	Note     *string      `json:"note"`
	Url      *string      `json:"url"`
	Price    *json.Number `json:"price"`
	Quantity *int         `json:"quantity"`
}

// GroupSuggestionResponse is visible only to eligible members of its group.
type GroupSuggestionResponse struct {
	Id                                 string       `json:"id"`
	Name                               string       `json:"name"`
	Note                               *string      `json:"note"`
	Url                                *string      `json:"url"`
	Price                              *json.Number `json:"price"`
	Quantity                           int          `json:"quantity"`
	ReservedQuantity                   int          `json:"reservedQuantity"`
	CurrentParticipantReservedQuantity int          `json:"currentParticipantReservedQuantity"`
	AuthorId                           *string      `json:"authorId"`
	AuthorDisplayName                  *string      `json:"authorDisplayName"`
	CanEdit                            bool         `json:"canEdit"`
	HasImage                           bool         `json:"hasImage"`
	CreatedAt                          time.Time    `json:"createdAt"`
	UpdatedAt                          *time.Time   `json:"updatedAt"`
	EntityTag                          string       `json:"entityTag"`
}
