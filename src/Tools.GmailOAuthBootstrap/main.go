// Command GmailOAuthBootstrap obtains a Gmail send-only secret through native loopback OAuth.
package main

import (
	"context"
	"fmt"

	"io"
	"os"
	"os/signal"
	"syscall"
	"time"
)

func main() {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	budget, cancel := context.WithTimeout(ctx, 10*time.Minute)
	defer cancel()
	os.Exit(run(budget, os.Getenv, os.Stdout, NewGmailOAuthBroker()))
}
func run(ctx context.Context, environment func(string) string, output io.Writer, broker GmailAuthorizationBroker) int {
	clientID, secret := environment("GMAIL_CLIENT_ID"), environment("GMAIL_CLIENT_SECRET")
	if clientID == "" || secret == "" {
		fmt.Fprintln(output, "Set GMAIL_CLIENT_ID and GMAIL_CLIENT_SECRET for this process before running the tool.")
		return 2
	}
	token, err := broker.Authorize(ctx, clientID, secret, output)
	if err != nil {
		fmt.Fprintln(output, "Gmail authorization failed. Check the OAuth client, consent and callback; no secret has been stored.")
		return 1
	}
	fmt.Fprintln(output, "Store this refresh token as GMAIL_REFRESH_TOKEN in your secret configuration. It is displayed once and has not been written to disk:")
	fmt.Fprintln(output, token)
	return 0
}
