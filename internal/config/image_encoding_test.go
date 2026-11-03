package config_test

import (
	"github.com/JennGllg/mon-kado/internal/config"
	"testing"
)

func TestLoad_WhenImageEncodingSwitchIsMalformed_RejectsConfiguration(t *testing.T) {
	// Arrange
	t.Setenv("IMAGE_FAST_ENCODING_ENABLED", "invalid")
	// Act
	_, err := config.Load()
	// Assert
	if err == nil || err.Error() != "IMAGE_FAST_ENCODING_ENABLED must be a boolean" {
		t.Fatal("invalid image encoding switch accepted")
	}
}
