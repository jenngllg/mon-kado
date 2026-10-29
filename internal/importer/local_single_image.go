package importer

import (
	"context"
	"errors"
	"net/url"
)

// NewLocalSingleImageAttempt limits a fallback to one request per preview.
// The wrapper does not share attempt counters between concurrent previews.
func NewLocalSingleImageAttempt(source ImageSource) ImageSource {
	return &localSingleImageFactory{source: source}
}

type localSingleImageFactory struct{ source ImageSource }

func (f *localSingleImageFactory) GetImage(ctx context.Context, target *url.URL) ([]byte, error) {
	return f.source.GetImage(ctx, target)
}

func (f *localSingleImageFactory) newPreviewImageSource() ImageSource {
	return &localSingleImageAttempt{source: f.source}
}

type localSingleImageAttempt struct {
	source    ImageSource
	attempted bool
}

func (s *localSingleImageAttempt) GetImage(ctx context.Context, target *url.URL) ([]byte, error) {
	if err := ctx.Err(); err != nil {
		return nil, err
	}
	if s.attempted {
		return nil, errors.New("local image attempt already used")
	}
	s.attempted = true
	return s.source.GetImage(ctx, target)
}
