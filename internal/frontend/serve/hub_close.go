package serve

import (
	"errors"
	"fmt"
	"log/slog"
)

// errRuntimeGone is Close's answer for an id the hub does not hold.
var errRuntimeGone = errors.New("no such runtime")

// Close retires a runtime: the conversation is persisted, the assembly torn
// down, and the session file released for another window to open.
func (h *Hub) Close(id string) error {
	return h.close(id, false)
}

// close retires one runtime. Detached close is for a process that is exiting
// because its parent went away: the conversation is snapshotted and written,
// but a background job's grace window is left to its own cancellation rather
// than waited out, so the process can be replaced promptly.
func (h *Hub) close(id string, detached bool) error {
	h.mu.Lock()
	rt := h.runtimes[id]
	if rt == nil {
		h.mu.Unlock()
		return fmt.Errorf("%w: %s", errRuntimeGone, id)
	}
	delete(h.runtimes, id)
	h.order = removeString(h.order, id)
	h.mu.Unlock()
	defer h.treeChanged()
	defer h.muxes.paneClosed(id)

	if rt.Local() {
		rt.Server.preparations.close()
		rt.Server.terminals.CloseAll()
		rt.Server.bindMu.Lock()
		defer rt.Server.bindMu.Unlock()
	}
	if h.opts.OnClose != nil {
		h.opts.OnClose(rt)
	}
	if rt.stop != nil {
		rt.stop()
	}
	if rt.remote != nil {
		// Releasing the forward first would strand the far session's write lease.
		rt.closeFarRuntime()
		if rt.remote.release != nil {
			rt.remote.release()
		}
		return nil
	}
	if err := rt.Server.Controller().Snapshot(); err != nil {
		slog.Warn("serve: snapshot before closing runtime", "id", id, "err", err)
	}
	if detached {
		rt.Server.Controller().CloseAfterDestroy()
	} else {
		rt.Server.Controller().Close()
	}
	if rt.leases != nil {
		rt.leases.Release()
	}
	return nil
}

// Shutdown closes every runtime, newest first.
func (h *Hub) Shutdown() {
	h.shutdown(false)
}

// ShutdownDetached closes every runtime for a process whose parent has gone.
// The transcript of each pane is written before its controller is released, but
// a background job is not waited out: the window that asked for the exit is
// gone, and an installer replacing this build gives it seconds, not the job
// teardown's grace.
func (h *Hub) ShutdownDetached() {
	h.shutdown(true)
}

func (h *Hub) shutdown(detached bool) {
	defer h.closeLegacyListings()
	for _, view := range h.List() {
		if err := h.close(view.ID, detached); err != nil {
			slog.Warn("serve: close runtime", "id", view.ID, "err", err)
		}
	}
}
