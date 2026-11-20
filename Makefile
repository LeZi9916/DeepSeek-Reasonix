VERSION := $(shell git describe --tags --always 2>/dev/null | sed 's/^studio-//' || echo dev)
BUILD_TIME_UTC := $(shell date -u +%Y-%m-%dT%H:%M:%SZ)
GIT_COMMIT := $(shell git rev-parse --short=12 HEAD 2>/dev/null || echo unknown)
LDFLAGS := -s -w \
	-X main.version=$(VERSION) \
	-X main.gitCommit=$(GIT_COMMIT) \
	-X main.buildTimeUTC=$(BUILD_TIME_UTC)
GOEXE := $(shell go env GOEXE)
# One pin for the Makefile and the CI lint job; see .github/workflows/ci.yml.
GOLANGCI_VERSION := $(shell cat .golangci-version)

.PHONY: build vet fmt lint lint-go lint-install lint-cross lint-update coverage-gate coverage-gate-update pricecheck acp-conformance check test studio-test sdk-test sdk-test-race hooks cross clean studio studio-dotnet studio-dotnet-test studio-geometry studio-messages studio-messages-check wiregen-check

build:
	CGO_ENABLED=0 go build -ldflags "$(LDFLAGS)" -o bin/reasonix$(GOEXE) ./cmd/reasonix
	CGO_ENABLED=0 go build -ldflags "$(LDFLAGS)" -o bin/reasonix-plugin-example$(GOEXE) ./cmd/reasonix-plugin-example

vet:
	go vet ./...

# Build Studio and launch it. The Electron shell is the one this runs: it is a
# real GUI app on every platform without being bundled first, so there is one
# launch path rather than a macOS one and an everywhere-else one.
studio:
	bash desktop/electron/run-studio.sh


fmt:
	gofmt -w .

# Both gates CI runs, at the version CI pins. Skipping golangci-lint locally
# trades a second here for a ten-minute CI round trip: `modernize` findings in
# particular never surface in `go vet`.
lint: lint-go
	go run ./tools/repolint

lint-go:
	@command -v golangci-lint >/dev/null || { echo "golangci-lint not installed; run: make lint-install"; exit 1; }
	@have=$$(golangci-lint version --short 2>/dev/null); want=$$(echo "$(GOLANGCI_VERSION)" | sed 's/^v//'); \
		[ "$$have" = "$$want" ] || echo "warning: local golangci-lint $$have, CI pins $$want (make lint-install)"
	golangci-lint run --timeout=5m ./...

# CGO_ENABLED=0 keeps the install working where a stray clang on PATH shadows
# the toolchain and breaks runtime/cgo.
lint-install:
	CGO_ENABLED=0 go install github.com/golangci/golangci-lint/v2/cmd/golangci-lint@$(GOLANGCI_VERSION)

# repolint over what the working tree changed. It exists as a target rather than
# a command to paste because the host recognizes `make check` as a verification
# it can read the result of, while an arbitrary `go run ./tools/...` is an
# unknown program it must assume writes — so only this form can be cited as
# evidence that the standards gate passed.
check:
	go run ./tools/repolint -changed

lint-update:
	go run ./tools/repolint -update

# The paths the project declared sensitive, held to the coverage they already
# have. It runs their tests, so it is its own target rather than part of the
# fast `make lint`. -update records the current numbers; lowering one needs
# -allow-drop, the way widening a repolint budget does.
coverage-gate:
	go run ./tools/covergate

coverage-gate-update:
	go run ./tools/covergate -update

# The vendors publish their rates on pages this reads and compares the table
# against. It reaches the network, so it stays out of lint and out of CI: run it
# before touching a rate and when a vendor announces a change. A page it cannot
# read exits non-zero — unread never counts as agreement.
pricecheck:
	go run ./tools/pricecheck

# Linting one GOOS leaves every //go:build windows and darwin file unchecked.
lint-cross:
	@for t in "linux ." "darwin ." "windows ." "linux desktop" "windows desktop"; do \
		set -- $$t; \
		echo "== golangci-lint GOOS=$$1 ($$2)"; \
		(cd $$2 && GOOS=$$1 golangci-lint run --timeout=5m ./...) || exit 1; \
	done

# Every module, because a nested one is where a red test hides longest: the
# shell's route allowlist sat broken through a release, and `go test ./...` from
# the root cannot see it.
test: studio-test sdk-test
	go test ./...

# Drives `reasonix acp` with the official ACP TypeScript SDK and holds every
# message it writes to the schema that SDK ships. Needs node and network for
# `npm ci`, which runs without install scripts against a lockfile.
acp-conformance: build
	cd tools/acp-conformance && npm ci --ignore-scripts --no-audit --no-fund
	cd tools/acp-conformance && REASONIX_BIN=$(CURDIR)/bin/reasonix$(GOEXE) npm test

# The shell is a nested module, so the root `test` target never reaches it.
studio-test:
	cd desktop && go test ./...

# The C# shell is a separate solution, so neither `go test` nor `make lint`
# reaches it either. These two targets are what a local run and CI both call, so
# the build flags and the test filter are written once.
studio-dotnet:
	dotnet build desktop/avalonia/Reasonix.Studio.slnx -c Debug

studio-dotnet-test:
	dotnet test desktop/avalonia/Reasonix.Studio.slnx -c Debug --no-build

# The geometry gate alone: renders the real window against the captured
# conversation and holds it there. It runs inside the test project, so the
# solution has to be built first; this target exists so a change to the shell
# can be checked against the baseline without running the suite's live-kernel
# cases, and so CI cites one command rather than a filter someone retypes.
studio-geometry: studio-dotnet
	dotnet test desktop/avalonia/Reasonix.Studio.slnx -c Debug --no-build --filter "FullyQualifiedName~GeometryGateTests|FullyQualifiedName~GeometryCoverageTests|FullyQualifiedName~CoverageAuditTests|FullyQualifiedName~UncomparableAuditTests"

# The interface's wording lives in the reference frontend's own tables. The
# shell's copies are generated from them, so this check is what notices an edit
# on either side that nobody propagated: a key added there and not here, or a
# phrase changed in one place only.
studio-messages:
	node desktop/avalonia/tools/render-messages.mjs

studio-messages-check:
	node desktop/avalonia/tools/render-messages.mjs -check

# The shell's wire records are generated from the kernel's Go structs, so a
# source edit without a regeneration leaves a contract the shell reads a field
# short. This is the cheap half of the check; repolint runs the same comparison
# over fields rather than bytes.
wiregen-check:
	go run ./tools/wiregen -check

sdk-test:
	cd sdk/go && go test ./...

sdk-test-race:
	cd sdk/go && go test -race ./...

hooks:
	@git config core.hooksPath .githooks
	@echo "installed: core.hooksPath -> .githooks (pre-push runs go vet)"

cross:
	@mkdir -p dist
	@for p in darwin/amd64 darwin/arm64 linux/amd64 linux/arm64 windows/amd64 windows/arm64; do \
		os=$${p%/*}; arch=$${p#*/}; ext=; [ $$os = windows ] && ext=.exe; \
		echo "build $$os/$$arch"; \
		CGO_ENABLED=0 GOOS=$$os GOARCH=$$arch go build -ldflags "$(LDFLAGS)" -o dist/reasonix-$$os-$$arch$$ext ./cmd/reasonix; \
	done

clean:
	rm -rf bin dist
