package control

import (
	"context"
	"io"
	"testing"
	"time"

	"reasonix/internal/contract/event"
	"reasonix/internal/tools/jobs"
)

// A host whose parent has gone is being replaced, and the installer that asked
// gives it seconds. Waiting out a non-cooperative job's grace is what made an
// update report that it could not close a program the person had already
// closed, so the detached path must return without that wait.
func TestCloseAfterDestroyDoesNotWaitForANonCooperativeJob(t *testing.T) {
	m := jobs.NewManager(event.Discard)
	t.Cleanup(func() {
		// The job ignores cancellation on purpose; force it done so the manager
		// can be torn down without waiting out its own grace here.
		for _, j := range m.Running() {
			m.Kill(j.ID)
		}
		m.CloseWithGrace(0)
	})
	// A job that never answers its context: the exact case Close waits out.
	blocked := make(chan struct{})
	m.Start("task", "deaf", func(context.Context, io.Writer) (string, error) {
		<-blocked
		return "", nil
	})

	c := New(Options{Sink: event.Discard, Jobs: m})
	began := time.Now()
	c.CloseAfterDestroy()
	took := time.Since(began)
	close(blocked)

	if took > 2*time.Second {
		t.Fatalf("CloseAfterDestroy waited %s on a job that ignores cancellation", took)
	}
}
