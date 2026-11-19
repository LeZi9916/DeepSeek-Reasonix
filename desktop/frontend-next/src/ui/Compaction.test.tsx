// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import "./testkit";
import { Compaction } from "./Compaction";
import { MockPort } from "../port/mock";
import type { AgentPort, CompactionSettings } from "../port/port";

afterEach(cleanup);

const port = (over: Partial<CompactionSettings> = {}) => {
  const p = new MockPort() as unknown as AgentPort;
  const base = {
    ratio: 0.85, context_window: 1_000_000, trigger: 850000, path: "~/.reasonix/config.toml",
    ...over,
  };
  p.compaction = async () => ({ ...base });
  return p;
};

const slider = () => screen.getByRole("slider", { name: "自动整理阈值" }) as HTMLInputElement;

describe("context maintenance follows the window share and nothing else", () => {
  it("shows where the session folds and the share it comes from", async () => {
    render(<Compaction port={port()} />);
    await screen.findByText("下次整理");
    expect(screen.getAllByText("850k").length).toBeGreaterThan(0);
    expect(screen.getByText(/本会话预计在 850k tokens 左右自动整理上下文/)).toBeTruthy();
    expect(screen.getByText(/模型窗口的 85% 自动整理/)).toBeTruthy();
  });

  it("offers the threshold as a share of the window, not an absolute size", async () => {
    render(<Compaction port={port()} />);
    await screen.findByText("下次整理");
    expect(slider().value).toBe("0.85");
    expect(screen.getByText("85%")).toBeTruthy();
    // No field asks for a token count: the window is declared on the model, and
    // the threshold is only ever a fraction of it.
    expect(screen.queryByRole("spinbutton")).toBeNull();
  });

  it("says nothing folds when no window was declared", async () => {
    render(<Compaction port={port({ context_window: 0, trigger: 0 })} />);
    await screen.findByText(/未声明上下文窗口/);
  });

  it("reports an unreadable configuration", async () => {
    const p = port();
    p.compaction = async () => {
      throw new Error("down");
    };
    render(<Compaction port={p} />);
    await screen.findByText("无法读取压缩配置。");
  });
});

describe("moving the fold threshold", () => {
  it("asks the kernel once, on release, and shows the trigger it answers with", async () => {
    const p = port();
    const save = vi.fn(async (ratio: number) => ({
      ratio, context_window: 1_000_000, trigger: Math.round(1_000_000 * ratio), path: "~/.reasonix/config.toml",
    }));
    p.setCompactRatio = save;
    render(<Compaction port={p} />);
    await screen.findByText("下次整理");

    fireEvent.change(slider(), { target: { value: "0.6" } });
    expect(save).not.toHaveBeenCalled();
    expect(screen.getByText("60%")).toBeTruthy();

    fireEvent.pointerUp(slider());
    expect(save).toHaveBeenCalledTimes(1);
    expect(save).toHaveBeenCalledWith(0.6);
    await screen.findByText(/本会话预计在 600k tokens 左右自动整理上下文/);
  });

  it("keeps the stored share and says why when the kernel refuses it", async () => {
    const p = port();
    p.setCompactRatio = vi.fn(async () => {
      throw Object.assign(new Error("out of range"), {
        reason: { code: "compaction.ratio_out_of_range", error: "out of range" },
      });
    });
    render(<Compaction port={p} />);
    await screen.findByText("下次整理");

    fireEvent.change(slider(), { target: { value: "0.4" } });
    fireEvent.pointerUp(slider());

    // The refused share was never written, so the thumb goes back to 85%.
    await vi.waitFor(() => expect(slider().value).toBe("0.85"));
    expect(screen.getByText("85%")).toBeTruthy();
  });

  // A stored share outside the slider's ends is drawn at the nearest end. A
  // click there settles the control without moving it, and writing the end would
  // be a value the person never chose.
  it("writes nothing when the thumb is released without moving", async () => {
    const p = port({ ratio: 0.99 });
    const save = vi.fn(async (ratio: number) => ({
      ratio, context_window: 1_000_000, trigger: Math.round(1_000_000 * ratio), path: "~/.reasonix/config.toml",
    }));
    p.setCompactRatio = save;
    render(<Compaction port={p} />);
    await screen.findByText("下次整理");

    fireEvent.pointerUp(slider());
    fireEvent.blur(slider());

    expect(save).not.toHaveBeenCalled();
  });

  // Dragging away and back is not a change: a write here would rebuild the
  // runtime to reach the threshold it already had.
  it("writes nothing when the drag ends on the share already in force", async () => {
    const p = port();
    const save = vi.fn(async (ratio: number) => ({
      ratio, context_window: 1_000_000, trigger: Math.round(1_000_000 * ratio), path: "~/.reasonix/config.toml",
    }));
    p.setCompactRatio = save;
    render(<Compaction port={p} />);
    await screen.findByText("下次整理");

    fireEvent.change(slider(), { target: { value: "0.5" } });
    fireEvent.change(slider(), { target: { value: "0.85" } });
    fireEvent.pointerUp(slider());

    expect(save).not.toHaveBeenCalled();
  });

  // A release raises pointerup and then blur, both before React re-renders, so
  // a `busy` state alone lets the second one through and the kernel is asked
  // to rebuild twice for one gesture.
  it("saves once even though release and blur both settle the same drag", async () => {
    const p = port();
    const save = vi.fn(async (ratio: number) => ({
      ratio, context_window: 1_000_000, trigger: Math.round(1_000_000 * ratio), path: "~/.reasonix/config.toml",
    }));
    p.setCompactRatio = save;
    render(<Compaction port={p} />);
    await screen.findByText("下次整理");

    fireEvent.change(slider(), { target: { value: "0.7" } });
    fireEvent.pointerUp(slider());
    fireEvent.blur(slider());

    await vi.waitFor(() => expect(save).toHaveBeenCalledTimes(1));
    expect(save).toHaveBeenCalledWith(0.7);
  });
});
