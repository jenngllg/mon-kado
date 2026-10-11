package domain

// PaginatedResponse carries one page and the real collection metadata.
type PaginatedResponse[T any] struct {
	Items           []T  `json:"items"`
	CurrentPage     int  `json:"currentPage"`
	PageSize        int  `json:"pageSize"`
	TotalCount      int  `json:"totalCount"`
	TotalPages      int  `json:"totalPages"`
	HasPreviousPage bool `json:"hasPreviousPage"`
	HasNextPage     bool `json:"hasNextPage"`
}

// Page preserves out-of-range page numbers and serializes empty items as an array.
func Page[T any](items []T, page, size, total int) PaginatedResponse[T] {
	if items == nil {
		items = []T{}
	}
	pages := (total + size - 1) / size
	return PaginatedResponse[T]{items, page, size, total, pages, total > 0 && page > 1, page < pages}
}
