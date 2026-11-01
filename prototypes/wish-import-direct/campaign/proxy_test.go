package main

import (
	"context"
	"testing"
)

func TestDialPublic_WhenAddressIsPrivateOrPortForbidden_Rejects(t *testing.T) {
	// Arrange
	for _, address := range []string{"127.0.0.1:80", "[::1]:443", "10.0.0.1:443", "8.8.8.8:8080", "localhost:80"} {
		t.Run(address, func(t *testing.T) {
			// Act
			conn, err := dialPublic(t.Context(), "tcp", address)
			// Assert
			if err == nil || conn != nil {
				if conn != nil {
					conn.Close()
				}
				t.Fatal("unsafe destination was accepted")
			}
		})
	}
}

func TestDialPublic_WhenContextIsCancelled_PropagatesCancellation(t *testing.T) {
	// Arrange
	ctx, cancel := context.WithCancel(t.Context())
	cancel()
	// Act
	conn, err := dialPublic(ctx, "tcp", "8.8.8.8:443")
	// Assert
	if err == nil || conn != nil {
		if conn != nil {
			conn.Close()
		}
		t.Fatal("cancelled operation opened a connection")
	}
}
