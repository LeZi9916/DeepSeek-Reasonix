package control

import (
	"sync"
	"testing"
	"time"
)

// A session swap clears the fold and then adopts the new one. A gauge read that
// lands between those two steps sees the whole pre-fold transcript, so a folded
// conversation reports its old size for as long as the swap takes — the "90%
// right after a restart" that falls once the sidecar is in place.
//
// The swap holds swapMu exclusively and every reader takes it shared, so a read
// either finishes before the swap starts or waits for it to end. This asserts
// the gate itself rather than the fold, which is covered in the agent package.
func TestAContextReadWaitsForASessionSwap(t *testing.T) {
	c := &Controller{}
	var (
		mu       sync.Mutex
		observed []string
	)
	record := func(what string) {
		mu.Lock()
		observed = append(observed, what)
		mu.Unlock()
	}

	// The swap: hold it the way resume does, and do work inside it.
	started := make(chan struct{})
	released := make(chan struct{})
	go func() {
		c.swapMu.Lock()
		defer c.swapMu.Unlock()
		close(started)
		<-released
		record("swap done")
	}()
	<-started

	read := make(chan struct{})
	go func() {
		c.swapMu.RLock()
		record("read")
		c.swapMu.RUnlock()
		close(read)
	}()

	// The read must not land while the swap is in progress.
	select {
	case <-read:
		t.Fatal("a context read ran in the middle of a session swap")
	case <-time.After(100 * time.Millisecond):
	}

	close(released)
	select {
	case <-read:
	case <-time.After(5 * time.Second):
		t.Fatal("the read never resumed after the swap finished")
	}

	mu.Lock()
	defer mu.Unlock()
	if len(observed) != 2 || observed[0] != "swap done" || observed[1] != "read" {
		t.Fatalf("order = %v, want the read after the swap", observed)
	}
}
