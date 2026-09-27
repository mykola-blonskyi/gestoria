"use client";

import { useVirtualizer } from "@tanstack/react-virtual";
import { useRef, type ReactNode } from "react";

import { cn } from "@/shared/lib/utils";

type VirtualListProps<T> = {
  items: readonly T[];
  label: string;
  rowHeight: number;
  getKey: (item: T) => string;
  renderRow: (item: T) => ReactNode;
  className?: string;
};

export function VirtualList<T>({ items, label, rowHeight, getKey, renderRow, className }: VirtualListProps<T>) {
  const scrollRef = useRef<HTMLDivElement>(null);
  // React Compiler is not enabled here; this warning only matters once it is.
  // eslint-disable-next-line react-hooks/incompatible-library
  const virtualizer = useVirtualizer({
    count: items.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => rowHeight,
    getItemKey: (index) => getKey(items[index]!),
  });

  return (
    <div
      ref={scrollRef}
      role="region"
      tabIndex={0}
      aria-label={label}
      className={cn("h-96 overflow-auto rounded-lg border border-border", className)}
    >
      <ul role="list" className="relative w-full" style={{ height: virtualizer.getTotalSize() }}>
        {virtualizer.getVirtualItems().map((row) => (
          <li
            key={row.key}
            aria-setsize={items.length}
            aria-posinset={row.index + 1}
            className="absolute top-0 left-0 w-full"
            style={{ height: row.size, transform: `translateY(${row.start}px)` }}
          >
            {renderRow(items[row.index]!)}
          </li>
        ))}
      </ul>
    </div>
  );
}
