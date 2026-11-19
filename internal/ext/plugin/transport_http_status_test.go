package plugin

import (
	"context"
	"encoding/json"
	"errors"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
)

// The status is a fact the host had at the moment it answered. Formatting it
// into the sentence was where it stopped being one: a failure record keeps only
// prose, and the settings row above it was left reading the digits back out of
// text an external server had a hand in writing.
func TestATerminalHTTPFailureKeepsItsStatusAsANumber(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		http.Error(w, "go away", http.StatusUnauthorized)
	}))
	defer srv.Close()

	_, _, err := StartAll(context.Background(), []Spec{{Name: "e", Type: "http", URL: srv.URL}})
	if err == nil {
		t.Fatal("a server answering 401 to everything did not fail the handshake")
	}
	var status *httpStatusError
	if !errors.As(err, &status) {
		t.Fatalf("no status survived to the terminal error: %v", err)
	}
	if status.Status != http.StatusUnauthorized {
		t.Errorf("status = %d, want 401", status.Status)
	}
	if strings.Contains(err.Error(), "go away") || !strings.Contains(err.Error(), "http 401") {
		t.Errorf("message = %q, want status without remote detail", err.Error())
	}
}

// The one non-2xx that already had an identity keeps it. Giving every status a
// typed error is exactly the change that could have swallowed this one, and a
// session the host can silently re-establish is not a failure to report.
func TestAnExpiredSessionIsStillItsOwnIdentity(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(http.StatusNotFound)
		_, _ = w.Write([]byte(`{"jsonrpc":"2.0","id":1,"error":{"code":-32001,"message":"Session not found"}}`))
	}))
	defer srv.Close()

	transport, err := newHTTPTransport(Spec{Name: "remote", Type: "http", URL: srv.URL})
	if err != nil {
		t.Fatal(err)
	}
	defer transport.close()
	transport.session.id = "issued-earlier"

	_, err = transport.call(context.Background(), "ping", nil)
	var expired *httpSessionExpiredError
	if !errors.As(err, &expired) {
		t.Fatalf("an expired session became an ordinary status: %v", err)
	}
	var status *httpStatusError
	if errors.As(err, &status) {
		t.Errorf("it also reads as a plain http %d, which is the arm that would swallow it", status.Status)
	}
}

// The spec's rule is the status alone: a 404 to a request that carried a
// session id means the session is gone, whatever the body says. Reading the
// body's wording to decide was a judgement made from an external server's
// prose, and a server that answers with plain text was never re-initialised.
func TestA404ToASessionRequestIsExpiryWhateverTheBody(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodDelete {
			w.WriteHeader(http.StatusMethodNotAllowed)
			return
		}
		w.WriteHeader(http.StatusNotFound)
		_, _ = w.Write([]byte("not found"))
	}))
	defer srv.Close()
	transport, err := newHTTPTransport(Spec{Name: "remote", Type: "http", URL: srv.URL})
	if err != nil {
		t.Fatal(err)
	}
	defer transport.close()

	_, err = transport.call(context.Background(), "ping", nil)
	var expired *httpSessionExpiredError
	if errors.As(err, &expired) {
		t.Fatalf("a 404 with no session held is a wrong address, not an expiry: %v", err)
	}

	transport.session.id = "issued-earlier"
	_, err = transport.call(context.Background(), "ping", nil)
	if !errors.As(err, &expired) {
		t.Fatalf("a 404 to a session request = %v, want an expired session", err)
	}
	if transport.sessionID() != "" {
		t.Fatal("the expired session id was kept")
	}
}

// mcp-for-unity answers the pre-handshake probe with 400 and a fresh
// Mcp-Session-Id. Adopting it pointed the legacy initialize that followed at a
// session no request opened, and that initialize came back 404.
func TestAFailedAnswerDoesNotIssueASession(t *testing.T) {
	const probed = "issued-by-the-refused-probe"
	var initialized bool
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		var req struct {
			ID     *int   `json:"id"`
			Method string `json:"method"`
		}
		_ = json.NewDecoder(r.Body).Decode(&req)
		w.Header().Set("Content-Type", "application/json")
		if req.Method == "server/discover" {
			w.Header().Set("Mcp-Session-Id", probed)
			w.WriteHeader(http.StatusBadRequest)
			_, _ = w.Write([]byte(`{"jsonrpc":"2.0","id":"server-error","error":{"code":-32600,"message":"Missing session ID"}}`))
			return
		}
		if got := r.Header.Get("Mcp-Session-Id"); got == probed {
			w.WriteHeader(http.StatusNotFound)
			_, _ = w.Write([]byte(`{"jsonrpc":"2.0","id":"server-error","error":{"code":-32600,"message":"Session not found"}}`))
			return
		}
		switch req.Method {
		case "initialize":
			initialized = true
			w.Header().Set("Mcp-Session-Id", "real-session")
		case "tools/list":
			w.Header().Set("Mcp-Session-Id", "real-session")
		}
		if req.ID == nil {
			w.WriteHeader(http.StatusAccepted)
			return
		}
		result := map[string]any{}
		if req.Method == "initialize" {
			result = map[string]any{"protocolVersion": protocolVersion, "capabilities": map[string]any{"tools": map[string]any{}}}
		}
		_ = json.NewEncoder(w).Encode(map[string]any{"jsonrpc": "2.0", "id": *req.ID, "result": result})
	}))
	defer srv.Close()

	host, tools, err := StartAll(context.Background(), []Spec{{Name: "unity", Type: "http", URL: srv.URL}})
	if err != nil {
		t.Fatalf("a session id in a refused probe blocked the handshake: %v", err)
	}
	defer host.Close()
	if !initialized {
		t.Error("the fallback never reached initialize")
	}
	if len(tools) != 0 {
		t.Errorf("tools = %d, want none from a server that lists none", len(tools))
	}
}
